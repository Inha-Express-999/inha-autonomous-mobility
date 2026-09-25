using System;
using InhaExpress.Client.Domain;
using InhaExpress.Client.Networking;
using InhaExpress.Client.Presentation;
using InhaExpress.Client.Mobile;
using InhaExpress.Client.PC;
using NUnit.Framework;

namespace InhaExpress.Client.Tests
{
    public sealed class FixtureClientTests
    {
        private const string Version = "0.1.5.0";

        [Test]
        public void RoleProjectionAtSameTickPreservesOwnVehicleAndRequest()
        {
            var pc = Snapshot(500);
            var mobile = FixtureScenario.Create(ClientRole.Mobile_Passenger, FixtureScenario.PassengerId, Version, "run", 500, 500);
            Assert.That(mobile.RunId, Is.EqualTo(pc.RunId));
            Assert.That(mobile.SimulationTick, Is.EqualTo(pc.SimulationTick));
            Assert.That(mobile.Requests[0].Status, Is.EqualTo(pc.Requests[0].Status));
            Assert.That(mobile.Vehicles[0].Position.Y, Is.EqualTo(pc.Vehicles[0].Position.Y));
            Assert.That(mobile.Vehicles[0].Reason, Is.EqualTo(pc.Vehicles[0].Reason));
        }

        [TestCase(ClientRole.PC_Operator)]
        [TestCase(ClientRole.Mobile_Passenger)]
        public void EveryRecordedTickIsAValidCompleteSnapshot(ClientRole role)
        {
            var subscriber = role == ClientRole.Mobile_Passenger ? FixtureScenario.PassengerId : null;
            var store = new WorldStateStore(role, subscriber);
            for (int tick = 0; tick <= 900; tick++)
            {
                var snapshot = FixtureScenario.Create(role, subscriber, Version, "run", tick, tick);
                Assert.That(store.ApplySnapshot(snapshot, tick * 0.05), Is.EqualTo(SnapshotApplyResult.Applied));
                if (role == ClientRole.Mobile_Passenger)
                {
                    Assert.That(snapshot.Zones, Is.Empty);
                    Assert.That(snapshot.Requests.Count, Is.EqualTo(1));
                    Assert.That(snapshot.Requests[0].OwnerId, Is.EqualTo(subscriber));
                    Assert.That(snapshot.Vehicles.Count, Is.LessThanOrEqualTo(1));
                    Assert.That(snapshot.Stops.Count, Is.LessThanOrEqualTo(2));
                    foreach (var landmark in snapshot.Landmarks)
                        Assert.That(landmark.Verification, Is.EqualTo(VerificationStatus.SYNTHETIC));
                }
            }
        }

        [TestCase(0, RequestStatus.CREATED)]
        [TestCase(20, RequestStatus.VALIDATED)]
        [TestCase(40, RequestStatus.QUEUED)]
        [TestCase(60, RequestStatus.ASSIGNED)]
        [TestCase(200, RequestStatus.PICKUP_SERVICE)]
        [TestCase(260, RequestStatus.IN_TRANSIT)]
        [TestCase(800, RequestStatus.DROPOFF_SERVICE)]
        [TestCase(880, RequestStatus.COMPLETED)]
        public void TimelineHasExplicitServiceStages(int tick, RequestStatus expected)
        {
            Assert.That(Snapshot(tick).Requests[0].Status, Is.EqualTo(expected));
        }

        [Test]
        public void AnotherSubscriberGetsNoPrivateState()
        {
            var snapshot = FixtureScenario.Create(ClientRole.Mobile_Passenger, "unrelated", Version, "run", 1, 500);
            new WorldStateStore(ClientRole.Mobile_Passenger, "unrelated").ApplySnapshot(snapshot, 0);
            Assert.That(snapshot.Requests, Is.Empty);
            Assert.That(snapshot.Vehicles, Is.Empty);
            Assert.That(snapshot.Routes, Is.Empty);
            Assert.That(snapshot.Stops, Is.Empty);
            Assert.That(snapshot.Zones, Is.Empty);
            Assert.That(snapshot.Landmarks.Count, Is.EqualTo(6));
        }

        [Test]
        public void PauseKeepsHeartbeatFreshAtTheSameTick()
        {
            using (var source = Source())
            {
                var store = Connect(source);
                source.Pump(4);
                source.SetPaused(true, 4);
                long tick = store.Current.SimulationTick;
                long seq = store.Current.Sequence;
                source.Pump(10);
                Assert.That(store.Current.SimulationTick, Is.EqualTo(tick));
                Assert.That(store.Current.Sequence, Is.GreaterThan(seq));
                Assert.That(store.IsStale(10.9), Is.False);
                source.SetPaused(false, 10);
                source.Pump(11);
                Assert.That(store.Current.SimulationTick, Is.EqualTo(tick + 20));
            }
        }

