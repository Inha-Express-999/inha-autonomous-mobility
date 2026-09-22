using System;
using System.Collections.Generic;
using InhaExpress.Client.Domain;
using InhaExpress.Client.Presentation;
using NUnit.Framework;
using UnityEngine;

namespace InhaExpress.Client.Tests
{
    public sealed class ClientStateTests
    {
        private static readonly MapPositionDto Position = new MapPositionDto(-12.25, 30.5, 2.75);
        private static readonly ServiceNeedsDto Needs = new ServiceNeedsDto(false, 0, false);

        private static VehicleDto Vehicle(string id = "v1", string route = null) =>
            new VehicleDto(id, Position, Math.PI / 2, 0, null, VehicleMissionState.IDLE,
                VehicleMotionState.WAITING_RESOURCE, routeId: route);

        private static RequestDto Request(string owner = "p1", string vehicle = null,
            RequestStatus status = RequestStatus.CREATED) =>
            new RequestDto("r1", owner, ServiceType.PASSENGER, status, 0, "a", "b",
                null, null, Needs, 1, 0, vehicle);

        private static WorldSnapshotDto Snapshot(string run = "run1", long seq = 1, long tick = 1,
            VehicleDto[] vehicles = null, RequestDto[] requests = null,
            ClientRole role = ClientRole.PC_Operator, string subscriber = null,
            ZoneDto[] zones = null, RouteDto[] routes = null, int schema = 3,
            string version = "0.1.3.0", string map = "synthetic-v1") =>
            new WorldSnapshotDto(schema, version, map, run, seq, tick, tick * 0.05, role, subscriber,
                vehicles ?? Array.Empty<VehicleDto>(), requests ?? Array.Empty<RequestDto>(),
                new[] { new LandmarkDto("a", "Synthetic A", "fixture", VerificationStatus.SYNTHETIC, Array.Empty<string>()),
                    new LandmarkDto("b", "Synthetic B", "fixture", VerificationStatus.SYNTHETIC, Array.Empty<string>()) },
                Array.Empty<StopDto>(), routes ?? Array.Empty<RouteDto>(), zones ?? Array.Empty<ZoneDto>());

        [Test]
        public void SnapshotPublishesBeforeNotificationAndSupportsIdLookup()
        {
            var store = new WorldStateStore(ClientRole.PC_Operator);
            var snapshot = Snapshot(vehicles: new[] { Vehicle() }, requests: new[] { Request(vehicle: "v1") });
            int notifications = 0;
            store.SnapshotChanged += value =>
            {
                Assert.That(store.Current, Is.SameAs(value));
                Assert.That(store.Vehicles["v1"].Id, Is.EqualTo("v1"));
                notifications++;
            };
            Assert.That(store.ApplySnapshot(snapshot, 0), Is.EqualTo(SnapshotApplyResult.Applied));
            Assert.That(store.Requests["r1"].VehicleId, Is.EqualTo("v1"));
            Assert.That(notifications, Is.EqualTo(1));
        }

        [TestCase(1)]
        [TestCase(0)]
        public void OldSequenceDoesNotReplaceStateNotifyOrRefreshFreshness(long sequence)
        {
            var store = new WorldStateStore(ClientRole.PC_Operator);
            store.ApplySnapshot(Snapshot(), 0);
            var current = store.Current;
            int notifications = 0;
            store.SnapshotChanged += _ => notifications++;
            Assert.That(store.ApplySnapshot(Snapshot(seq: sequence), 0.9), Is.EqualTo(SnapshotApplyResult.IgnoredOldSequence));
            Assert.That(store.Current, Is.SameAs(current));
            Assert.That(notifications, Is.Zero);
            Assert.That(store.IsStale(1), Is.True);
        }

        [Test]
        public void RunChangeClearsEntitiesAndDelayedOldRunCannotRestoreThem()
        {
            var store = new WorldStateStore(ClientRole.PC_Operator);
            store.ApplySnapshot(Snapshot(seq: 100, vehicles: new[] { Vehicle() }), 0);
            store.ApplySnapshot(Snapshot(run: "run2", seq: 0, tick: 0), 1);
            Assert.That(store.Vehicles, Is.Empty);
            Assert.That(store.ApplySnapshot(Snapshot(seq: 101), 2), Is.EqualTo(SnapshotApplyResult.IgnoredRetiredRun));
            Assert.That(store.Current.RunId, Is.EqualTo("run2"));
        }

