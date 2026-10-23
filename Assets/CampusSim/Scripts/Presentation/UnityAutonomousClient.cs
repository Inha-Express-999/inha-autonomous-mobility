using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using InhaExpress.Client.MapClient;
using UnityEngine;
using UnityEngine.Networking;

namespace InhaExpress.Client.Presentation
{
    /// <summary>PC-only ego localization + anonymous Raycast observations -> Python -> existing Physics controller.</summary>
    public sealed class UnityAutonomousClient : MonoBehaviour
    {
        [Serializable] private sealed class Ray { public float range_m, bearing_rad, lateral_m, forward_m, origin_forward_m; }
        [Serializable] private sealed class Observation
        {
            public int schema_version = 2, tick;
            public string run_id, geometry_hash;
            public float x, z, heading_rad, speed_mps, wheelbase_m, max_steering_rad, sensor_age_s;
            public float half_width_m;
            public bool sensor_valid;
            public Ray[] rays;
        }
        [Serializable] private sealed class Command
        {
            public int schema_version, tick;
            public string run_id, status, reason;
            public float target_speed_kmh, steering_angle_rad, valid_for_s;
            public bool emergency_stop;
            public float lane_error_m;
            public string lane_profile_hash;
        }
        [Serializable] private sealed class Mission { public string run_id, command_id, stop_id; }
        [Serializable] private sealed class MissionReceipt { public Vector3 goal_unity; public string lane_profile_hash; }
        private sealed class Response { public int generation; public string json; }
        private CampusServerMap map;
        private IAutonomousVehicle vehicle;
        private CancellationTokenSource cancellation;
        private readonly ConcurrentQueue<Response> responses = new ConcurrentQueue<Response>();
        private readonly RaycastHit[] hits = new RaycastHit[32];
        private volatile string snapshot;
        private volatile string transportStatus = "연결 대기";
        private volatile bool connected;
        private float nextObservation;
        private int tick, lastCommandTick = -1;
        private double commandExpires;
        private Command latest;
        private Bounds localBody;
        private WheelCollider[] wheels;
        private int pendingMissions, transportGeneration, consumedGeneration;
        private bool waitingForMissionAck;
        private double missionErrorUntil;
        public string VehicleId => vehicle?.OperatorVehicleId;
        public string Status { get; private set; } = "서버 제어 연결 대기";
        public string DiagnosticReason { get; private set; }
        public string MissionResult { get; private set; }
        public Vector3 GoalPosition { get; private set; }
        public string LaneProfileHash { get; private set; }
        public float LaneErrorM => latest?.lane_error_m ?? 0;
        public bool Connected => connected;
        public float LastTargetSpeedKmh => latest?.target_speed_kmh ?? 0;
        public string LatestObservationJson => snapshot;
        public bool IsDriving => !waitingForMissionAck && consumedGeneration == Volatile.Read(ref transportGeneration) &&
            connected && latest != null && !latest.emergency_stop && Time.realtimeSinceStartupAsDouble < commandExpires;

        public void Initialize(CampusServerMap sharedMap, IAutonomousVehicle target)
        {
            map = sharedMap;
            vehicle = target;
            wheels = target.VehicleTransform.GetComponentsInChildren<WheelCollider>();
            bool first = true;
            foreach (var box in target.VehicleTransform.GetComponentsInChildren<BoxCollider>())
                for (int x = -1; x <= 1; x += 2) for (int y = -1; y <= 1; y += 2) for (int z = -1; z <= 1; z += 2)
                {
                    var point = target.VehicleTransform.InverseTransformPoint(box.transform.TransformPoint(
                        box.center + Vector3.Scale(box.size * .5f, new Vector3(x, y, z))));
                    if (first) { localBody = new Bounds(point, Vector3.zero); first = false; }
                    else localBody.Encapsulate(point);
                }
            if (first || wheels.Length < 4 || target.WheelbaseM < .3f)
            { Status = "차량 geometry/접지 구성 확인 필요"; return; }
            cancellation = new CancellationTokenSource();
            string url = map.serverUrl.TrimEnd('/') + "/v1/demo/unity/vehicles/" + Uri.EscapeDataString(VehicleId) + "/control";
            var uri = new UriBuilder(url) { Scheme = url.StartsWith("https:") ? "wss" : "ws" }.Uri;
            _ = RunTransport(uri, cancellation.Token);
        }

