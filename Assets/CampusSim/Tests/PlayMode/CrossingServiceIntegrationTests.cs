using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Reflection;
using InhaExpress.Client.Domain;
using InhaExpress.Client.Networking;
using InhaExpress.Client.Presentation;
using InhaExpress.Simulation;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace InhaExpress.Client.Tests
{
    public sealed class CrossingServiceIntegrationTests
    {
        private readonly List<GameObject> objects = new List<GameObject>();
        private GameObject ego, pedestrian;
        private bool overlap;
        private float minimumClearance = float.PositiveInfinity;
        [Serializable]
        private sealed class RaySummary
        {
            public int rays = 0;
            public int hits = 0;
            public int misses = 0;
        }
        [Serializable]
        private sealed class ReservationSummary
        {
            public bool enabled = false;
            public int completed = 0;
            public int claims = 0;
            public int closed = 0;
            public int faults = 0;
        }

        [UnityTearDown]
        public IEnumerator Cleanup()
        {
            foreach (var item in objects) if (item != null) UnityEngine.Object.Destroy(item);
            objects.Clear();
            yield return null;
        }

        [UnityTest]
        public IEnumerator SensorInsideExternalColliderCannotClaimMisses()
        {
            var rig = New("Inside collider rig").AddComponent<VehicleRaycastSensorRig>();
            New("Containing obstacle").AddComponent<BoxCollider>();
            Physics.SyncTransforms();
            const BindingFlags flags = BindingFlags.NonPublic | BindingFlags.Instance;
            Assert.IsFalse((bool)typeof(VehicleRaycastSensorRig).GetMethod("Scan", flags).Invoke(rig, new object[] { 0.1 }));
            var samples = (List<SensorRayDto>)typeof(VehicleRaycastSensorRig).GetField("rays", flags).GetValue(rig);
            Assert.IsEmpty(samples);
            yield return null;
        }

        [UnityTest]
        public IEnumerator RayScanPreservesOcclusionAndMisses()
        {
            var rig = New("Ray sample rig").AddComponent<VehicleRaycastSensorRig>();
            foreach (float z in new[] { 3f, 5f })
            {
                var obstacle = New("Ray occluder");
                obstacle.transform.position = new Vector3(0, 0, z);
                obstacle.AddComponent<BoxCollider>();
            }
            Physics.SyncTransforms();
            const BindingFlags flags = BindingFlags.NonPublic | BindingFlags.Instance;
            Assert.IsTrue((bool)typeof(VehicleRaycastSensorRig).GetMethod("Scan", flags).Invoke(rig, new object[] { 0.1 }));
            var samples = (List<SensorRayDto>)typeof(VehicleRaycastSensorRig).GetField("rays", flags).GetValue(rig);
            Assert.AreEqual(64, samples.Count);
            Assert.AreEqual("HIT", samples[32].Outcome);
            Assert.That(samples[32].RangeM.Value, Is.EqualTo(2.5).Within(0.001));
            Assert.AreEqual("MISS", samples[16].Outcome);
            Assert.AreEqual(30, samples[16].RangeM.Value);
            yield return null;
        }

        [UnityTest]
        public IEnumerator SaturatedRayCannotReportMissOrValidFrame()
        {
            var rig = New("Saturated ray rig").AddComponent<VehicleRaycastSensorRig>();
            for (int i = 0; i < 70; i++)
            {
                var obstacle = New("Buffer saturation obstacle");
                obstacle.transform.position = new Vector3(0, 0, 2 + i * 0.2f);
                obstacle.AddComponent<BoxCollider>().size = Vector3.one * 0.1f;
            }
            Physics.SyncTransforms();
            const BindingFlags flags = BindingFlags.NonPublic | BindingFlags.Instance;
            Assert.IsFalse((bool)typeof(VehicleRaycastSensorRig).GetMethod("Scan", flags).Invoke(rig, new object[] { 0.1 }));
            var samples = (List<SensorRayDto>)typeof(VehicleRaycastSensorRig).GetField("rays", flags).GetValue(rig);
            Assert.AreEqual(64, samples.Count);
            Assert.AreEqual("INVALID", samples[32].Outcome);
            Assert.IsFalse(samples[32].RangeM.HasValue);
            yield return null;
        }

        [UnityTest]
        public IEnumerator LateralLidarCrossingStopsOutsideForwardSectorAndCompletesPassenger()
        {
            if (Environment.GetEnvironmentVariable("INHA_UNITY_E2E_CROSSING") != "1")
                Assert.Ignore("Dedicated -Crossing integration scenario required.");
            string url = Environment.GetEnvironmentVariable("INHA_UNITY_E2E_URL");
            string version = Environment.GetEnvironmentVariable("INHA_UNITY_E2E_VERSION");
            string trace = Environment.GetEnvironmentVariable("INHA_UNITY_E2E_TRACE");
            var source = new WebSocketClientDataSource(url, ClientRole.PC_Operator, version);
            var host = New("Crossing PC host").AddComponent<ClientRuntimeHost>();
            host.Initialize(ClientRole.PC_Operator, source);
            var mobile = new WebSocketClientDataSource(url, ClientRole.Mobile_Passenger, version, "crossing-passenger");
            var mobileHost = New("Crossing mobile host").AddComponent<ClientRuntimeHost>();
            mobileHost.Initialize(ClientRole.Mobile_Passenger, mobile, "crossing-passenger");
            int accepted = 0, rejected = 0;
            source.SensorObservationAcknowledged += ack => { if (ack.Accepted) accepted++; else rejected++; };
            ServiceCommandAckDto ack = null;
            mobile.CommandAcknowledged += value => ack = value;
            VehicleDto latest = null;
            bool completed = false;
            host.Store.SnapshotChanged += snapshot =>
            {
                foreach (var vehicle in snapshot.Vehicles) if (vehicle.Id == "V01") latest = vehicle;
                foreach (var request in snapshot.Requests)
                    if (request.Status == RequestStatus.COMPLETED) completed = true;
                if (latest != null && pedestrian != null && !string.IsNullOrEmpty(trace))
                    File.AppendAllText(trace, FormattableString.Invariant(
                        $"{snapshot.SimulationTick},{latest.Position.X:F3},{pedestrian.transform.position.z:F3},{latest.SpeedMps:F3},{latest.MotionState},{latest.Reason},CROSSING\n"));
            };
            var template = New("Crossing 0.4m shell");
            template.transform.position = new Vector3(1000, 0, 1000);
            template.AddComponent<Rigidbody>().isKinematic = true;
            template.GetComponent<Rigidbody>().useGravity = false;
            template.AddComponent<BoxCollider>().size = Vector3.one * 0.4f;
            template.AddComponent<VehicleEgoLocalizationReporter>();
            var spawner = New("Crossing spawner").AddComponent<VehicleActorSpawner>();
            const BindingFlags flags = BindingFlags.NonPublic | BindingFlags.Instance;
            var type = typeof(VehicleActorSpawner);
            bool pythonControl = Environment.GetEnvironmentVariable("INHA_UNITY_E2E_PYTHON_CONTROL") == "1";
            type.GetField("usePythonControl", flags).SetValue(spawner, pythonControl);
            type.GetField("requiredMapVersion", flags).SetValue(spawner, "synthetic-physics-integration-v1");
            var field = type.GetField("vehiclePrefabs", flags);
            var bindingType = field.FieldType.GetElementType();
            var binding = Activator.CreateInstance(bindingType, true);
            bindingType.GetField("vehicleId").SetValue(binding, "V01");
            bindingType.GetField("prefab").SetValue(binding, template);
            var bindings = Array.CreateInstance(bindingType, 1);
            bindings.SetValue(binding, 0);
            field.SetValue(spawner, bindings);
            spawner.Bind(host);
            pedestrian = New("Visible crossing pedestrian");
            pedestrian.transform.position = new Vector3(2.5f, 0, 5f);
            var walker = pedestrian.AddComponent<SyntheticPedestrianWalker>();
            walker.ConfigureSyntheticPath(new[] { new Vector3(2.5f, 0, -4f) }, 2f);
            walker.SetPaused(true);
            Physics.SyncTransforms();
            host.StartSource();
            mobileHost.StartSource();
            yield return Wait(() => accepted >= 3 && mobileHost.Store.Current != null, 20, "No connected telemetry");
            using (var http = new HttpClient())
            {
                var rayCheck = http.GetStringAsync(url.Replace("ws://", "http://").Replace("/v1/client/ws", "/test/ray-summary"));
                yield return Wait(() => rayCheck.IsCompleted, 5, "Ray ingress check timeout");
                var counts = JsonUtility.FromJson<RaySummary>(rayCheck.Result);
                Assert.AreEqual(64, counts.rays);
                Assert.Greater(counts.hits, 0);
                Assert.Greater(counts.misses, 0);
                Debug.Log("Python retained measured ray summary: " + rayCheck.Result);
            }
            ego = GameObject.Find("Vehicle V01");
            Assert.That(ego, Is.Not.Null);
            mobile.SendPassengerRequest(new PassengerRequestCommandDto(
                "fixture_landmark_2", "fixture_landmark_3", new ServiceNeedsDto(false, 0, false)));
            yield return Wait(() => ack != null, 5, "Missing request ACK");
            Assert.That(ack.Accepted, Is.True);
            if (pythonControl && Environment.GetEnvironmentVariable("INHA_UNITY_E2E_DISCONNECT") != "1")
            {
                var controlled = ego.GetComponent<VehicleCommandActuator>();
                yield return Wait(() => ego.transform.position.x > 0.1f && controlled.SpeedMps < 0.01f,
                    20, "Vehicle did not wait for the reserved exit space");
                Assert.That(ego.transform.position.x + 0.2f, Is.LessThan(1f), "Body entered the ungranted corridor");
                var heldPosition = ego.transform.position;
                yield return new WaitForSeconds(0.25f);
                Assert.That(Vector3.Distance(ego.transform.position, heldPosition), Is.LessThan(0.01f));
                using (var client = new HttpClient())
                {
                    var endpoint = new UriBuilder(url) { Scheme = "http", Path = "/test/release-exit", Query = "" };
                    var release = client.PostAsync(endpoint.Uri, null);
                    yield return Wait(() => release.IsCompleted, 5, "Exit release timed out");
                    using (var response = release.GetAwaiter().GetResult()) response.EnsureSuccessStatusCode();
                }
                Debug.Log($"Resource admission held the body outside the corridor at x={heldPosition.x:F3}");
            }
            yield return Wait(() => latest?.SpeedMps > 0.4 && latest.Position.X > 0.5, 20, "Vehicle never moved");
            if (Environment.GetEnvironmentVariable("INHA_UNITY_E2E_DISCONNECT") == "1")
            {
                var controlled = ego.GetComponent<VehicleCommandActuator>();
                var beforePosition = ego.transform.position;
                long beforeSequence = controlled.LastSequence;
                string session = source.TelemetrySessionId;
                string run = host.Store.Current.RunId;
                var endpoint = new UriBuilder(url) { Scheme = "http", Path = "/test/disconnect", Query = "" };
                using (var client = new HttpClient())
                {
                    var responseTask = client.PostAsync(endpoint.Uri, null);
                    yield return Wait(() => responseTask.IsCompleted, 5, "Disconnect endpoint timed out");
                    using (var response = responseTask.GetAwaiter().GetResult())
                    {
                        response.EnsureSuccessStatusCode();
                        var content = response.Content.ReadAsStringAsync();
                        yield return Wait(() => content.IsCompleted, 5, "No disconnect result");
                        StringAssert.Contains("\"closed_pc\":1", content.GetAwaiter().GetResult());
                    }
                }
                yield return Wait(() => !controlled.CommandFresh && controlled.SpeedMps < 0.01f,
                    3, "Vehicle did not brake during socket loss");
                Assert.That(host.ConnectionState, Is.Not.EqualTo(ConnectionState.Connected));
                Assert.That(Vector3.Distance(ego.transform.position, beforePosition), Is.LessThan(0.65f));
                var stoppedPosition = ego.transform.position;
                yield return new WaitForSeconds(0.2f);
                Assert.That(Vector3.Distance(ego.transform.position, stoppedPosition), Is.LessThan(0.01f));
                yield return Wait(() => host.ConnectionState == ConnectionState.Connected &&
                    controlled.LastSequence > beforeSequence, 15, "Command connection did not recover");
                Assert.That(source.TelemetrySessionId, Is.EqualTo(session));
                Assert.That(host.Store.Current.RunId, Is.EqualTo(run));
                Assert.That(GameObject.Find("Vehicle V01"), Is.SameAs(ego));
                yield return Wait(() => latest?.Reason == ReasonCode.SAFETY_RESUME_HOLD,
                    5, "Reconnect skipped fresh-sensor recovery hold");
                yield return Wait(() => controlled.SpeedMps > 0.4f, 10, "No motion after recovery hold");
                Debug.Log($"Socket interruption: bounded stop and recovery, command sequence {beforeSequence} -> {controlled.LastSequence}");
            }
            walker.SetPaused(false);
            yield return Wait(() => latest?.Reason == ReasonCode.OBSTACLE_STOP, 5, "No crossing stop");
            float forward = pedestrian.transform.position.x - ego.transform.position.x;
            float lateral = Mathf.Abs(pedestrian.transform.position.z - ego.transform.position.z);
            Assert.That(lateral, Is.GreaterThan(Mathf.Abs(forward) + 0.5f), "Stop must precede entry into forward range sector");
            var follower = ego.GetComponent<VehicleRouteFollower>();
            var actuator = ego.GetComponent<VehicleCommandActuator>();
            if (pythonControl)
            {
                Assert.That(actuator, Is.Not.Null);
                Assert.That(follower.enabled, Is.False, "Only the command actuator may drive.");
                Assert.That(actuator.LastSequence, Is.GreaterThan(0));
            }
            Func<float> speed = () => pythonControl ? actuator.SpeedMps : follower.SpeedMps;
            Func<bool> authorized = () => pythonControl ? actuator.CommandFresh : follower.MotionAuthorized;
            yield return Wait(() => speed() < 0.01f, 3, "Vehicle did not brake");
            Assert.That(authorized(), Is.False);
            yield return Wait(() => latest?.Reason == ReasonCode.SAFETY_RESUME_HOLD, 10, "No clear hold after crossing");
            Assert.That(authorized(), Is.False);
            yield return Wait(() => completed, 60, "Passenger not completed after crossing");
            Assert.That(walker.Completed, Is.True);
            Assert.That(pedestrian.activeSelf, Is.True);
            Assert.That(overlap, Is.False, "Physics collider overlap observed");
            Assert.That(minimumClearance, Is.GreaterThan(0));
            Assert.That(rejected, Is.Zero);
            if (pythonControl && Environment.GetEnvironmentVariable("INHA_UNITY_E2E_DISCONNECT") != "1")
            {
                using (var http = new HttpClient())
                {
                    var query = http.GetStringAsync(url.Replace("ws://", "http://").Replace("/v1/client/ws", "/test/reservation-summary"));
                    yield return Wait(() => query.IsCompleted, 5, "Reservation observation check timeout");
                    var summary = JsonUtility.FromJson<ReservationSummary>(query.Result);
                    Assert.IsTrue(summary.enabled);
                    Assert.AreEqual(1, summary.completed, "Full-body entry and confirmed exit required");
                    Assert.AreEqual(0, summary.claims);
                    Assert.AreEqual(0, summary.closed);
                    Assert.AreEqual(0, summary.faults);
                    Debug.Log("Ego-derived resource occupancy: " + query.Result);
                }
            }
            if (pythonControl)
            {
                long lastControlSequence = actuator.LastSequence;
                UnityEngine.Object.Destroy(ego);
                yield return null;
                yield return Wait(() => GameObject.Find("Vehicle V01") != null, 5, "No replacement actor");
                ego = GameObject.Find("Vehicle V01");
                var replacement = ego.GetComponent<VehicleCommandActuator>();
                yield return Wait(() => replacement != null && replacement.LastSequence > lastControlSequence,
                    5, "Replacement did not continue the command sequence");
                Assert.That(ego.GetComponent<VehicleRouteFollower>().enabled, Is.False);
                Assert.That(rejected, Is.Zero);
                Debug.Log($"Recreated command actor: sequence {lastControlSequence} -> {replacement.LastSequence}");
            }
            Debug.Log($"Crossing completed: pythonControl={pythonControl}, sensorACK={accepted}, rejected={rejected}, sampledMinClearance={minimumClearance:F3}m, stopForward={forward:F3}, stopLateral={lateral:F3}");
        }

        private GameObject New(string name)
        {
            var result = new GameObject(name);
            objects.Add(result);
            return result;
        }

        private IEnumerator Wait(Func<bool> predicate, float timeout, string message)
        {
            float deadline = Time.realtimeSinceStartup + timeout;
            while (!predicate() && Time.realtimeSinceStartup < deadline)
            {
                // Test-only Ground Truth oracle; none of these positions go to Python.
                if (ego != null && pedestrian != null)
                {
                    var shell = ego.GetComponent<BoxCollider>();
                    var person = pedestrian.GetComponent<CapsuleCollider>();
                    overlap |= Physics.ComputePenetration(shell, ego.transform.position, ego.transform.rotation,
                        person, pedestrian.transform.position, pedestrian.transform.rotation, out _, out _);
                    float clearance = Vector3.Distance(shell.ClosestPoint(pedestrian.transform.position),
                        pedestrian.transform.position) - person.radius;
                    minimumClearance = Mathf.Min(minimumClearance, clearance);
                }
                yield return new WaitForFixedUpdate();
            }
            Assert.That(predicate(), Is.True, message);
        }
    }
}
