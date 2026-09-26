using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Reflection;
using InhaExpress.Client.Domain;
using InhaExpress.Client.Networking;
using InhaExpress.Client.Presentation;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace InhaExpress.Client.Tests
{
    public sealed class ReservationServiceIntegrationTests
    {
        private readonly List<GameObject> objects = new List<GameObject>();
        private readonly List<GameObject> fleet = new List<GameObject>();
        private bool overlap, dualOccupancy;
        private readonly List<string> entries = new List<string>();
        private readonly HashSet<string> inside = new HashSet<string>();
        [Serializable] private sealed class RequestAudit
        {
            public string id = null, vehicle = null, service = null, status = null;
        }
        [Serializable] private sealed class Summary
        {
            public int completed = 0, claims = 0, closed = 0, faults = 0, pending = 0;
            public RequestAudit[] requests = Array.Empty<RequestAudit>();
        }

        [UnityTearDown] public IEnumerator Cleanup()
        {
            foreach (var obj in objects) if (obj != null) UnityEngine.Object.Destroy(obj);
            objects.Clear();
            fleet.Clear(); inside.Clear(); entries.Clear(); overlap = dualOccupancy = false;
            yield return null;
        }

        [UnityTest] public IEnumerator OpposingVehiclesSerializeOrRetainFailedOccupant()
        {
            if (Environment.GetEnvironmentVariable("INHA_UNITY_E2E_RESERVATIONS") != "1")
                Assert.Ignore("Dedicated -Reservations run required.");
            bool fault = Environment.GetEnvironmentVariable("INHA_UNITY_E2E_RESERVATION_FAULT") == "1";
            bool three = Environment.GetEnvironmentVariable("INHA_UNITY_E2E_FLEET_THREE") == "1";
            int fleetSize = three ? 3 : 2;
            string url = Environment.GetEnvironmentVariable("INHA_UNITY_E2E_URL");
            string trace = Environment.GetEnvironmentVariable("INHA_UNITY_E2E_TRACE");
            var source = new WebSocketClientDataSource(url, ClientRole.PC_Operator,
                Environment.GetEnvironmentVariable("INHA_UNITY_E2E_VERSION"));
            var host = New("Reservation host").AddComponent<ClientRuntimeHost>();
            host.Initialize(ClientRole.PC_Operator, source);
            var mobile = new WebSocketClientDataSource(url, ClientRole.Mobile_Passenger,
                Environment.GetEnvironmentVariable("INHA_UNITY_E2E_VERSION"), "reservation-a");
            var mobileHost = New("Reservation passenger host").AddComponent<ClientRuntimeHost>();
            mobileHost.Initialize(ClientRole.Mobile_Passenger, mobile, "reservation-a");
            bool mobileWait = false, pcWait = false, mobileLeak = false, mobileWaitEtaKnown = false;
            mobileHost.Store.SnapshotChanged += snapshot =>
            {
                mobileLeak |= snapshot.ControlCommands.Count != 0 || snapshot.Vehicles.Count > 1;
                foreach (var request in snapshot.Requests) mobileLeak |= request.OwnerId != "reservation-a";
                foreach (var vehicle in snapshot.Vehicles)
                    if (vehicle.Reason == ReasonCode.RESOURCE_WAIT)
                    {
                        mobileWait = true;
                        foreach (var request in snapshot.Requests) mobileWaitEtaKnown |= request.EtaS.HasValue;
                    }
            };
            int accepted = 0, rejected = 0, expectedStale = 0, completed = 0;
            string failedVehicle = null;
            var sensorReady = new HashSet<string>();
            source.SensorObservationAcknowledged += ack =>
            {
                if (ack.Accepted) { accepted++; sensorReady.Add(ack.VehicleId); }
                else if (fault && ack.VehicleId == failedVehicle && ack.ErrorCode == "stale_localization") expectedStale++;
                else rejected++;
            };
            host.Store.SnapshotChanged += snapshot =>
            {
                foreach (var vehicle in snapshot.Vehicles)
                    pcWait |= vehicle.Reason == ReasonCode.RESOURCE_WAIT;
                completed = 0;
                foreach (var request in snapshot.Requests) if (request.Status == RequestStatus.COMPLETED) completed++;
                if (!string.IsNullOrEmpty(trace))
                    foreach (var vehicle in snapshot.Vehicles)
                        File.AppendAllText(trace, FormattableString.Invariant(
                            $"{snapshot.SimulationTick},{vehicle.Id},{vehicle.Position.X:F3},{vehicle.Position.Y:F3},{vehicle.SpeedMps:F3},{vehicle.MotionState},{vehicle.Reason}\n"));
            };
            var template = New("Reservation 0.4m shell");
            template.transform.position = new Vector3(1000, 0, 1000);
            var body = template.AddComponent<Rigidbody>();
            body.isKinematic = true; body.useGravity = false;
            template.AddComponent<BoxCollider>().size = Vector3.one * 0.4f;
            template.AddComponent<VehicleEgoLocalizationReporter>();
            var spawner = New("Reservation spawner").AddComponent<VehicleActorSpawner>();
            const BindingFlags flags = BindingFlags.NonPublic | BindingFlags.Instance;
            var type = typeof(VehicleActorSpawner);
            type.GetField("usePythonControl", flags).SetValue(spawner, true);
            type.GetField("requiredMapVersion", flags).SetValue(spawner, Environment.GetEnvironmentVariable("INHA_UNITY_E2E_MAP"));
            var field = type.GetField("vehiclePrefabs", flags);
            var bindingType = field.FieldType.GetElementType();
            var bindings = Array.CreateInstance(bindingType, fleetSize);
            for (int i = 0; i < fleetSize; i++)
            {
                var binding = Activator.CreateInstance(bindingType, true);
                bindingType.GetField("vehicleId").SetValue(binding, $"V{i + 1:00}");
                bindingType.GetField("prefab").SetValue(binding, template);
                bindings.SetValue(binding, i);
            }
            field.SetValue(spawner, bindings); spawner.Bind(host); host.StartSource(); mobileHost.StartSource();
            yield return Wait(() => accepted >= 3 * fleetSize && sensorReady.Count == fleetSize, 20, "Fleet telemetry absent");
            for (int i = 0; i < fleetSize; i++)
            {
                var actor = GameObject.Find($"Vehicle V{i + 1:00}");
                Assert.NotNull(actor); fleet.Add(actor);
            }
            using (var client = new HttpClient())
            {
                var start = client.PostAsync(Endpoint(url, "/test/reservation-start"), null);
                yield return Wait(() => start.IsCompleted, 5, "Request setup timed out");
                using (var response = start.GetAwaiter().GetResult())
                {
                    response.EnsureSuccessStatusCode();
                    var content = response.Content.ReadAsStringAsync();
                    yield return Wait(() => content.IsCompleted, 5, "Request setup response absent");
                    StringAssert.Contains("\"accepted\":true", content.Result);
                }
                yield return Wait(() => entries.Count > 0, 45, "No vehicle entered the corridor");
                if (fault)
                {
                    var occupant = GameObject.Find("Vehicle " + entries[0]);
                    yield return Wait(() =>
                    {
                        var bounds = occupant.GetComponent<BoxCollider>().bounds;
                        return bounds.min.x > -0.5f && bounds.max.x < 4.5f &&
                            bounds.min.z > -0.6f && bounds.max.z < 0.6f &&
                            occupant.GetComponent<VehicleCommandActuator>().SpeedMps > 0.1f;
                    }, 30, "Moving occupant never reached a fully internal fault pose");
                    failedVehicle = entries[0];
                    var initialPosition = occupant.transform.position;
                    float initialSpeed = occupant.GetComponent<VehicleCommandActuator>().SpeedMps;
                    occupant.GetComponent<VehicleEgoLocalizationReporter>().enabled = false;
                    yield return Wait(() => occupant.GetComponent<VehicleCommandActuator>().SpeedMps < 0.01f,
                        4, "Lost telemetry did not brake occupant");
                    Assert.Less(Vector3.Distance(initialPosition, occupant.transform.position),
                        initialSpeed * 0.3f + initialSpeed * initialSpeed / 4f + 0.1f,
                        "Command expiry plus braking distance exceeded synthetic bound");
                    var stoppedPosition = occupant.transform.position;
                    // Beyond the 20 second pre-entry lease TTL: physical occupancy
                    // must still prevent reassignment. Never delete the actor.
                    float until = Time.realtimeSinceStartup + 22;
                    yield return Wait(() => Time.realtimeSinceStartup >= until, 25, "Fault hold observation timeout");
                    Assert.Less(Vector3.Distance(stoppedPosition, occupant.transform.position), 0.01f);
                    Assert.Greater(expectedStale, 0, "Stale occupant sensor input must be rejected");
                }
                else yield return Wait(() => completed == fleetSize, 150, "Fleet services did not complete");
                var summaryTask = client.GetStringAsync(Endpoint(url, "/test/fleet-reservations"));
                yield return Wait(() => summaryTask.IsCompleted, 5, "Missing reservation audit");
                string json = summaryTask.GetAwaiter().GetResult();
                var summary = JsonUtility.FromJson<Summary>(json);
                if (!string.IsNullOrEmpty(trace)) File.WriteAllText(Path.Combine(Path.GetDirectoryName(trace), "reservation-events.json"), json);
                if (fault)
                {
                    Assert.AreEqual(1, entries.Count, "Waiting vehicle entered after an internal fault");
                    Assert.Greater(summary.claims, 0); Assert.Greater(summary.closed, 0);
                    Assert.Greater(summary.faults, 0); Assert.Greater(summary.pending, 0);
                    Assert.AreEqual(0, summary.completed);
                }
                else
                {
                    Assert.AreEqual(fleetSize, entries.Count); Assert.AreEqual(fleetSize, new HashSet<string>(entries).Count);
                    Assert.AreEqual(fleetSize, summary.completed); Assert.AreEqual(0, summary.claims);
                    Assert.AreEqual(0, summary.closed); Assert.AreEqual(0, summary.faults);
                    Assert.AreEqual(0, summary.pending);
                    Assert.AreEqual(fleetSize, summary.requests.Length);
                    var assigned = new HashSet<string>(); var requestIds = new HashSet<string>();
                    int cargo = 0;
                    foreach (var request in summary.requests)
                    {
                        Assert.AreEqual("COMPLETED", request.status);
                        Assert.IsTrue(assigned.Add(request.vehicle)); Assert.IsTrue(requestIds.Add(request.id));
                        if (request.service == "CARGO") { cargo++; Assert.AreEqual("V03", request.vehicle); }
                        else Assert.AreEqual("PASSENGER", request.service);
                    }
                    Assert.AreEqual(three ? 1 : 0, cargo);
                }
                Assert.IsFalse(overlap, "Actual vehicle collider overlap sampled");
                Assert.IsFalse(dualOccupancy, "Both physical bodies occupied the corridor");
                Assert.AreEqual(0, rejected);
                Assert.IsTrue(pcWait, "Operator never received resource wait reason");
                Assert.IsFalse(mobileLeak, "Passenger received another owner's state or actuator commands");
                Assert.IsFalse(mobileWaitEtaKnown, "Unbounded wait must not claim a known ETA");
                // Request servicing may let any vehicle obtain the first grant.
                // If V01 queued behind another, its owner must observe the wait.
                if (three && entries[0] != "V01")
                    Assert.IsTrue(mobileWait, "Passenger never received resource wait reason");
                Debug.Log($"Opposing fleet: size={fleetSize}, fault={fault}, entries={string.Join(",", entries)}, sensorACK={accepted}, rejected={rejected}, expectedStale={expectedStale}, completed={completed}");
            }
        }

        private static Uri Endpoint(string url, string path) => new UriBuilder(url) { Scheme = "http", Path = path, Query = "" }.Uri;
        private GameObject New(string name) { var obj = new GameObject(name); objects.Add(obj); return obj; }
        private IEnumerator Wait(Func<bool> predicate, float timeout, string message)
        {
            float deadline = Time.realtimeSinceStartup + timeout;
            while (!predicate() && Time.realtimeSinceStartup < deadline)
            {
                if (fleet.Count > 0)
                {
                    // Test-only Physics oracle. Hidden transforms are never sent to planning.
                    var zone = new Bounds(new Vector3(2, 0, 0), new Vector3(5, 2, 1.2f));
                    for (int i = 0; i < fleet.Count; i++)
                    {
                        var actor = fleet[i]; var collider = actor.GetComponent<BoxCollider>();
                        string id = actor.GetComponent<VehicleEgoLocalizationReporter>().VehicleId;
                        if (collider.bounds.Intersects(zone)) { if (inside.Add(id)) entries.Add(id); }
                        else inside.Remove(id);
                        for (int j = i + 1; j < fleet.Count; j++)
                        {
                            var other = fleet[j];
                            overlap |= Physics.ComputePenetration(collider, actor.transform.position, actor.transform.rotation,
                                other.GetComponent<BoxCollider>(), other.transform.position, other.transform.rotation, out _, out _);
                        }
                    }
                    dualOccupancy |= inside.Count > 1;
                }
                yield return new WaitForFixedUpdate();
            }
            Assert.IsTrue(predicate(), message);
        }
    }
}
