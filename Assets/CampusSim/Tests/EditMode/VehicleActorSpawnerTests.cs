using System;
using System.Reflection;
using InhaExpress.Client.Domain;
using InhaExpress.Client.Networking;
using InhaExpress.Client.Presentation;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace InhaExpress.Client.Tests
{
    public sealed class VehicleActorSpawnerTests
    {
        private const string VehicleId = "spawner-test-vehicle";
        private const string ActorPrefabPath = "Assets/CampusSim/Prefabs/Vehicles/VehicleActor_Default.prefab";

        private GameObject hostObject;
        private GameObject spawnerObject;
        private ClientRuntimeHost host;
        private VehicleActorSpawner spawner;

        [TestCase("VehicleActor_Default")]
        [TestCase("VehicleActor_Annyoung")]
        [TestCase("VehicleActor_Induck")]
        public void OriginalActorWrapperHasResolvedVisualAndMeasuredCollider(string name)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                "Assets/CampusSim/Prefabs/Vehicles/" + name + ".prefab");
            Assert.That(prefab, Is.Not.Null);
            var instance = UnityEngine.Object.Instantiate(prefab);
            try
            {
                Assert.That(instance.transform.localScale, Is.EqualTo(Vector3.one));
                var body = instance.GetComponent<Rigidbody>();
                Assert.That(body, Is.Not.Null);
                Assert.That(body.isKinematic, Is.True);
                Assert.That(body.useGravity, Is.False);
                var collider = instance.GetComponent<BoxCollider>();
                Assert.That(collider, Is.Not.Null);
                Assert.That(instance.GetComponent<VehicleEgoLocalizationReporter>(), Is.Not.Null);
                foreach (var child in instance.GetComponentsInChildren<Transform>(true))
                    Assert.That(GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(child.gameObject), Is.Zero);
                var renderers = instance.GetComponentsInChildren<Renderer>(true);
                Assert.That(renderers.Length, Is.GreaterThan(0), "Nested vehicle visual must resolve.");
                var bounds = renderers[0].bounds;
                foreach (var renderer in renderers) bounds.Encapsulate(renderer.bounds);
                Assert.That(bounds.size.sqrMagnitude, Is.GreaterThan(0));
                Physics.SyncTransforms();
                Assert.That(Vector3.Distance(collider.bounds.center, bounds.center), Is.LessThan(0.02f));
                Assert.That(Vector3.Distance(collider.bounds.size, bounds.size), Is.LessThan(0.02f));
            }
            finally { UnityEngine.Object.DestroyImmediate(instance); }
        }

        [SetUp]
        public void SetUp()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(ActorPrefabPath);
            Assert.That(prefab, Is.Not.Null, "Default vehicle actor prefab must exist.");
            var routePrefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                "Assets/CampusSim/Prefabs/Navigation/VehicleRouteLine.prefab");
            Assert.That(routePrefab, Is.Not.Null, "A reusable route overlay prefab must exist.");

            spawnerObject = new GameObject("VehicleActorSpawner test component");
            spawner = spawnerObject.AddComponent<VehicleActorSpawner>();
            var serialized = new SerializedObject(spawner);
            serialized.FindProperty("requiredMapVersion").stringValue = "synthetic-spawner-test-v1";
            serialized.FindProperty("spawnHeightOffsetM").floatValue = 0.37f;
            serialized.FindProperty("routeOverlayPrefab").objectReferenceValue = routePrefab;
            var bindings = serialized.FindProperty("vehiclePrefabs");
            bindings.arraySize = 1;
            var binding = bindings.GetArrayElementAtIndex(0);
            binding.FindPropertyRelative("vehicleId").stringValue = VehicleId;
            binding.FindPropertyRelative("prefab").objectReferenceValue = prefab;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        [TearDown]
        public void TearDown()
        {
            var actor = GameObject.Find("Vehicle " + VehicleId);
            if (actor != null) UnityEngine.Object.DestroyImmediate(actor);
            if (hostObject != null) UnityEngine.Object.DestroyImmediate(hostObject);
            if (spawnerObject != null) UnityEngine.Object.DestroyImmediate(spawnerObject);
        }

        [Test]
        public void ServerSnapshotSpawnsActorWithConvertedPoseAndReporterIdOnlyOnce()
        {
            BindHost(new WebSocketClientDataSource("ws://127.0.0.1:1/v1/client/ws",
                ClientRole.PC_Operator, "0.2.3.0"));
            var vehicle = new VehicleDto(VehicleId, new MapPositionDto(12.0, 4.0, -7.0),
                Math.PI / 3.0, 0.0, null, VehicleMissionState.TO_PICKUP, VehicleMotionState.DRIVING,
                routeId: "route-test");
            var unconfiguredVehicle = new VehicleDto("V02", new MapPositionDto(0.0, 0.0, 0.0),
                0.0, 0.0, null, VehicleMissionState.IDLE, VehicleMotionState.WAITING_RESOURCE);
            var route = new RouteDto("route-test", "synthetic-spawner-test-v1",
                new[] { new MapPositionDto(12.0, 4.0, -7.0), new MapPositionDto(14.0, 4.0, -7.0) });
            host.Store.ApplySnapshot(CreateSnapshot("run-a", 1, new[] { vehicle, unconfiguredVehicle }, route), 1.0);

            var actor = GameObject.Find("Vehicle " + VehicleId);
            Assert.That(actor, Is.Not.Null);
            Assert.That(actor.transform.position, Is.EqualTo(
                MapCoordinateConverter.ToUnity(vehicle.Position) + Vector3.up * 0.37f));
            Assert.That(Quaternion.Angle(actor.transform.rotation,
                Quaternion.Euler(0f, MapCoordinateConverter.ToUnityYaw(vehicle.HeadingRad), 0f)), Is.LessThan(0.1f));

            var reporter = actor.GetComponent<VehicleEgoLocalizationReporter>();
            Assert.That(reporter, Is.Not.Null);
            Assert.That(reporter.VehicleId, Is.EqualTo(VehicleId));
            var vehicleLayer = LayerMask.NameToLayer("Vehicle");
            Assert.That(vehicleLayer, Is.GreaterThanOrEqualTo(0), "Vehicle physics layer must be configured.");
            foreach (var child in actor.GetComponentsInChildren<Transform>(true))
                Assert.That(child.gameObject.layer, Is.EqualTo(vehicleLayer),
                    "Every vehicle collider visual must share the Vehicle layer for sensor classification.");
            Assert.That(actor.GetComponent<VehicleRaycastSensorRig>(), Is.Not.Null);
            var follower = actor.GetComponent<VehicleRouteFollower>();
            Assert.That(follower, Is.Not.Null);
            Assert.That(follower.RouteId, Is.EqualTo("route-test"));
            Assert.That(follower.MotionAuthorized, Is.True);
            var routeLine = actor.GetComponentInChildren<LineRenderer>();
            Assert.That(routeLine, Is.Not.Null, "The PC preview should render the server route beside each actor.");
            Assert.That(routeLine.positionCount, Is.EqualTo(2), "Display the complete immutable server geometry.");
            Assert.That(Vector3.Distance(routeLine.GetPosition(0),
                MapCoordinateConverter.ToUnity(route.Polyline[0]) + Vector3.up * 0.12f),
                Is.LessThan(0.001f));
            Assert.That(Vector3.Distance(routeLine.GetPosition(1),
                MapCoordinateConverter.ToUnity(route.Polyline[1]) + Vector3.up * 0.12f), Is.LessThan(0.001f));
            Assert.That(GameObject.Find("Vehicle V02"), Is.Null,
                "Vehicles without an explicit runtime prefab binding must not spawn at the origin.");

            host.Store.ApplySnapshot(CreateSnapshot("run-a", 2, new[] { vehicle, unconfiguredVehicle }, route), 1.1);
            Assert.That(CountActiveReporters(VehicleId), Is.EqualTo(1), "A later snapshot in the same run must not duplicate the actor.");
            var originalStart = routeLine.GetPosition(0);
            actor.GetComponent<Rigidbody>().position += Vector3.right;
            host.Store.ApplySnapshot(CreateSnapshot("run-a", 3, new[] { vehicle, unconfiguredVehicle }, route), 1.2);
            Assert.That(routeLine.GetPosition(0), Is.EqualTo(originalStart), "Actor motion cannot rewrite route geometry.");
        }

        [Test]
        public void NonDrivingSnapshotRevokesMotionWithoutDiscardingRouteProgress()
        {
            BindHost(new WebSocketClientDataSource("ws://127.0.0.1:1/v1/client/ws",
                ClientRole.PC_Operator, "0.2.3.0"));
            var route = new RouteDto("route-test", "synthetic-spawner-test-v1",
                new[] { new MapPositionDto(12.0, 4.0, -7.0), new MapPositionDto(14.0, 4.0, -7.0) });
            var driving = new VehicleDto(VehicleId, new MapPositionDto(12.0, 4.0, -7.0),
                0.0, 0.0, null, VehicleMissionState.TO_PICKUP, VehicleMotionState.DRIVING,
                routeId: route.Id);
            host.Store.ApplySnapshot(CreateSnapshot("run-stop", 1, new[] { driving }, route), 1.0);

            var actor = GameObject.Find("Vehicle " + VehicleId);
            var follower = actor.GetComponent<VehicleRouteFollower>();
            typeof(VehicleRouteFollower).GetField("waypointIndex", BindingFlags.Instance | BindingFlags.NonPublic)
                .SetValue(follower, 1);
            int progressBeforeStop = follower.WaypointIndex;
            var stale = new VehicleDto(VehicleId, driving.Position, 0.0, 0.0, null,
                VehicleMissionState.TO_PICKUP, VehicleMotionState.REPLANNING,
                routeId: route.Id, reason: ReasonCode.STALE_LOCALIZATION);
            host.Store.ApplySnapshot(CreateSnapshot("run-stop", 2, new[] { stale }, route), 1.1);

            Assert.That(follower.RouteId, Is.EqualTo(route.Id), "A temporary hold preserves the assigned route.");
            Assert.That(follower.WaypointIndex, Is.EqualTo(progressBeforeStop));
            Assert.That(follower.MotionAuthorized, Is.False);
            var routeLine = actor.GetComponentInChildren<LineRenderer>();
            // LineRenderer stores colors with 8-bit channel precision.
            var heldColor = routeLine.startColor;
            Assert.That(heldColor.r, Is.EqualTo(1.00f).Within(1f / 255f));
            Assert.That(heldColor.g, Is.EqualTo(0.60f).Within(1f / 255f));
            Assert.That(heldColor.b, Is.EqualTo(0.10f).Within(1f / 255f));
            Assert.That(heldColor.a, Is.EqualTo(0.95f).Within(1f / 255f));

            host.Store.ApplySnapshot(CreateSnapshot("run-stop", 3, new[] { driving }, route), 1.2);
            Assert.That(follower.MotionAuthorized, Is.True, "Motion resumes only after the server restores DRIVING.");
            Assert.That(follower.WaypointIndex, Is.EqualTo(progressBeforeStop));
        }

        [Test]
        public void FixtureSnapshotDoesNotSpawnPhysicsActors()
        {
            BindHost(new FixtureClientDataSource(ClientRole.PC_Operator, "0.2.3.0"));

            host.StartSource();

            Assert.That(GameObject.Find("Vehicle " + VehicleId), Is.Null);
            Assert.That(CountActiveReporters(VehicleId), Is.Zero);
        }

        [Test]
        public void SnapshotWithDifferentMapVersionDoesNotSpawnOnCampusScene()
        {
            BindHost(new WebSocketClientDataSource("ws://127.0.0.1:1/v1/client/ws",
                ClientRole.PC_Operator, "0.2.3.0"));
            var vehicle = new VehicleDto(VehicleId, new MapPositionDto(0.0, 0.0, 0.0),
                0.0, 0.0, null, VehicleMissionState.IDLE, VehicleMotionState.WAITING_RESOURCE);

            host.Store.ApplySnapshot(CreateSnapshot("run-unmatched-map", 1,
                "synthetic-campus-6stop-v1", vehicle), 1.0);

            Assert.That(GameObject.Find("Vehicle " + VehicleId), Is.Null);
            Assert.That(CountActiveReporters(VehicleId), Is.Zero);
        }

        [Test]
        public void RouteFollowerRejectsNonSyntheticAndMismatchedMapVersions()
        {
            var followerObject = new GameObject("VehicleRouteFollower validation test");
            try
            {
                var follower = followerObject.AddComponent<VehicleRouteFollower>();
                var points = new[] { new MapPositionDto(0.0, 0.0, 0.0), new MapPositionDto(1.0, 0.0, 0.0) };
                Assert.That(follower.ApplyRoute(new RouteDto("real-route", "campus-map-v1", points),
                    "campus-map-v1", 1.0), Is.False);
                Assert.That(follower.ApplyRoute(new RouteDto("wrong-map-route", "synthetic-map-a", points),
                    "synthetic-map-b", 1.0), Is.False);
                Assert.That(follower.ApplyRoute(new RouteDto("valid-route", "synthetic-map-a", points),
                    "synthetic-map-a", 1.0), Is.True);
                Assert.That(follower.RouteId, Is.EqualTo("valid-route"));
                follower.ClearRoute();
                Assert.That(follower.RouteId, Is.Null);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(followerObject);
            }
        }

        [Test]
        public void ReusedRouteIdWithChangedGeometryClearsRouteAndRevokesMotionAuthority()
        {
            var followerObject = new GameObject("VehicleRouteFollower route identity test");
            try
            {
                var follower = followerObject.AddComponent<VehicleRouteFollower>();
                var original = new RouteDto("immutable-route", "synthetic-map-a", new[]
                {
                    new MapPositionDto(0.0, 0.0, 0.0),
                    new MapPositionDto(1.0, 0.0, 0.0)
                });
                Assert.That(follower.ApplyRoute(original, original.MapVersion, 1.0), Is.True);
                follower.SetMotionAuthorized(true);

                var changedGeometry = new RouteDto("immutable-route", "synthetic-map-a", new[]
                {
                    new MapPositionDto(0.0, 0.0, 0.0),
                    new MapPositionDto(0.0, 1.0, 0.0)
                });

                Assert.That(follower.ApplyRoute(changedGeometry, changedGeometry.MapVersion, 1.1), Is.False);
                Assert.That(follower.MotionAuthorized, Is.False);
                Assert.That(follower.RouteId, Is.Null);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(followerObject);
            }
        }

        [Test]
        public void RejectedMapUpdateClearsPreviouslyAuthorizedRoute()
        {
            var followerObject = new GameObject("VehicleRouteFollower rejected map test");
            try
            {
                var follower = followerObject.AddComponent<VehicleRouteFollower>();
                var accepted = new RouteDto("accepted-route", "synthetic-map-a", new[]
                {
                    new MapPositionDto(0.0, 0.0, 0.0),
                    new MapPositionDto(1.0, 0.0, 0.0)
                });
                Assert.That(follower.ApplyRoute(accepted, accepted.MapVersion, 1.0), Is.True);
                follower.SetMotionAuthorized(true);

                var mismatched = new RouteDto("foreign-route", "synthetic-map-b", new[]
                {
                    new MapPositionDto(0.0, 0.0, 0.0),
                    new MapPositionDto(0.0, 1.0, 0.0)
                });
                Assert.That(follower.ApplyRoute(mismatched, "synthetic-map-a", 1.1), Is.False);
                Assert.That(follower.MotionAuthorized, Is.False);
                Assert.That(follower.RouteId, Is.Null);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(followerObject);
            }
        }

        [Test]
        public void MissingRouteOrMapChangeClearsPreviouslyAcceptedRoute()
        {
            BindHost(new WebSocketClientDataSource("ws://127.0.0.1:1/v1/client/ws",
                ClientRole.PC_Operator, "0.2.3.0"));
            var route = new RouteDto("route-test", "synthetic-spawner-test-v1",
                new[] { new MapPositionDto(12.0, 4.0, -7.0), new MapPositionDto(14.0, 4.0, -7.0) });
            var activeVehicle = new VehicleDto(VehicleId, new MapPositionDto(12.0, 4.0, -7.0),
                0.0, 0.0, null, VehicleMissionState.TO_PICKUP, VehicleMotionState.DRIVING,
                routeId: route.Id);
            host.Store.ApplySnapshot(CreateSnapshot("run-clear", 1,
                new[] { activeVehicle }, route), 1.0);

            var actor = GameObject.Find("Vehicle " + VehicleId);
            Assert.That(actor, Is.Not.Null);
            var follower = actor.GetComponent<VehicleRouteFollower>();
            Assert.That(follower.RouteId, Is.EqualTo(route.Id));
            var routeLine = actor.GetComponentInChildren<LineRenderer>();
            Assert.That(routeLine, Is.Not.Null);
            Assert.That(routeLine.positionCount, Is.GreaterThan(1));

            var idleVehicle = new VehicleDto(VehicleId, activeVehicle.Position, 0.0, 0.0, null,
                VehicleMissionState.IDLE, VehicleMotionState.WAITING_RESOURCE);
            host.Store.ApplySnapshot(CreateSnapshot("run-clear", 2, idleVehicle), 1.1);
            Assert.That(follower.RouteId, Is.Null, "A snapshot without the assigned route must stop following it.");
            Assert.That(routeLine.positionCount, Is.Zero, "The visualization must clear when no route is assigned.");

            host.Store.ApplySnapshot(CreateSnapshot("run-clear-map-change", 3,
                "synthetic-other-map", idleVehicle), 1.2);
            Assert.That(follower.RouteId, Is.Null, "A map version change must clear the current route immediately.");
        }

        private static WorldSnapshotDto CreateSnapshot(string runId, long sequence, params VehicleDto[] vehicles) =>
            CreateSnapshot(runId, sequence, "synthetic-spawner-test-v1", vehicles);

        private static WorldSnapshotDto CreateSnapshot(string runId, long sequence,
            VehicleDto[] vehicles, params RouteDto[] routes) =>
            CreateSnapshot(runId, sequence, "synthetic-spawner-test-v1", vehicles, routes);

        private static WorldSnapshotDto CreateSnapshot(string runId, long sequence, string mapVersion,
            params VehicleDto[] vehicles) => CreateSnapshot(runId, sequence, mapVersion, vehicles, Array.Empty<RouteDto>());

        private static WorldSnapshotDto CreateSnapshot(string runId, long sequence, string mapVersion,
            VehicleDto[] vehicles, RouteDto[] routes) =>
            new WorldSnapshotDto(WorldSnapshotDto.SupportedSchemaVersion, "0.2.3.0", mapVersion,
                runId, sequence, sequence, sequence * 0.05, ClientRole.PC_Operator, null,
                vehicles, Array.Empty<RequestDto>(), Array.Empty<LandmarkDto>(), Array.Empty<StopDto>(),
                routes, Array.Empty<ZoneDto>());

        private void BindHost(IClientDataSource source)
        {
            hostObject = new GameObject("VehicleActorSpawner test host");
            host = hostObject.AddComponent<ClientRuntimeHost>();
            host.Initialize(ClientRole.PC_Operator, source);
            spawner.Bind(host);
        }

        private static int CountActiveReporters(string vehicleId)
        {
            int count = 0;
            foreach (var reporter in UnityEngine.Object.FindObjectsByType<VehicleEgoLocalizationReporter>(FindObjectsSortMode.None))
                if (reporter.VehicleId == vehicleId) count++;
            return count;
        }
    }
}
