using System;
using System.Collections;
using UnityEngine;
using UnityEngine.Networking;

namespace InhaExpress.Client.MapClient
{
    /// <summary>Read-only link to the same demo session used by the web client. No vehicle commands.</summary>
    public sealed class CampusServerMap : MonoBehaviour
    {
        public TextAsset authoredMap;
        public string serverUrl = "http://127.0.0.1:8765";
        public bool connectOnPlay = true;
        public bool showStatus = true;
        public bool showEdges;
        [Min(0.5f)] public float pollSeconds = 1f;
        public string geometryHash;
        public string mapVersion;
        public ServerMapMarker[] markers = Array.Empty<ServerMapMarker>();
        public string ConnectionStatus { get; private set; } = "Offline map / vehicle control deferred";
        public string LastError { get; private set; }
        public string RunId { get; private set; }
        public double SimulationTime { get; private set; }
        public bool ServerPaused { get; private set; }
        public int VehicleCount { get; private set; }
        public int Completed { get; private set; }
        public string ServerProjectVersion { get; private set; }
        private UnityWebRequest activeRequest;
        private Coroutine connection;
        private ServerMapDocument localMap;

        [Serializable] private sealed class State
        {
            public string run_id, map_version;
            public double time_s;
            public bool paused, simulation_only;
            public int completed;
            public Vehicle[] vehicles;
        }
        [Serializable] private sealed class Vehicle { public string id; }

        private void OnEnable()
        {
            if (!Application.isPlaying) return;
            try
            {
                if (!authoredMap) throw new ArgumentException("No authored server map asset.");
                localMap = ServerMapDocument.Parse(authoredMap.text);
                if (!Uri.TryCreate(serverUrl, UriKind.Absolute, out var url) ||
                    (url.Scheme != "http" && url.Scheme != "https"))
                    throw new ArgumentException("Set a valid HTTP server URL in the Inspector.");
                if (connectOnPlay) connection = StartCoroutine(Poll());
            }
            catch (Exception error) { LastError = error.Message; ConnectionStatus = "Map connection unavailable"; }
        }

        private void OnDisable()
        {
            if (connection != null) StopCoroutine(connection);
            connection = null;
            if (activeRequest != null) { activeRequest.Abort(); activeRequest.Dispose(); activeRequest = null; }
            ConnectionStatus = "Disconnected / authored nodes retained";
        }

        public void ValidateRemote(string json)
        {
            var remote = ServerMapDocument.Parse(json);
            var local = ServerMapDocument.Parse(authoredMap.text);
            if (remote.geometry_hash != geometryHash || remote.map_version != mapVersion ||
                local.geometry_hash != geometryHash || remote.nodes.Length != local.nodes.Length ||
                remote.hubs.Length != local.hubs.Length || remote.stops.Length != local.stops.Length ||
                remote.edges.Length != local.edges.Length)
                throw new ArgumentException("Server map differs from the authored scene; export and import again.");
            // Do not rely on a reported hash alone when accepting server coordinates.
            var byId = new System.Collections.Generic.Dictionary<string, Vector3>();
            foreach (var node in local.nodes) byId.Add(node.id, node.position);
            foreach (var node in remote.nodes)
                if (!byId.TryGetValue(node.id, out var point) || Vector3.Distance(point, node.position) > .001f)
                    throw new ArgumentException("Server node differs from authored coordinates.");
            var anchors = new System.Collections.Generic.Dictionary<string, string>();
            foreach (var hub in local.hubs) anchors.Add("hub:" + hub.id, hub.node_id);
            foreach (var stop in local.stops) anchors.Add("stop:" + stop.id, stop.node_id);
            foreach (var hub in remote.hubs)
                if (!anchors.TryGetValue("hub:" + hub.id, out var node) || node != hub.node_id)
                    throw new ArgumentException("Server hub binding differs from authored map.");
            foreach (var stop in remote.stops)
                if (!anchors.TryGetValue("stop:" + stop.id, out var node) || node != stop.node_id)
                    throw new ArgumentException("Server Stop binding differs from authored map.");
            ServerProjectVersion = remote.project_version;
        }

