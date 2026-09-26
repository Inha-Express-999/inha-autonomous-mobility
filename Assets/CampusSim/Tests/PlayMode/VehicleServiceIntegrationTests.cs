using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using InhaExpress.Client.Domain;
using InhaExpress.Client.Networking;
using InhaExpress.Client.Presentation;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace InhaExpress.Client.Tests
{
    public sealed class VehicleServiceIntegrationTests
    {
        private readonly List<GameObject> objects = new List<GameObject>();
        private WebSocketClientDataSource mobileSource;

        [UnityTearDown]
        public IEnumerator Cleanup()
        {
            mobileSource?.Dispose();
            mobileSource = null;
            foreach (var item in objects)
                if (item != null) UnityEngine.Object.Destroy(item);
            objects.Clear();
            yield return null;
        }

        [UnityTest]
        [Category("PythonIntegration")]
        public IEnumerator MobilePassengerAndOperatorCargoCompleteUsingPhysicsSensorStopAndRecovery()
        {
            string url = Environment.GetEnvironmentVariable("INHA_UNITY_E2E_URL");
            if (string.IsNullOrWhiteSpace(url))
                Assert.Ignore("Run AgentScripts/RunUnityPhysicsIntegration.ps1 with its isolated Python server.");
            string version = Environment.GetEnvironmentVariable("INHA_UNITY_E2E_VERSION");
            string tracePath = Environment.GetEnvironmentVariable("INHA_UNITY_E2E_TRACE");
            var source = new WebSocketClientDataSource(url, ClientRole.PC_Operator, version);
            var host = NewObject("Integration host").AddComponent<ClientRuntimeHost>();
            host.Initialize(ClientRole.PC_Operator, source);
            int poseAcks = 0, sensorAcks = 0, rejectedSensors = 0;
            source.EgoLocalizationAcknowledged += ack => { if (ack.Accepted) poseAcks++; };
            source.SensorObservationAcknowledged += ack =>
            {
                if (ack.Accepted) sensorAcks++;
                else rejectedSensors++;
            };
            ServiceCommandAckDto requestAck = null;
            ServiceCommandAckDto cargoAck = null;
            source.CommandAcknowledged += ack => cargoAck = ack;
            const string passengerId = "physics-integration-passenger";
            mobileSource = new WebSocketClientDataSource(url, ClientRole.Mobile_Passenger, version, passengerId);
            mobileSource.CommandAcknowledged += ack => requestAck = ack;
            WorldSnapshotDto mobileSnapshot = null;
            mobileSource.SnapshotReceived += (snapshot, receivedAt) => mobileSnapshot = snapshot;

            // A small synthetic shell exercises the production spawner/reporter/rig.
            // The unconfigured template stays outside all sensor ranges.
            var template = NewObject("Synthetic integration shell");
            template.transform.position = new Vector3(1000f, 0f, 1000f);
            var body = template.AddComponent<Rigidbody>();
            body.isKinematic = true;
            body.useGravity = false;
            template.AddComponent<BoxCollider>().size = new Vector3(0.4f, 0.4f, 0.4f);
            template.AddComponent<VehicleEgoLocalizationReporter>();
            var spawner = NewObject("Integration spawner").AddComponent<VehicleActorSpawner>();
            ConfigureSpawner(spawner, template);
            spawner.Bind(host);

            var obstacle = NewObject("Raycast integration obstacle");
            obstacle.transform.position = new Vector3(6f, 0f, 0f);
            obstacle.AddComponent<BoxCollider>().size = new Vector3(0.3f, 2f, 2f);
            Physics.SyncTransforms();
            var statuses = new HashSet<RequestStatus>();
            VehicleDto latestVehicle = null;
            bool completed = false;
            host.Store.SnapshotChanged += snapshot =>
            {
                foreach (var vehicle in snapshot.Vehicles)
                    if (vehicle.Id == "V01") latestVehicle = vehicle;
                foreach (var request in snapshot.Requests)
                {
                    statuses.Add(request.Status);
                    if (request.Status == RequestStatus.COMPLETED) completed = true;
                }
                if (!string.IsNullOrWhiteSpace(tracePath) && latestVehicle != null)
                    File.AppendAllText(tracePath, string.Format(System.Globalization.CultureInfo.InvariantCulture,
                        "{0},{1:F3},{2:F3},{3:F3},{4},{5},{6}\n", snapshot.SimulationTick,
                        latestVehicle.Position.X, latestVehicle.Position.Y, latestVehicle.SpeedMps,
                        latestVehicle.MotionState, latestVehicle.Reason,
                        snapshot.Requests.Count > 0 ? snapshot.Requests[0].Status.ToString() : "NONE"));
            };
            host.StartSource();
            mobileSource.Start(Time.realtimeSinceStartupAsDouble);
            yield return WaitFor(() => poseAcks >= 2 && sensorAcks >= 2 && mobileSnapshot != null, 20f,
                () => $"telemetry: poses={poseAcks}, sensors={sensorAcks}, rejected={rejectedSensors}");
            mobileSource.SendPassengerRequest(new PassengerRequestCommandDto(
                "fixture_landmark_2", "fixture_landmark_3", new ServiceNeedsDto(false, 0, false)));
            yield return WaitFor(() => requestAck != null, 10f, () => "No request ACK");
            Assert.That(requestAck.Accepted, Is.True, requestAck.ErrorCode);

            yield return WaitFor(() => latestVehicle?.Reason == ReasonCode.OBSTACLE_STOP, 60f,
                () => $"No obstacle stop: x={latestVehicle?.Position.X}, reason={latestVehicle?.Reason}");
            var actor = GameObject.Find("Vehicle V01");
            Assert.That(actor, Is.Not.Null);
            var follower = actor.GetComponent<VehicleRouteFollower>();
            yield return WaitFor(() => follower.SpeedMps < 0.01f, 3f, () => "Vehicle failed to brake");
            Assert.That(actor.transform.position.x, Is.InRange(4f, 5.65f));
            Assert.That(completed, Is.False);
            float heldX = actor.transform.position.x;
            yield return new WaitForSeconds(0.3f);
            Assert.That(actor.transform.position.x, Is.EqualTo(heldX).Within(0.02f));

            obstacle.SetActive(false);
            yield return WaitFor(() => latestVehicle?.Reason == ReasonCode.SAFETY_RESUME_HOLD, 3f,
                () => "No server clear-frame hold after obstacle removal");
            Assert.That(follower.MotionAuthorized, Is.False);
            yield return WaitFor(() => completed, 60f,
                () => $"Not completed: x={latestVehicle?.Position.X}, reason={latestVehicle?.Reason}");
            Assert.That(statuses, Does.Contain(RequestStatus.PICKUP_SERVICE));
            Assert.That(statuses, Does.Contain(RequestStatus.IN_TRANSIT));
            Assert.That(statuses, Does.Contain(RequestStatus.DROPOFF_SERVICE));
            Assert.That(actor.transform.position.x, Is.InRange(7f, 8.2f));
            Assert.That(sensorAcks, Is.GreaterThan(20));
            yield return WaitFor(() => mobileSnapshot.Requests.Count == 1 &&
                mobileSnapshot.Requests[0].Status == RequestStatus.COMPLETED, 5f,
                () => "Mobile did not receive passenger completion");
            Assert.That(mobileSnapshot.Requests[0].Id, Is.EqualTo(requestAck.Request.Id));
            Assert.That(mobileSnapshot.Requests[0].OwnerId, Is.EqualTo(passengerId));
            Assert.That(mobileSnapshot.RunId, Is.EqualTo(host.Store.Current.RunId));

            source.SendCargoRequest(new CargoRequestCommandDto(
                "fixture_landmark_3", "fixture_landmark_1", 2.0));
            yield return WaitFor(() => cargoAck != null, 10f, () => "No operator cargo ACK");
            Assert.That(cargoAck.Accepted, Is.True, cargoAck.ErrorCode);
            yield return WaitFor(() => host.Store.Requests.TryGetValue(cargoAck.Request.Id, out var cargo) &&
                cargo.Status == RequestStatus.COMPLETED, 90f,
                () => $"Cargo incomplete: x={latestVehicle?.Position.X}, reason={latestVehicle?.Reason}");
            Assert.That(actor.transform.position.x, Is.InRange(-0.2f, 1f));
            Assert.That(mobileSnapshot.Requests.Count, Is.EqualTo(1), "Operator cargo must stay private.");
            Assert.That(mobileSnapshot.Requests[0].Id, Is.EqualTo(requestAck.Request.Id));
            Assert.That(rejectedSensors, Is.EqualTo(0));
            Debug.Log($"Physics passenger/cargo completed: poseACK={poseAcks}, sensorACK={sensorAcks}, rejectedSensor={rejectedSensors}");
        }

        private GameObject NewObject(string name)
        {
            var item = new GameObject(name);
            objects.Add(item);
            return item;
        }

        private IEnumerator WaitFor(Func<bool> predicate, float seconds, Func<string> diagnostic)
        {
            float deadline = Time.realtimeSinceStartup + seconds;
            while (!predicate() && Time.realtimeSinceStartup < deadline)
            {
                mobileSource?.Pump(Time.realtimeSinceStartupAsDouble);
                yield return null;
            }
            Assert.That(predicate(), Is.True, diagnostic());
        }

        private static void ConfigureSpawner(VehicleActorSpawner spawner, GameObject template)
        {
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
            var type = typeof(VehicleActorSpawner);
            type.GetField("requiredMapVersion", flags).SetValue(spawner, "synthetic-physics-integration-v1");
            var field = type.GetField("vehiclePrefabs", flags);
            var bindingType = field.FieldType.GetElementType();
            var binding = Activator.CreateInstance(bindingType, true);
            bindingType.GetField("vehicleId").SetValue(binding, "V01");
            bindingType.GetField("prefab").SetValue(binding, template);
            var bindings = Array.CreateInstance(bindingType, 1);
            bindings.SetValue(binding, 0);
            field.SetValue(spawner, bindings);
        }
    }
}