        [Test]
        public void DeliveryInterruptionFreezesClientButNotReplayAndRestoresFullSnapshot()
        {
            using (var source = Source())
            {
                var store = Connect(source);
                source.SetDeliveryEnabled(false, 5);
                var before = store.Current;
                source.Pump(25);
                Assert.That(store.Current, Is.SameAs(before));
                Assert.That(store.IsStale(5.999), Is.False);
                Assert.That(store.IsStale(6), Is.True);
                Assert.That(source.ConnectionState, Is.EqualTo(ConnectionState.Disconnected));
                source.SetDeliveryEnabled(true, 25);
                Assert.That(store.Current.SimulationTick, Is.EqualTo(500));
                Assert.That(store.Current.Vehicles[0].MotionState, Is.EqualTo(VehicleMotionState.YIELDING));
                Assert.That(store.IsStale(25), Is.False);
            }
        }

        [Test]
        public void RestartCreatesNewRunAndRetiresOldRun()
        {
            using (var source = Source())
            {
                var store = Connect(source);
                source.Pump(25);
                var before = store.Current;
                source.Restart(26);
                Assert.That(store.Current.RunId, Is.Not.EqualTo(before.RunId));
                Assert.That(store.Current.SimulationTick, Is.Zero);
                Assert.That(store.Current.Requests[0].Status, Is.EqualTo(RequestStatus.CREATED));
                Assert.That(store.ApplySnapshot(before, 27), Is.EqualTo(SnapshotApplyResult.IgnoredRetiredRun));
            }
        }

        [Test]
        public void CoarseAndFinePumpsReachIdenticalRecordedStateWithoutCatchupFlood()
        {
            using (var coarse = Source())
            using (var fine = Source())
            {
                var a = Connect(coarse);
                var b = Connect(fine);
                int received = 0;
                coarse.SnapshotReceived += (_, __) => received++;
                coarse.Pump(25);
                for (int i = 1; i <= 500; i++) fine.Pump(i * 0.05);
                Assert.That(received, Is.EqualTo(1));
                Assert.That(a.Current.SimulationTick, Is.EqualTo(b.Current.SimulationTick));
                Assert.That(a.Current.Vehicles[0].Position.Y, Is.EqualTo(b.Current.Vehicles[0].Position.Y));
                Assert.That(a.Current.Requests[0].Status, Is.EqualTo(b.Current.Requests[0].Status));
            }
        }

        [Test]
        public void CompletedReplayContinuesHeartbeats()
        {
            using (var source = Source())
            {
                var store = Connect(source);
                source.Pump(45);
                long seq = store.Current.Sequence;
                source.Pump(100);
                Assert.That(store.Current.SimulationTick, Is.EqualTo(900));
                Assert.That(store.Current.Sequence, Is.GreaterThan(seq));
                Assert.That(store.IsStale(100), Is.False);
            }
        }

        [Test]
        public void InvalidClocksAndDisposedUseAreRejected()
        {
            var source = Source();
            source.Start(2);
            Assert.Throws<ArgumentOutOfRangeException>(() => source.Pump(1));
            Assert.Throws<ArgumentOutOfRangeException>(() => source.Pump(double.NaN));
            Assert.Throws<ArgumentOutOfRangeException>(() => source.Pump(double.PositiveInfinity));
            source.Dispose();
            source.Dispose();
            Assert.Throws<ObjectDisposedException>(() => source.Pump(3));
        }

        [Test]
        public void PassengerGuidanceRequiresMatchingStateAndReason()
        {
            StringAssert.Contains("혼잡", PassengerStatusPresenter.Describe(Snapshot(420)));
            StringAssert.Contains("보행자", PassengerStatusPresenter.Describe(Snapshot(500)));
            StringAssert.DoesNotContain("보행자", PassengerStatusPresenter.Describe(Snapshot(550)));
            StringAssert.Contains("센서 관측 지연", FixtureUiText.Reason(ReasonCode.SENSOR_DATA_STALE));
            StringAssert.Contains("장애물 정지", FixtureUiText.Reason(ReasonCode.OBSTACLE_STOP));
            StringAssert.Contains("완료 전", PassengerStatusPresenter.StatusText(RequestStatus.DROPOFF_SERVICE));
            StringAssert.Contains("최종 보행", PassengerStatusPresenter.StatusText(RequestStatus.COMPLETED));
        }

        [Test]
        public void OperatorTextKeepsUnknownObservationsSeparateFromPrior()
        {
            StringAssert.Contains("Observed unknown | EMA unknown | prior", OperatorStatusPresenter.Describe(Snapshot(420)));
        }

        private static WorldSnapshotDto Snapshot(long tick) => FixtureScenario.Create(
            ClientRole.PC_Operator, null, Version, "run", tick, tick);
        private static FixtureClientDataSource Source() => new FixtureClientDataSource(
            ClientRole.PC_Operator, Version, sessionId: "test");
        private static WorldStateStore Connect(FixtureClientDataSource source)
        {
            var store = new WorldStateStore(ClientRole.PC_Operator);
            source.SnapshotReceived += (snapshot, now) => store.ApplySnapshot(snapshot, now);
            source.Start(0);
            return store;
        }
    }
}