        private IEnumerator Poll()
        {
            bool verified = false;
            while (true)
            {
                var endpoint = verified ? "/v1/demo/state" : "/v1/demo/unity-map";
                activeRequest = UnityWebRequest.Get(serverUrl.TrimEnd('/') + endpoint);
                activeRequest.timeout = 5;
                yield return activeRequest.SendWebRequest();
                try
                {
                    if (activeRequest.result != UnityWebRequest.Result.Success)
                        throw new InvalidOperationException(activeRequest.error);
                    if (!verified)
                    {
                        ValidateRemote(activeRequest.downloadHandler.text);
                        verified = true;
                        ConnectionStatus = "Map verified / awaiting shared web state";
                    }
                    else
                    {
                        var state = JsonUtility.FromJson<State>(activeRequest.downloadHandler.text);
                        if (state == null || state.map_version != mapVersion || !state.simulation_only ||
                            string.IsNullOrEmpty(state.run_id) || state.vehicles == null ||
                            double.IsNaN(state.time_s) || double.IsInfinity(state.time_s) || state.time_s < 0)
                            throw new ArgumentException("Invalid shared demo state.");
                        RunId = state.run_id;
                        SimulationTime = state.time_s;
                        ServerPaused = state.paused;
                        VehicleCount = state.vehicles.Length;
                        Completed = state.completed;
                        ConnectionStatus = "Connected to shared web demo / read only";
                    }
                    LastError = null;
                }
                catch (Exception error)
                {
                    verified = false;
                    ConnectionStatus = "Offline / authored nodes retained";
                    LastError = error.Message;
                }
                finally { activeRequest.Dispose(); activeRequest = null; }
                yield return new WaitForSecondsRealtime(verified ? Mathf.Max(0.5f, pollSeconds) : 5f);
            }
        }

        private void OnGUI()
        {
            if (!showStatus) return;
            GUILayout.BeginArea(new Rect(12, 12, 540, 180), GUI.skin.box);
            GUILayout.Label("Inha Mobil | " + Application.version + " | Map connection");
            GUILayout.Label(ConnectionStatus);
            if (localMap != null)
                GUILayout.Label(mapVersion + $" | {localMap.nodes.Length} nodes / {localMap.hubs.Length} hubs / {localMap.stops.Length} Stop candidates");
            if (!string.IsNullOrEmpty(ServerProjectVersion)) GUILayout.Label("Server version " + ServerProjectVersion);
            if (!string.IsNullOrEmpty(RunId))
                GUILayout.Label($"Run {RunId} | {SimulationTime:F1}s | paused={ServerPaused} | vehicles={VehicleCount} | completed={Completed}");
            if (!string.IsNullOrEmpty(LastError)) GUILayout.Label(LastError);
            GUILayout.Label("Node placement only. Vehicle actuation deferred. Access unverified.");
            GUILayout.Label("© OpenStreetMap contributors · ODbL 1.0");
            GUILayout.EndArea();
        }

        private void OnDrawGizmos()
        {
            if (!showEdges || authoredMap == null || markers == null) return;
            // Debug topology only: no colliders, road widening, or vehicle navigation.
            var positions = new System.Collections.Generic.Dictionary<string, Vector3>();
            foreach (var marker in markers)
                if (marker != null && marker.kind == ServerMapMarker.MarkerKind.RoadNode)
                    positions[marker.serverId] = marker.transform.position;
            var map = ServerMapDocument.Parse(authoredMap.text);
            Gizmos.color = new Color(0.1f, 0.7f, 1, 0.45f);
            foreach (var edge in map.edges)
                if (positions.TryGetValue(edge.u, out var a) && positions.TryGetValue(edge.v, out var b))
                    Gizmos.DrawLine(a, b);
        }
    }
}