        [Test]
        public void DuplicateVehicleIsRejectedAtomicallyEvenForNewRun()
        {
            var store = new WorldStateStore(ClientRole.PC_Operator);
            store.ApplySnapshot(Snapshot(), 0);
            var current = store.Current;
            Assert.Throws<ArgumentException>(() => store.ApplySnapshot(
                Snapshot(run: "run2", vehicles: new[] { Vehicle(), Vehicle() }), 0.5));
            Assert.That(store.Current, Is.SameAs(current));
            Assert.That(store.IsStale(1), Is.True);
            Assert.That(store.ApplySnapshot(Snapshot(seq: 2), 1), Is.EqualTo(SnapshotApplyResult.Applied));
        }

        [Test]
        public void DuplicateRequestRejected()
        {
            var store = new WorldStateStore(ClientRole.PC_Operator);
            Assert.Throws<ArgumentException>(() => store.ApplySnapshot(Snapshot(requests: new[] { Request(), Request() }), 0));
            Assert.That(store.Current, Is.Null);
        }

        [Test]
        public void MissingVehicleReferenceRejected()
        {
            var store = new WorldStateStore(ClientRole.PC_Operator);
            Assert.Throws<ArgumentException>(() => store.ApplySnapshot(Snapshot(requests: new[] { Request(vehicle: "missing") }), 0));
        }

        [Test]
        public void TickRegressionAndMapChangeRejectedWithinRun()
        {
            var store = new WorldStateStore(ClientRole.PC_Operator);
            store.ApplySnapshot(Snapshot(tick: 10), 0);
            Assert.Throws<ArgumentException>(() => store.ApplySnapshot(Snapshot(seq: 2, tick: 9), 1));
            Assert.Throws<ArgumentException>(() => store.ApplySnapshot(Snapshot(seq: 2, tick: 10, map: "other"), 1));
        }

        [Test]
        public void PauseAllowsIncreasingSequenceAtSameTick()
        {
            var store = new WorldStateStore(ClientRole.PC_Operator);
            store.ApplySnapshot(Snapshot(), 0);
            store.ApplySnapshot(Snapshot(seq: 2), 2);
            Assert.That(store.IsStale(2.5), Is.False);
        }

        [Test]
        public void SnapshotOwnsImmutableCollections()
        {
            var input = new[] { Vehicle() };
            var snapshot = Snapshot(vehicles: input);
            input[0] = Vehicle("changed");
            Assert.That(snapshot.Vehicles[0].Id, Is.EqualTo("v1"));
            Assert.Throws<NotSupportedException>(() => ((IList<VehicleDto>)snapshot.Vehicles).Clear());
            var store = new WorldStateStore(ClientRole.PC_Operator);
            store.ApplySnapshot(snapshot, 0);
            Assert.Throws<NotSupportedException>(() => ((IDictionary<string, VehicleDto>)store.Vehicles).Clear());
        }

        [Test]
        public void RouteGeometryIsDefensivelyCopied()
        {
            var points = new[] { Position };
            var route = new RouteDto("route", "map", points);
            points[0] = new MapPositionDto(0, 0, 0);
            Assert.That(route.Polyline[0].X, Is.EqualTo(Position.X));
        }

        [Test]
        public void MobileAcceptsOwnRequestAndAssignedVehicle()
        {
            var store = new WorldStateStore(ClientRole.Mobile_Passenger, "p1");
            store.ApplySnapshot(Snapshot(role: ClientRole.Mobile_Passenger, subscriber: "p1",
                requests: new[] { Request(vehicle: "v1") }, vehicles: new[] { Vehicle() }), 0);
            Assert.That(store.Requests.Count, Is.EqualTo(1));
        }

