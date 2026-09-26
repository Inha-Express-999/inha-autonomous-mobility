using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
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
    public sealed class WebSocketClientDataSource : IClientDataSource, IClientCommandSource,
        IEgoLocalizationSource, ISensorObservationSource
    {
        private const int MaxMessageBytes = 16 * 1024 * 1024;
        private const int MaxPendingCommands = 64;
        private const double InitialReconnectDelayS = 0.5;
        private const double MaxReconnectDelayS = 15.0;
        private const double ConnectionHandshakeTimeoutS = 10.0;
        private static readonly JsonSerializerSettings JsonSettings = CreateJsonSettings();
        private readonly Uri endpoint;
        private readonly ClientRole role;
        private readonly string projectVersion;
        private readonly string subscriberId;
        private readonly string telemetrySessionId = Guid.NewGuid().ToString("N");
        public string TelemetrySessionId => telemetrySessionId;
        private readonly CancellationTokenSource cancellation = new CancellationTokenSource();
        private readonly ConcurrentQueue<InboundMessage> inbound = new ConcurrentQueue<InboundMessage>();
        private readonly ConcurrentQueue<ConnectionFailure> failures = new ConcurrentQueue<ConnectionFailure>();
        private readonly Queue<string> outbound = new Queue<string>();
        private readonly Dictionary<string, string> pendingCommands = new Dictionary<string, string>();
        private readonly object telemetryLock = new object();
        private readonly Dictionary<string, string> latestTelemetryByVehicle = new Dictionary<string, string>();
        private readonly Dictionary<string, long> lastTelemetryTickByVehicle = new Dictionary<string, long>();
        private readonly Dictionary<string, long> acknowledgedTelemetryTickByVehicle = new Dictionary<string, long>();
        private readonly Dictionary<string, string> latestSensorTelemetryByKey = new Dictionary<string, string>();
        private readonly Dictionary<string, long> latestSensorPoseTickByKey = new Dictionary<string, long>();
        private readonly Dictionary<string, long> lastSensorTickByKey = new Dictionary<string, long>();
        private ClientWebSocket socket;
        private bool sending;
        private bool started;
        private bool disposed;
        private bool hasConnected;
        private bool connectAttemptActive;
        private int connectionGeneration;
        private int reconnectAttempt;
        private double lastNow;
        private double reconnectAtS;
        private double connectAttemptStartedAtS;

        public ConnectionState ConnectionState { get; private set; } = ConnectionState.Disconnected;
        public event Action<WorldSnapshotDto, double> SnapshotReceived;
        public event Action<ServiceCommandAckDto> CommandAcknowledged;
        public event Action<EgoLocalizationAckDto> EgoLocalizationAcknowledged;
        public event Action<SensorObservationAckDto> SensorObservationAcknowledged;

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
            BeginConnect();
        }

        public void Pump(double monotonicNowS)
        {
            CheckClock(monotonicNowS);
            lastNow = monotonicNowS;
            while (failures.TryDequeue(out var failure))
            {
                if (failure.Generation != connectionGeneration) continue;
                ScheduleReconnect(failure.Reason);
            }
            while (inbound.TryDequeue(out var inboundMessage))
            {
                if (inboundMessage.Generation != connectionGeneration) continue;
                try
                {
                    var envelope = JObject.Parse(inboundMessage.Text);
                    var type = (string)envelope["type"];
                    if (type == "connected")
                    {
                        if ((int?)envelope["schemaVersion"] != WorldSnapshotDto.SupportedSchemaVersion)
                            throw new JsonSerializationException("Server schema version is incompatible.");
                        hasConnected = true;
                        connectAttemptActive = false;
                        reconnectAttempt = 0;
                        ConnectionState = ConnectionState.Connected;
                        ReplayPendingCommands();
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
                        var messageId = (string)envelope["messageId"];
                        if (!string.IsNullOrEmpty(messageId)) pendingCommands.Remove(messageId);
                        var ack = new ServiceCommandAckDto(
                            messageId,
                            (string)envelope["commandType"],
                            (bool)envelope["accepted"],
                            envelope["request"]?.Type == JTokenType.Null
                                ? null
                                : envelope["request"]?.ToObject<RequestDto>(JsonSerializer.Create(JsonSettings)),
                            (string)envelope["errorCode"],
                            (string)envelope["errorMessage"]);
                        CommandAcknowledged?.Invoke(ack);
                    }
                    else if (type == "ego_localization_ack")
                    {
                        var ack = new EgoLocalizationAckDto(
                            (string)envelope["vehicleId"],
                            (string)envelope["sessionId"],
                            (long)envelope["observedTick"],
                            (bool)envelope["accepted"],
                            (string)envelope["errorCode"]);
                        if (ack.Accepted) acknowledgedTelemetryTickByVehicle[ack.VehicleId] = ack.ObservedTick;
                        EgoLocalizationAcknowledged?.Invoke(ack);
                    }
                    else if (type == "sensor_observation_ack")
                    {
                        var ack = new SensorObservationAckDto(
                            (string)envelope["vehicleId"],
                            (string)envelope["sensorId"],
                            (string)envelope["sessionId"],
                            (long)envelope["observedTick"],
                            (bool)envelope["accepted"],
                            (string)envelope["errorCode"]);
                        SensorObservationAcknowledged?.Invoke(ack);
                    }
                    else if (type == "error")
                    {
                        throw new JsonSerializationException("Server rejected the session: " + (string)envelope["code"]);
                    }
                }
                catch (Exception error)
                {
                    Debug.LogWarning("Discarding invalid server message: " + error.Message);
                    ScheduleReconnect("invalid server message: " + error.Message);
                    break;
                }
            }
            FlushOutbound();
            if (connectAttemptActive && ConnectionState != ConnectionState.Connected &&
                monotonicNowS - connectAttemptStartedAtS >= ConnectionHandshakeTimeoutS)
            {
                ScheduleReconnect("connection handshake timed out");
            }
            if (started && !disposed && ConnectionState == ConnectionState.Reconnecting &&
                !connectAttemptActive && monotonicNowS >= reconnectAtS)
            {
                BeginConnect();
            }
        }

        public void SendPassengerRequest(PassengerRequestCommandDto command)
        {
            if (command == null) throw new ArgumentNullException(nameof(command));
            if (role != ClientRole.Mobile_Passenger && role != ClientRole.PC_Operator)
                throw new InvalidOperationException("This role cannot create passenger requests.");
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

        public void SendCargoRequest(CargoRequestCommandDto command)
        {
            if (command == null) throw new ArgumentNullException(nameof(command));
            if (role != ClientRole.PC_Operator)
                throw new InvalidOperationException("Only the operator session may create cargo requests.");
            EnsureConnected();
            QueueOutbound(new JObject
            {
                ["type"] = "create_request",
                ["messageId"] = command.MessageId,
                ["serviceType"] = "CARGO",
                ["pickupLandmarkId"] = command.PickupLandmarkId,
                ["dropoffLandmarkId"] = command.DropoffLandmarkId,
                ["partySize"] = 0,
                ["cargoKg"] = command.CargoKg,
                ["latestArrivalS"] = command.LatestArrivalS.HasValue
                    ? new JValue(command.LatestArrivalS.Value)
                    : JValue.CreateNull()
            });
        }

        public void SendCancelRequest(CancelRequestCommandDto command)
        {
            if (command == null) throw new ArgumentNullException(nameof(command));
            if (role != ClientRole.Mobile_Passenger && role != ClientRole.PC_Operator)
                throw new InvalidOperationException("This role cannot cancel service requests.");
            EnsureConnected();
            QueueOutbound(new JObject
            {
                ["type"] = "cancel_request",
                ["messageId"] = command.MessageId,
                ["requestId"] = command.RequestId
            });
        }

        public void SendEgoLocalization(EgoLocalizationDto observation)
        {
            if (observation == null) throw new ArgumentNullException(nameof(observation));
            if (role != ClientRole.PC_Operator)
                throw new InvalidOperationException("Only the Unity simulation session may report ego localization.");
            if (disposed) throw new ObjectDisposedException(nameof(WebSocketClientDataSource));
            if (!started) throw new InvalidOperationException("Start the data source before reporting localization.");
            if (lastTelemetryTickByVehicle.TryGetValue(observation.VehicleId, out var previousTick) &&
                observation.ObservedTick <= previousTick)
                throw new ArgumentOutOfRangeException(nameof(observation), "Observed ticks must increase per vehicle.");

            lastTelemetryTickByVehicle[observation.VehicleId] = observation.ObservedTick;
            var message = new JObject
            {
                ["type"] = "ego_localization",
                ["vehicleId"] = observation.VehicleId,
                ["sessionId"] = telemetrySessionId,
                ["observedTick"] = observation.ObservedTick,
                ["mapVersion"] = observation.MapVersion,
                ["position"] = new JObject
                {
                    ["x"] = observation.Position.X,
                    ["y"] = observation.Position.Y,
                    ["z"] = observation.Position.Z
                },
                ["headingRad"] = observation.HeadingRad,
                ["speedMps"] = observation.SpeedMps
            }.ToString(Formatting.None);
            lock (telemetryLock) latestTelemetryByVehicle[observation.VehicleId] = message;
            FlushOutbound();
        }

        public void SendSensorObservation(SensorObservationDto observation)
        {
            if (observation == null) throw new ArgumentNullException(nameof(observation));
            if (role != ClientRole.PC_Operator)
                throw new InvalidOperationException("Only the Unity simulation session may send sensor observations.");
            if (disposed) throw new ObjectDisposedException(nameof(WebSocketClientDataSource));
            if (!started) throw new InvalidOperationException("Start the data source before sending observations.");
            if (!lastTelemetryTickByVehicle.TryGetValue(observation.VehicleId, out var latestPoseTick) ||
                observation.EgoPoseTick != latestPoseTick)
                throw new ArgumentException("Sensor frames must reference the latest reported ego pose.", nameof(observation));

            var key = observation.VehicleId + "\n" + observation.SensorId;
            if (lastSensorTickByKey.TryGetValue(key, out var previousTick) &&
                observation.ObservedTick <= previousTick)
                throw new ArgumentOutOfRangeException(nameof(observation), "Sensor ticks must increase per vehicle and sensor.");
            lastSensorTickByKey[key] = observation.ObservedTick;

            var detections = new JArray();
            foreach (var detection in observation.Detections)
            {
                detections.Add(new JObject
                {
                    ["rangeM"] = detection.RangeM,
                    ["bearingRad"] = detection.BearingRad,
                    ["localPositionM"] = new JObject
                    {
                        ["x"] = detection.LocalPositionM.X,
                        ["y"] = detection.LocalPositionM.Y,
                        ["z"] = detection.LocalPositionM.Z
                    },
                    ["entityClass"] = detection.EntityClass.ToString(),
                    ["entityId"] = detection.EntityId == null ? JValue.CreateNull() : new JValue(detection.EntityId),
                    ["relativeSpeedMps"] = detection.RelativeSpeedMps.HasValue
                        ? new JValue(detection.RelativeSpeedMps.Value)
                        : JValue.CreateNull()
                });
            }
            var message = new JObject
            {
                ["type"] = "sensor_observation",
                ["vehicleId"] = observation.VehicleId,
                ["sensorId"] = observation.SensorId,
                ["sensorType"] = observation.SensorType.ToString(),
                ["sessionId"] = telemetrySessionId,
                ["mapVersion"] = observation.MapVersion,
                ["observedTick"] = observation.ObservedTick,
                ["egoPoseTick"] = observation.EgoPoseTick,
                ["valid"] = observation.Valid,
                ["observedTimeS"] = observation.ObservedTimeS.HasValue
                    ? new JValue(observation.ObservedTimeS.Value) : JValue.CreateNull(),
                ["sensorPositionM"] = observation.SensorPositionM.HasValue ? new JObject
                {
                    ["x"] = observation.SensorPositionM.Value.X,
                    ["y"] = observation.SensorPositionM.Value.Y,
                    ["z"] = observation.SensorPositionM.Value.Z
                } : JValue.CreateNull(),
                ["sensorHeadingRad"] = observation.SensorHeadingRad.HasValue
                    ? new JValue(observation.SensorHeadingRad.Value) : JValue.CreateNull(),
                ["detections"] = detections,
                ["rayMaxRangeM"] = observation.RayMaxRangeM.HasValue
                    ? new JValue(observation.RayMaxRangeM.Value) : JValue.CreateNull(),
                ["rays"] = new JArray(System.Linq.Enumerable.Select(observation.Rays, ray => new JObject
                {
                    ["bearingRad"] = ray.BearingRad,
                    ["outcome"] = ray.Outcome,
                    ["rangeM"] = ray.RangeM.HasValue ? new JValue(ray.RangeM.Value) : JValue.CreateNull()
                }))
            }.ToString(Formatting.None);
            lock (telemetryLock)
            {
                latestSensorTelemetryByKey[key] = message;
                latestSensorPoseTickByKey[key] = observation.EgoPoseTick;
            }
            FlushOutbound();
        }

        private void EnsureConnected()
        {
            if (disposed) throw new ObjectDisposedException(nameof(WebSocketClientDataSource));
            if (ConnectionState != ConnectionState.Connected || socket.State != WebSocketState.Open)
                throw new InvalidOperationException("The WebSocket session is not connected.");
        }

        private void QueueOutbound(JObject message)
        {
            var messageId = (string)message["messageId"];
            if (string.IsNullOrWhiteSpace(messageId))
                throw new ArgumentException("Commands need a stable message ID.", nameof(message));
            var serialized = message.ToString(Formatting.None);
            if (pendingCommands.TryGetValue(messageId, out var existing))
            {
                if (existing != serialized)
                    throw new InvalidOperationException("A pending message ID cannot be reused with another payload.");
                return;
            }
            if (pendingCommands.Count >= MaxPendingCommands)
                throw new InvalidOperationException("The client command queue is full.");
            pendingCommands.Add(messageId, serialized);
            outbound.Enqueue(serialized);
        }

        private void FlushOutbound()
        {
            if (sending || ConnectionState != ConnectionState.Connected ||
                socket == null || socket.State != WebSocketState.Open) return;

            string message = null;
            string telemetryKey = null;
            bool isTelemetry = false;
            bool isSensorTelemetry = false;
            if (outbound.Count > 0)
            {
                message = outbound.Dequeue();
            }
            else
            {
                lock (telemetryLock)
                {
                    string staleSensorKey = null;
                    foreach (var item in latestSensorTelemetryByKey)
                    {
                        var separator = item.Key.IndexOf('\n');
                        var vehicleId = separator < 0 ? item.Key : item.Key.Substring(0, separator);
                        if (!acknowledgedTelemetryTickByVehicle.TryGetValue(vehicleId, out var ackedTick))
                            continue;
                        if (ackedTick > latestSensorPoseTickByKey[item.Key])
                        {
                            staleSensorKey = item.Key;
                            break;
                        }
                        if (ackedTick != latestSensorPoseTickByKey[item.Key]) continue;
                        telemetryKey = item.Key;
                        message = item.Value;
                        isTelemetry = true;
                        isSensorTelemetry = true;
                        break;
                    }
                    if (staleSensorKey != null)
                    {
                        latestSensorTelemetryByKey.Remove(staleSensorKey);
                        latestSensorPoseTickByKey.Remove(staleSensorKey);
                    }
                    if (message == null && latestTelemetryByVehicle.Count > 0)
                        foreach (var item in latestTelemetryByVehicle)
                        {
                            telemetryKey = item.Key;
                            message = item.Value;
                            isTelemetry = true;
                            break;
                        }
                }
            }
            if (message == null) return;
            sending = true;
            _ = SendAsync(message, socket, connectionGeneration, isTelemetry,
                isSensorTelemetry, telemetryKey, cancellation.Token);
        }

        private async Task SendAsync(string message, ClientWebSocket target, int generation,
            bool isTelemetry, bool isSensorTelemetry, string telemetryKey, CancellationToken token)
        {
            bool sent = false;
            try
            {
                var bytes = Encoding.UTF8.GetBytes(message);
                await target.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, token);
                sent = true;
            }
            catch (OperationCanceledException) { }
            catch (Exception error)
            {
                failures.Enqueue(new ConnectionFailure(generation, error.Message));
            }
            finally
            {
                if (generation == connectionGeneration)
                {
                    if (sent && isTelemetry)
                    {
                        lock (telemetryLock)
                        {
                            var telemetry = isSensorTelemetry
                                ? latestSensorTelemetryByKey
                                : latestTelemetryByVehicle;
                            if (telemetry.TryGetValue(telemetryKey, out var latest) && latest == message)
                            {
                                telemetry.Remove(telemetryKey);
                                if (isSensorTelemetry) latestSensorPoseTickByKey.Remove(telemetryKey);
                            }
                        }
                    }
                    sending = false;
                }
            }
        }

        private void BeginConnect()
        {
            if (disposed || cancellation.IsCancellationRequested || connectAttemptActive) return;
            var target = new ClientWebSocket();
            socket = target;
            var generation = ++connectionGeneration;
            connectAttemptActive = true;
            connectAttemptStartedAtS = lastNow;
            ConnectionState = hasConnected ? ConnectionState.Reconnecting : ConnectionState.Connecting;
            _ = ConnectAsync(target, generation, cancellation.Token);
        }

        private async Task ConnectAsync(ClientWebSocket target, int generation, CancellationToken token)
        {
            try
            {
                await target.ConnectAsync(endpoint, token);
                var subscribe = new JObject
                {
                    ["type"] = "subscribe",
                    ["schemaVersion"] = WorldSnapshotDto.SupportedSchemaVersion,
                    ["projectVersion"] = projectVersion,
                    ["role"] = role.ToString(),
                    ["subscriberId"] = subscriberId == null ? JValue.CreateNull() : new JValue(subscriberId)
                };
                var bytes = Encoding.UTF8.GetBytes(subscribe.ToString(Formatting.None));
                await target.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, token);
                _ = ReceiveLoopAsync(target, generation, token);
            }
            catch (OperationCanceledException) { }
            catch (Exception error)
            {
                failures.Enqueue(new ConnectionFailure(generation, error.Message));
            }
        }

        private async Task ReceiveLoopAsync(ClientWebSocket target, int generation,
            CancellationToken token)
        {
            var buffer = new byte[8192];
            try
            {
                while (!token.IsCancellationRequested && target.State == WebSocketState.Open)
                {
                    using (var message = new MemoryStream())
                    {
                        WebSocketReceiveResult result;
                        do
                        {
                            result = await target.ReceiveAsync(new ArraySegment<byte>(buffer), token);
                            if (result.MessageType == WebSocketMessageType.Close)
                            {
                                failures.Enqueue(new ConnectionFailure(generation, "server closed the WebSocket"));
                                return;
                            }
                            if (result.MessageType != WebSocketMessageType.Text)
                                throw new WebSocketException("Only UTF-8 text messages are supported.");
                            if (message.Length + result.Count > MaxMessageBytes)
                                throw new WebSocketException("Server message exceeds the configured size limit.");
                            message.Write(buffer, 0, result.Count);
                        } while (!result.EndOfMessage);
                        inbound.Enqueue(new InboundMessage(generation, Encoding.UTF8.GetString(message.ToArray())));
                    }
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception error)
            {
                if (!token.IsCancellationRequested)
                    failures.Enqueue(new ConnectionFailure(generation, error.Message));
            }
        }

        private void ReplayPendingCommands()
        {
            outbound.Clear();
            foreach (var command in pendingCommands.Values) outbound.Enqueue(command);
        }

        private void ScheduleReconnect(string reason)
        {
            if (disposed || !started) return;
            Debug.LogWarning("Server connection ended; retrying: " + reason);
            socket?.Abort();
            socket?.Dispose();
            socket = null;
            connectionGeneration++;
            connectAttemptActive = false;
            sending = false;
            ConnectionState = ConnectionState.Reconnecting;
            acknowledgedTelemetryTickByVehicle.Clear();
            lock (telemetryLock)
            {
                latestSensorTelemetryByKey.Clear();
                latestSensorPoseTickByKey.Clear();
            }
            var delay = Math.Min(InitialReconnectDelayS * Math.Pow(2, reconnectAttempt), MaxReconnectDelayS);
            reconnectAttempt = Math.Min(reconnectAttempt + 1, 10);
            reconnectAtS = lastNow + delay;
        }

        private sealed class InboundMessage
        {
            public int Generation { get; }
            public string Text { get; }
            public InboundMessage(int generation, string text) { Generation = generation; Text = text; }
        }

        private sealed class ConnectionFailure
        {
            public int Generation { get; }
            public string Reason { get; }
            public ConnectionFailure(int generation, string reason) { Generation = generation; Reason = reason; }
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
            connectionGeneration++;
            socket?.Abort();
            socket?.Dispose();
            socket = null;
            outbound.Clear();
            pendingCommands.Clear();
            lock (telemetryLock) latestTelemetryByVehicle.Clear();
            lock (telemetryLock)
            {
                latestSensorTelemetryByKey.Clear();
                latestSensorPoseTickByKey.Clear();
            }
            cancellation.Dispose();
            SnapshotReceived = null;
            CommandAcknowledged = null;
            EgoLocalizationAcknowledged = null;
            SensorObservationAcknowledged = null;
        }
    }
}
