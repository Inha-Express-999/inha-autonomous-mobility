using System;
using System.Collections.Concurrent;
using System.IO;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using InhaExpress.Client.Domain;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json.Serialization;
using UnityEngine;

namespace InhaExpress.Client.Networking
{
    // Prototype snapshot transport. Network callbacks are queued and surfaced by Pump on Unity's main thread.
    public sealed class WebSocketClientDataSource : IClientDataSource, IClientCommandSource
    {
        private const int MaxMessageBytes = 16 * 1024 * 1024;
        private static readonly JsonSerializerSettings JsonSettings = CreateJsonSettings();
        private readonly Uri endpoint;
        private readonly ClientRole role;
        private readonly string projectVersion;
        private readonly string subscriberId;
        private readonly ClientWebSocket socket = new ClientWebSocket();
        private readonly CancellationTokenSource cancellation = new CancellationTokenSource();
        private readonly ConcurrentQueue<string> inbound = new ConcurrentQueue<string>();
        private readonly ConcurrentQueue<string> outbound = new ConcurrentQueue<string>();
        private readonly ConcurrentQueue<string> failures = new ConcurrentQueue<string>();
        private int outboundCount;
        private bool sending;
        private bool started;
        private bool disposed;
        private double lastNow;

        public ConnectionState ConnectionState { get; private set; } = ConnectionState.Disconnected;
        public event Action<WorldSnapshotDto, double> SnapshotReceived;
        public event Action<ServiceCommandAckDto> CommandAcknowledged;

        public WebSocketClientDataSource(string url, ClientRole role, string projectVersion,
            string subscriberId = null)
        {
            if (!Uri.TryCreate(url, UriKind.Absolute, out var parsedEndpoint) ||
                (parsedEndpoint.Scheme != "ws" && parsedEndpoint.Scheme != "wss"))
                throw new ArgumentException("Use an absolute ws:// or wss:// URL.", nameof(url));
            endpoint = parsedEndpoint;
            if (!Enum.IsDefined(typeof(ClientRole), role)) throw new ArgumentOutOfRangeException(nameof(role));
            if (role == ClientRole.Mobile_Passenger && string.IsNullOrWhiteSpace(subscriberId))
                throw new ArgumentException("Mobile subscriptions need a subscriber ID.", nameof(subscriberId));
            if (role == ClientRole.PC_Operator && subscriberId != null)
                throw new ArgumentException("Operator subscriptions cannot have a subscriber ID.", nameof(subscriberId));
            this.role = role;
            this.projectVersion = projectVersion;
            this.subscriberId = subscriberId;
        }

        public void Start(double monotonicNowS)
        {
            CheckClock(monotonicNowS);
            if (started) return;
            started = true;
            lastNow = monotonicNowS;
            ConnectionState = ConnectionState.Connecting;
            _ = ConnectAsync(cancellation.Token);
        }

        public void Pump(double monotonicNowS)
        {
            CheckClock(monotonicNowS);
            lastNow = monotonicNowS;
            while (failures.TryDequeue(out var failure))
            {
                ConnectionState = ConnectionState.Disconnected;
                Debug.LogWarning("Server connection ended: " + failure);
            }
            while (inbound.TryDequeue(out var message))
            {
                try
                {
                    var envelope = JObject.Parse(message);
                    var type = (string)envelope["type"];
                    if (type == "connected")
                    {
                        if ((int?)envelope["schemaVersion"] != WorldSnapshotDto.SupportedSchemaVersion)
                            throw new JsonSerializationException("Server schema version is incompatible.");
                        ConnectionState = ConnectionState.Connected;
                    }
                    else if (type == "snapshot")
                    {
                        var snapshot = envelope["snapshot"]?.ToObject<WorldSnapshotDto>(JsonSerializer.Create(JsonSettings));
                        if (snapshot == null) throw new JsonSerializationException("Snapshot payload is empty.");
                        if (snapshot.Role != role || snapshot.SubscriberId != subscriberId)
                            throw new JsonSerializationException("Snapshot subscription does not match this client.");
                        ConnectionState = ConnectionState.Connected;
                        SnapshotReceived?.Invoke(snapshot, monotonicNowS);
                    }
                    else if (type == "command_ack")
                    {
                        var ack = new ServiceCommandAckDto(
                            (string)envelope["messageId"],
                            (string)envelope["commandType"],
                            (bool)envelope["accepted"],
                            envelope["request"]?.Type == JTokenType.Null
                                ? null
                                : envelope["request"]?.ToObject<RequestDto>(JsonSerializer.Create(JsonSettings)),
                            (string)envelope["errorCode"],
                            (string)envelope["errorMessage"]);
                        CommandAcknowledged?.Invoke(ack);
                    }
                    else if (type == "error")
                    {
                        throw new JsonSerializationException("Server rejected the session: " + (string)envelope["code"]);
                    }
                }
                catch (Exception error)
                {
                    ConnectionState = ConnectionState.Disconnected;
                    Debug.LogWarning("Discarding invalid server message: " + error.Message);
                    socket.Abort();
                    break;
                }
            }
            FlushOutbound();
        }