        private async Task RunTransport(Uri uri, CancellationToken token)
        {
            while (!token.IsCancellationRequested)
            {
                try
                {
                    using (var socket = new ClientWebSocket())
                    {
                        await socket.ConnectAsync(uri, token).ConfigureAwait(false);
                        int generation = Interlocked.Increment(ref transportGeneration);
                        connected = true;
                        transportStatus = "WebSocket 연결";
                        var buffer = new byte[8192];
                        string sent = null;
                        while (!token.IsCancellationRequested && socket.State == WebSocketState.Open)
                        {
                            string data = snapshot;
                            if (data == null || data == sent) { await Task.Delay(10, token).ConfigureAwait(false); continue; }
                            sent = data;
                            var bytes = Encoding.UTF8.GetBytes(data);
                            await socket.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, token).ConfigureAwait(false);
                            int count = 0;
                            WebSocketReceiveResult result;
                            do
                            {
                                result = await socket.ReceiveAsync(new ArraySegment<byte>(buffer, count, buffer.Length - count), token).ConfigureAwait(false);
                                if (result.MessageType == WebSocketMessageType.Close) throw new InvalidOperationException("Server closed control session");
                                count += result.Count;
                                if (!result.EndOfMessage && count == buffer.Length) throw new InvalidOperationException("Oversized control response");
                            } while (!result.EndOfMessage);
                            responses.Enqueue(new Response { generation=generation, json=Encoding.UTF8.GetString(buffer, 0, count) });
                        }
                    }
                }
                catch (OperationCanceledException) { break; }
                catch (Exception error) { transportStatus = "제어 재연결 · " + error.GetType().Name; }
                finally { connected = false; }
                try { await Task.Delay(1000, token).ConfigureAwait(false); } catch (OperationCanceledException) { break; }
            }
        }

        private void Update()
        {
            if (vehicle == null || vehicle is Component component && component == null || vehicle.VehicleTransform == null || cancellation == null) return;
            if (!string.IsNullOrEmpty(map.RunId) && string.IsNullOrEmpty(map.LastError) && Time.unscaledTime >= nextObservation)
            {
                nextObservation = Time.unscaledTime + .1f;
                snapshot = JsonUtility.ToJson(Capture());
            }
            int currentGeneration=Volatile.Read(ref transportGeneration);
            if(consumedGeneration!=currentGeneration) { consumedGeneration=currentGeneration; latest=null; commandExpires=0; }
            while (responses.TryDequeue(out var response))
            {
                if(response.generation!=currentGeneration) continue;
                try
                {
                    var command = JsonUtility.FromJson<Command>(response.json);
                    if (command.schema_version != 2 || command.run_id != map.RunId || command.tick <= lastCommandTick ||
                        command.tick < tick - 4 || command.tick > tick ||
                        !float.IsFinite(command.target_speed_kmh) || command.target_speed_kmh < 0 || command.target_speed_kmh > 18 ||
                        !float.IsFinite(command.steering_angle_rad) || Math.Abs(command.steering_angle_rad) > vehicle.MaximumSteeringRad + .001f ||
                        !float.IsFinite(command.valid_for_s) || command.valid_for_s <= 0 || command.valid_for_s > .3f)
                        throw new ArgumentException("Invalid/stale command");
                    latest = command;
                    lastCommandTick = command.tick;
                    commandExpires = Time.realtimeSinceStartupAsDouble + command.valid_for_s;
                    DiagnosticReason = command.reason;
                    switch (command.status)
                    {
                        case "DRIVING": Status = "차로 자동 운행 중"; break;
                        case "ARRIVED": Status = "목적 거점 도착"; break;
                        case "YIELDING": Status = "주변·센서 확인 · 대기"; break;
                        case "OFF_ROUTE": Status = "경로 이탈 · 정지"; break;
                        default: Status = "자동 운행 대기"; break;
                    }
                }
                catch (Exception) { commandExpires = 0; Status = "유효하지 않은 제어 명령 · 제동"; }
            }
            if (waitingForMissionAck || !connected || latest == null || latest.run_id != map.RunId || Time.realtimeSinceStartupAsDouble >= commandExpires)
            {
                vehicle.SetDriveCommand(0, 0, true);
                Status = transportStatus + " · 제동 대기";
                if(waitingForMissionAck) Status="자동 경로 접수 대기";
            }
            else vehicle.SetDriveCommand(latest.target_speed_kmh, latest.steering_angle_rad, latest.emergency_stop);
            if (Time.realtimeSinceStartupAsDouble < missionErrorUntil) Status = "경로 접수 실패 · 위치/차로 확인";
        }

