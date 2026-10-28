#if UNITY_EDITOR_WIN
using System;
using System.Collections;
using System.Diagnostics;
using System.IO;
using System.Linq;
using InhaExpress.Client.MapClient;
using InhaExpress.Client.Presentation;
using NUnit.Framework;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace InhaExpress.Client.Tests
{
    public sealed class DesktopLiveIntegrationTests
    {
        [Serializable] private sealed class LiveState { public string run_id; public double time_s; public bool paused; }
        [Serializable] private sealed class SensorEnvelope
        {
            public int schema_version;
            public bool sensor_valid, obstacle_detection_enabled;
            public PhysicalLidarFrame lidar;
        }
        [Serializable] private sealed class LiveInfo { public int process_id; public string run_id; }
        [Serializable] private sealed class Evidence
        {
            public string project_version, run_id, http_run_id, geometry_hash;
            public double unity_time_s, web_time_s;
            public int process_id, nodes, hubs, stops, lidar_clients;
            public bool auto_started, reused_without_ownership, duplicate_bootstrap_rejected, vehicle_actuation;
            public string[] loaded_scenes;
        }

        [UnityTest]
        public IEnumerator PcBootstrapAutoStartsRealPythonAndLoadsTheExistingAdditiveStack()
        {
            if (Environment.GetEnvironmentVariable("CAMPUS_SIM_LIVE_TEST") != "1")
                Assert.Ignore("Opt in with CAMPUS_SIM_LIVE_TEST=1 and install backend dependencies.");
            var paths = new[] { "PC_Bootstrap", "CampusWorld", "CampusTerrain", "PC_Operator" }
                .Select(n => "Assets/CampusSim/Scenes/" + n + ".unity").ToArray();
            CampusServerMap map = null;
            ClientBootstrap bootstrap = null;
            DesktopServerLauncher launcher = null;
            GameObject reuseObject = null;
            int serverPid = 0;
            try
            {
                yield return EditorSceneManager.LoadSceneAsyncInPlayMode(paths[0], new LoadSceneParameters(LoadSceneMode.Additive));
                double deadline = Time.realtimeSinceStartupAsDouble + 120;
                while (Time.realtimeSinceStartupAsDouble < deadline)
                {
                    bootstrap = UnityEngine.Object.FindFirstObjectByType<ClientBootstrap>();
                    launcher = bootstrap ? bootstrap.GetComponent<DesktopServerLauncher>() : null;
                    map = UnityEngine.Object.FindFirstObjectByType<CampusServerMap>();
                    if (launcher && launcher.Ready && map && !string.IsNullOrEmpty(map.RunId) &&
                        paths.All(p => SceneManager.GetSceneByPath(p).isLoaded)) break;
                    yield return null;
                }
                Assert.That(launcher, Is.Not.Null);
                Assert.That(launcher.Ready, Is.True, launcher.Status);
                Assert.That(map, Is.Not.Null);
                Assert.That(map.RunId, Is.Not.Null.And.Not.Empty, map.LastError);
                Assert.That(launcher.OwnsServer, Is.True, "This opt-in test requires a free local port 8765.");
                foreach (string path in paths) Assert.That(SceneManager.GetSceneByPath(path).isLoaded, Is.True, path);
                Assert.That(UnityEngine.Object.FindObjectsByType<CampusServerMap>(FindObjectsSortMode.None).Length, Is.EqualTo(1));
                var document = ServerMapDocument.Parse(map.authoredMap.text);
                Assert.That(map.markers.Length, Is.EqualTo(document.nodes.Length + document.hubs.Length + document.stops.Length));
                foreach (var marker in map.markers)
                {
                    var node = document.nodes.Single(n => n.id == marker.nodeId);
                    Assert.That(marker.transform.position.x, Is.EqualTo(node.position.x).Within(.01));
                    Assert.That(marker.transform.position.z, Is.EqualTo(node.position.z).Within(.01));
                }
                // Actual PC components must attach the same continuous sensor contract to all three cars.
                UnityAutonomousClient[] clients = null;
                deadline = Time.realtimeSinceStartupAsDouble + 30;
                while (Time.realtimeSinceStartupAsDouble < deadline)
                {
                    clients = UnityEngine.Object.FindObjectsByType<UnityAutonomousClient>(FindObjectsSortMode.None);
                    if (clients.Length == 3 && clients.All(c=>c.Connected && !string.IsNullOrEmpty(c.LatestObservationJson))) break;
                    yield return null;
                }
                Assert.That(clients.Length, Is.EqualTo(3));
                foreach (var client in clients)
                {
                    Assert.That(client.Connected, Is.True, client.Status);
                    var sensor = JsonUtility.FromJson<SensorEnvelope>(client.LatestObservationJson);
                    Assert.That(sensor.schema_version, Is.EqualTo(5));
                    Assert.That(sensor.obstacle_detection_enabled, Is.True);
                    Assert.That(sensor.lidar, Is.Not.Null);
                    Assert.That(sensor.lidar.valid, Is.True, client.VehicleId);
                    Assert.That(sensor.lidar.ray_count, Is.EqualTo(2520));
                    Assert.That(sensor.sensor_valid, Is.True, client.VehicleId + " wheel/sensor validity");
                }
                var firstTick = JsonUtility.FromJson<SensorEnvelope>(clients[0].LatestObservationJson).lidar.tick;
                yield return new WaitForSecondsRealtime(.5f);
                Assert.That(JsonUtility.FromJson<SensorEnvelope>(clients[0].LatestObservationJson).lidar.tick, Is.GreaterThan(firstTick));
                LiveState state;
                using (var get = UnityWebRequest.Get("http://127.0.0.1:8765/v1/demo/state"))
                {
                    yield return get.SendWebRequest();
                    Assert.That(get.result, Is.EqualTo(UnityWebRequest.Result.Success));
                    state = JsonUtility.FromJson<LiveState>(get.downloadHandler.text);
                }
                Assert.That(state.run_id, Is.EqualTo(map.RunId));
                Assert.That(state.run_id, Is.EqualTo(launcher.RunId));
                Assert.That(state.paused, Is.True);
                Assert.That(map.SimulationTime, Is.EqualTo(state.time_s));
                reuseObject = new GameObject("Existing server reuse check");
                var reuse = reuseObject.AddComponent<DesktopServerLauncher>();
                yield return reuse.EnsureRunning();
                Assert.That(reuse.Ready, Is.True, reuse.Status);
                Assert.That(reuse.OwnsServer, Is.False);
                Assert.That(reuse.RunId, Is.EqualTo(map.RunId));
                UnityEngine.Object.Destroy(reuseObject); reuseObject = null;
                var duplicate = new GameObject("Duplicate PC Bootstrap check");
                duplicate.AddComponent<ClientBootstrap>();
                yield return null;
                Assert.That(UnityEngine.Object.FindObjectsByType<ClientBootstrap>(FindObjectsSortMode.None).Length, Is.EqualTo(1));
                LiveInfo info;
                using (var get = UnityWebRequest.Get("http://127.0.0.1:8765/v1/desktop/info"))
                {
                    yield return get.SendWebRequest();
                    Assert.That(get.result, Is.EqualTo(UnityWebRequest.Result.Success));
                    info = JsonUtility.FromJson<LiveInfo>(get.downloadHandler.text);
                }
                Assert.That(info.process_id, Is.EqualTo(launcher.OwnedProcessId));
                serverPid = info.process_id;
                var evidence = new Evidence
                {
                    project_version = Application.version, run_id = map.RunId, http_run_id = state.run_id,
                    geometry_hash = map.geometryHash, unity_time_s = map.SimulationTime, web_time_s = state.time_s,
                    process_id = info.process_id, nodes = document.nodes.Length, hubs = document.hubs.Length,
                    stops = document.stops.Length, auto_started = true, reused_without_ownership = true,
                    duplicate_bootstrap_rejected = true, vehicle_actuation = false, lidar_clients = clients.Length,
                    loaded_scenes = paths.Select(p => SceneManager.GetSceneByPath(p).name).ToArray()
                };
                Directory.CreateDirectory(".utmp");
                File.WriteAllText(".utmp/desktop-live-ready.json", JsonUtility.ToJson(evidence, true));
                if (Environment.GetEnvironmentVariable("CAMPUS_SIM_BROWSER_HOLD") == "1")
                {
                    deadline = Time.realtimeSinceStartupAsDouble + 180;
                    while (!File.Exists(".utmp/desktop-browser-done") && Time.realtimeSinceStartupAsDouble < deadline)
                        yield return new WaitForSecondsRealtime(1);
                    Assert.That(File.Exists(".utmp/desktop-browser-done"), Is.True, "Browser verification did not finish in time.");
                }
            }
            finally
            {
                if (reuseObject) UnityEngine.Object.DestroyImmediate(reuseObject);
                if (bootstrap) UnityEngine.Object.DestroyImmediate(bootstrap.gameObject);
            }
            // Unload only the scenes this test loaded, in reverse Additive dependency order.
            foreach (string path in paths.Reverse())
                if (SceneManager.GetSceneByPath(path).isLoaded) yield return SceneManager.UnloadSceneAsync(path);
            double stoppedBy = Time.realtimeSinceStartupAsDouble + 15;
            bool stopped = false;
            while (Time.realtimeSinceStartupAsDouble < stoppedBy)
            {
                try { using (var process = Process.GetProcessById(serverPid)) stopped = process.HasExited; }
                catch (ArgumentException) { stopped = true; }
                if (stopped) break;
                yield return null;
            }
            Assert.That(stopped, Is.True, "PC-owned Python did not exit after its stdin pipe closed.");
        }
    }
}
#endif