        [TestCase("p2", false, false)]
        [TestCase("p1", true, false)]
        [TestCase("p1", false, true)]
        public void MobileRejectsOtherOwnersUnassignedVehiclesAndZones(string owner, bool extraVehicle, bool zone)
        {
            var store = new WorldStateStore(ClientRole.Mobile_Passenger, "p1");
            var snapshot = Snapshot(role: ClientRole.Mobile_Passenger, subscriber: "p1", requests: new[] { Request(owner) },
                vehicles: extraVehicle ? new[] { Vehicle() } : null,
                zones: zone ? new[] { new ZoneDto("z", ZoneStatus.NORMAL, null, null, null) } : null);
            Assert.Throws<ArgumentException>(() => store.ApplySnapshot(snapshot, 0));
        }

        [Test]
        public void MobileRejectsOperatorSnapshotAndWrongSubscription()
        {
            var store = new WorldStateStore(ClientRole.Mobile_Passenger, "p1");
            Assert.Throws<ArgumentException>(() => store.ApplySnapshot(Snapshot(), 0));
            Assert.Throws<ArgumentException>(() => store.ApplySnapshot(Snapshot(role: ClientRole.Mobile_Passenger, subscriber: "p2"), 0));
        }

        [Test]
        public void MobileRejectsUnrelatedRoute()
        {
            var store = new WorldStateStore(ClientRole.Mobile_Passenger, "p1");
            Assert.Throws<ArgumentException>(() => store.ApplySnapshot(Snapshot(role: ClientRole.Mobile_Passenger,
                subscriber: "p1", routes: new[] { new RouteDto("route", "synthetic-v1", new[] { Position }) }), 0));
        }

        [Test]
        public void SchemaAndProjectVersionAreIndependent()
        {
            Assert.Throws<ArgumentException>(() => Snapshot(schema: 2));
            Assert.Throws<ArgumentException>(() => Snapshot(version: "1.0"));
            Assert.That(Snapshot(version: "0.999999999999999999999.0.0").SchemaVersion, Is.EqualTo(3));
        }

        [TestCase(-1, 0)]
        [TestCase(0, -1)]
        public void NegativeSequenceOrTickRejected(long seq, long tick) =>
            Assert.Throws<ArgumentOutOfRangeException>(() => Snapshot(seq: seq, tick: tick));

        [Test]
        public void InvalidNeedsRejected()
        {
            Assert.Throws<ArgumentException>(() => new ServiceNeedsDto(false, 1, false));
            Assert.Throws<ArgumentOutOfRangeException>(() => new ServiceNeedsDto(true, -1, false));
        }

        [Test]
        public void MissingIdUnknownEnumAndNonFiniteNumbersRejected()
        {
            Assert.Throws<ArgumentException>(() => Vehicle(" "));
            Assert.Throws<ArgumentOutOfRangeException>(() => new ZoneDto("z", (ZoneStatus)99, null, null, null));
            Assert.Throws<ArgumentOutOfRangeException>(() => new MapPositionDto(double.NaN, 0, 0));
            Assert.Throws<ArgumentOutOfRangeException>(() => new ZoneDto("z", ZoneStatus.NORMAL, double.PositiveInfinity, null, null));
            Assert.Throws<ArgumentOutOfRangeException>(() => new ZoneDto("z", ZoneStatus.NORMAL, -1, null, null));
        }

        [Test]
        public void UnconfirmedStopsAllowedOnlyBeforeValidation()
        {
            Assert.That(Request().PickupStopId, Is.Null);
            Assert.Throws<ArgumentException>(() => Request(status: RequestStatus.VALIDATED));
        }

        [Test]
        public void UnknownMeasurementsRemainNull()
        {
            Assert.That(Vehicle().BatteryWh, Is.Null);
            Assert.That(Request().EtaS, Is.Null);
            Assert.That(new ZoneDto("z", ZoneStatus.NORMAL, null, null, null).ObservedDensity, Is.Null);
        }

        [Test]
        public void RealTimeStalenessStartsAtOneSecondAndRejectsClockRegression()
        {
            var store = new WorldStateStore(ClientRole.PC_Operator);
            Assert.That(store.IsStale(0), Is.True);
            store.ApplySnapshot(Snapshot(), 10);
            Assert.That(store.IsStale(10.999), Is.False);
            Assert.That(store.IsStale(11), Is.True);
            Assert.Throws<ArgumentOutOfRangeException>(() => store.IsStale(9));
        }