        public void SendPassengerRequest(PassengerRequestCommandDto command)
        {
            if (command == null) throw new ArgumentNullException(nameof(command));
            if (role != ClientRole.Mobile_Passenger)
                throw new InvalidOperationException("Only a passenger session may create passenger requests.");
            EnsureConnected();
            QueueOutbound(new JObject
            {
                ["type"] = "create_request",
                ["messageId"] = command.MessageId,
                ["serviceType"] = "PASSENGER",
                ["pickupLandmarkId"] = command.PickupLandmarkId,
                ["dropoffLandmarkId"] = command.DropoffLandmarkId,
                ["serviceNeeds"] = new JObject
                {
                    ["requiresStepFree"] = command.ServiceNeeds.RequiresStepFree,
                    ["wheelchairSlots"] = command.ServiceNeeds.WheelchairSlots,
                    ["boardingAssistance"] = command.ServiceNeeds.BoardingAssistance
                },
                ["partySize"] = command.PartySize,
                ["latestArrivalS"] = command.LatestArrivalS.HasValue
                    ? new JValue(command.LatestArrivalS.Value)
                    : JValue.CreateNull()
            });
        }

        public void SendCancelRequest(CancelRequestCommandDto command)
        {
            if (command == null) throw new ArgumentNullException(nameof(command));
            if (role != ClientRole.Mobile_Passenger)
                throw new InvalidOperationException("Only a passenger session may cancel passenger requests.");
            EnsureConnected();
            QueueOutbound(new JObject
            {
                ["type"] = "cancel_request",
                ["messageId"] = command.MessageId,
                ["requestId"] = command.RequestId
            });
        }

        private void EnsureConnected()
        {
            if (disposed) throw new ObjectDisposedException(nameof(WebSocketClientDataSource));
            if (ConnectionState != ConnectionState.Connected || socket.State != WebSocketState.Open)
                throw new InvalidOperationException("The WebSocket session is not connected.");
        }

        private void QueueOutbound(JObject message)
        {
            if (Interlocked.Increment(ref outboundCount) > 64)
            {
                Interlocked.Decrement(ref outboundCount);
                throw new InvalidOperationException("The client command queue is full.");
            }
            outbound.Enqueue(message.ToString(Formatting.None));
        }

        private void FlushOutbound()
        {
            if (sending || socket.State != WebSocketState.Open || !outbound.TryDequeue(out var message)) return;
            Interlocked.Decrement(ref outboundCount);
            sending = true;
            _ = SendAsync(message, cancellation.Token);
        }

        private async Task SendAsync(string message, CancellationToken token)
        {
            try
            {
                var bytes = Encoding.UTF8.GetBytes(message);
                await socket.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, token);
            }
            catch (OperationCanceledException) { }
            catch (Exception error)
            {
                failures.Enqueue(error.Message);
            }
            finally
            {
                sending = false;
            }
        }

        private async Task ConnectAsync(CancellationToken token)
        {
            try
            {
                await socket.ConnectAsync(endpoint, token);
                var subscribe = new JObject
                {
                    ["type"] = "subscribe",
                    ["schemaVersion"] = WorldSnapshotDto.SupportedSchemaVersion,
                    ["projectVersion"] = projectVersion,
                    ["role"] = role.ToString(),
                    ["subscriberId"] = subscriberId == null ? JValue.CreateNull() : new JValue(subscriberId)
                };
                var bytes = Encoding.UTF8.GetBytes(subscribe.ToString(Formatting.None));
                await socket.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, token);
                _ = ReceiveLoopAsync(token);
            }
            catch (OperationCanceledException) { }
            catch (Exception error)
            {
                failures.Enqueue(error.Message);
            }
        }

        private async Task ReceiveLoopAsync(CancellationToken token)
        {
            var buffer = new byte[8192];
            try
            {
                while (!token.IsCancellationRequested && socket.State == WebSocketState.Open)
                {
                    using (var message = new MemoryStream())
                    {
                        WebSocketReceiveResult result;
                        do
                        {
                            result = await socket.ReceiveAsync(new ArraySegment<byte>(buffer), token);
                            if (result.MessageType == WebSocketMessageType.Close)
                            {
                                failures.Enqueue("server closed the WebSocket");
                                return;
                            }
                            if (result.MessageType != WebSocketMessageType.Text)
                                throw new WebSocketException("Only UTF-8 text messages are supported.");
                            if (message.Length + result.Count > MaxMessageBytes)
                                throw new WebSocketException("Server message exceeds the configured size limit.");
                            message.Write(buffer, 0, result.Count);
                        } while (!result.EndOfMessage);
                        inbound.Enqueue(Encoding.UTF8.GetString(message.ToArray()));
                    }
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception error)
            {
                if (!token.IsCancellationRequested) failures.Enqueue(error.Message);
            }
        }

        private static JsonSerializerSettings CreateJsonSettings()
        {
            var settings = new JsonSerializerSettings
            {
                ContractResolver = new CamelCasePropertyNamesContractResolver(),
                NullValueHandling = NullValueHandling.Include,
                MissingMemberHandling = MissingMemberHandling.Error
            };
            settings.Converters.Add(new StringEnumConverter());
            return settings;
        }

        private void CheckClock(double now)
        {
            if (disposed) throw new ObjectDisposedException(nameof(WebSocketClientDataSource));
            if (double.IsNaN(now) || double.IsInfinity(now) || now < 0 || (started && now < lastNow))
                throw new ArgumentOutOfRangeException(nameof(now), "Use a monotonic real-time clock.");
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            ConnectionState = ConnectionState.Disconnected;
            cancellation.Cancel();
            socket.Abort();
            socket.Dispose();
            cancellation.Dispose();
            SnapshotReceived = null;
            CommandAcknowledged = null;
        }
    }
}