        private Observation Capture()
        {
            var root = vehicle.VehicleTransform;
            var forward = root.forward;
            var observation = new Observation
            {
                tick = ++tick, run_id = map.RunId, geometry_hash = map.geometryHash,
                x = root.position.x, z = root.position.z, heading_rad = Mathf.Atan2(forward.x, forward.z),
                speed_mps = Vector3.Dot(vehicle.VehicleBody.linearVelocity, forward), wheelbase_m = vehicle.WheelbaseM,
                max_steering_rad = vehicle.MaximumSteeringRad, sensor_valid = true, rays = new Ray[7]
            };
            observation.half_width_m = root.TransformVector(Vector3.right * localBody.extents.x).magnitude;
            foreach (var wheel in wheels) if (!wheel.GetGroundHit(out _)) observation.sensor_valid = false;
            for (int i = 0; i < 7; i++)
            {
                float angle = (i - 3) * (20f / 3);
                var origin = root.TransformPoint(new Vector3(Mathf.Lerp(localBody.min.x, localBody.max.x, i / 6f),
                    localBody.center.y + localBody.extents.y * .5f, localBody.max.z + .05f));
                var direction = Quaternion.AngleAxis(angle, Vector3.up) * forward;
                float distance = 20;
                int count = Physics.RaycastNonAlloc(origin, direction, hits, distance, ~0, QueryTriggerInteraction.Ignore);
                if (count == hits.Length) observation.sensor_valid = false;
                for (int j = 0; j < count; j++)
                    if (hits[j].rigidbody != vehicle.VehicleBody && !hits[j].transform.IsChildOf(root)) distance = Mathf.Min(distance, hits[j].distance);
                // Anonymous sensor hit in the ego frame, never another object's Transform.
                var relative = origin + direction * distance - root.position;
                observation.rays[i] = new Ray { range_m = distance, bearing_rad = angle * Mathf.Deg2Rad,
                    lateral_m = Vector3.Dot(relative, root.right), forward_m = Vector3.Dot(relative, forward),
                    origin_forward_m = Vector3.Dot(origin - root.position, forward) };
            }
            return observation;
        }

        public void StartMission(string stopId)
        {
            if (pendingMissions>0 || !connected || string.IsNullOrEmpty(stopId)) { Status = "연결 및 목적 거점을 확인하세요"; return; }
            if (vehicle.VehicleBody.linearVelocity.magnitude > .2f) { Status = "도착 후 새 목적 거점을 선택하세요"; return; }
            waitingForMissionAck=true;
            StartCoroutine(Post("mission", JsonUtility.ToJson(new Mission { run_id = map.RunId, command_id = Guid.NewGuid().ToString("N"), stop_id = stopId })));
        }
        private IEnumerator Post(string action, string json)
        {
            pendingMissions++;
            using (var request = new UnityWebRequest(map.serverUrl.TrimEnd('/') + "/v1/demo/unity/vehicles/" + Uri.EscapeDataString(VehicleId) + "/" + action, "POST"))
            {
                request.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(json));
                request.downloadHandler = new DownloadHandlerBuffer(); request.timeout = 3;
                request.SetRequestHeader("Content-Type", "application/json");
                yield return request.SendWebRequest();
                MissionResult = request.result == UnityWebRequest.Result.Success ? "자동 경로 명령 접수" : request.downloadHandler.text;
                if(request.result != UnityWebRequest.Result.Success)
                { missionErrorUntil=Time.realtimeSinceStartupAsDouble+5; /* Keep braking until a mission is accepted. */ }
                else if(action=="mission")
                {
                    var receipt=JsonUtility.FromJson<MissionReceipt>(request.downloadHandler.text);
                    GoalPosition=receipt.goal_unity; LaneProfileHash=receipt.lane_profile_hash;
                    waitingForMissionAck=false;
                }
            }
            pendingMissions--;
        }
        private void OnDisable()
        {
            cancellation?.Cancel(); cancellation?.Dispose(); cancellation = null;
            vehicle?.SetDriveCommand(0, 0, true);
        }
    }
}