        [TestCase(100.25, 200.5, 3.0)]
        [TestCase(-456.7, -123.4, -2.5)]
        [TestCase(0.0, 0.0, 0.0)]
        public void LocalCoordinatesRoundTripWithinCentimetre(double x, double y, double z)
        {
            var unity = MapCoordinateConverter.ToUnity(new MapPositionDto(x, y, z));
            Assert.That(unity.y, Is.EqualTo(z).Within(0.01));
            Assert.That(unity.z, Is.EqualTo(y).Within(0.01));
            var back = MapCoordinateConverter.FromUnity(unity);
            Assert.That(back.X, Is.EqualTo(x).Within(0.01));
            Assert.That(back.Y, Is.EqualTo(y).Within(0.01));
            Assert.That(back.Z, Is.EqualTo(z).Within(0.01));
        }

        [Test]
        public void NorthAndEastHeadingsMatchUnityYaw()
        {
            Assert.That(MapCoordinateConverter.ToUnityYaw(0), Is.Zero);
            Assert.That(MapCoordinateConverter.ToUnityYaw(Math.PI / 2), Is.EqualTo(90).Within(0.001));
            Assert.Throws<ArgumentOutOfRangeException>(() => MapCoordinateConverter.ToUnityYaw(double.NaN));
            Assert.Throws<ArgumentOutOfRangeException>(() => MapCoordinateConverter.ToUnity(new MapPositionDto(1e20 + 0.1, 0, 0)));
        }

        [Test]
        public void AckRequiresAppliedTickOrRejectionReason()
        {
            Assert.Throws<ArgumentException>(() => new CommandAckDto("run", "msg", true, null));
            Assert.Throws<ArgumentException>(() => new CommandAckDto("run", "msg", false, null));
            Assert.That(new CommandAckDto("run", "msg", false, null, "NOT_OWNER").Accepted, Is.False);
            Assert.That(new CommandAckDto("run", "msg", true, 0).AppliedTick, Is.Zero);
        }

        [TestCase(false)]
        [TestCase(true)]
        public void CompleteAssignedSnapshotMatchesStopsAndCanBeSharedAcrossRoles(bool mobile)
        {
            var role = mobile ? ClientRole.Mobile_Passenger : ClientRole.PC_Operator;
            var route = new RouteDto("route1", "map", new[] { Position, new MapPositionDto(5, 10, 0) });
            var request = new RequestDto("r1", "p1", ServiceType.PASSENGER, RequestStatus.ASSIGNED,
                0, "a", "b", "sa", "sb", Needs, 1, 0, "v1");
            var snapshot = new WorldSnapshotDto(3, "0.1.3.0", "map", "run", 1, 1, 0.05, role,
                mobile ? "p1" : null, new[] { Vehicle(route: "route1") }, new[] { request },
                new[] { new LandmarkDto("a", "A", "fixture", VerificationStatus.SYNTHETIC, new[] { "sa" }),
                    new LandmarkDto("b", "B", "fixture", VerificationStatus.SYNTHETIC, new[] { "sb" }) },
                new[] { new StopDto("sa", "a", null, Position, null, VerificationStatus.SYNTHETIC),
                    new StopDto("sb", "b", null, Position, true, VerificationStatus.SYNTHETIC) },
                new[] { route }, Array.Empty<ZoneDto>());
            var store = new WorldStateStore(role, mobile ? "p1" : null);
            store.ApplySnapshot(snapshot, 0);
            Assert.That(store.Requests["r1"].Status, Is.EqualTo(RequestStatus.ASSIGNED));
            Assert.That(store.Vehicles["v1"].RouteId, Is.EqualTo("route1"));
        }

        [Test]
        public void AssignedRequestCannotOmitVehicle()
        {
            Assert.Throws<ArgumentException>(() => new RequestDto("r", "p", ServiceType.PASSENGER,
                RequestStatus.ASSIGNED, 0, "a", "b", "sa", "sb", Needs, 1, 0));
        }

        [Test]
        public void RouteMapMismatchRejected()
        {
            var store = new WorldStateStore(ClientRole.PC_Operator);
            Assert.Throws<ArgumentException>(() => store.ApplySnapshot(Snapshot(
                routes: new[] { new RouteDto("route", "wrong-map", new[] { Position }) }), 0));
        }
    }
}
