using System.Collections;
using System.Collections.Generic;
using InhaExpress.Client.Domain;
using InhaExpress.Client.Presentation;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace InhaExpress.Client.Tests
{
    public sealed class VehicleRouteFollowerPlayModeTests
    {
        private readonly List<GameObject> actors = new List<GameObject>();

        [UnityTearDown]
        public IEnumerator RemoveActorsEvenAfterFailedAssertion()
        {
            foreach (var actor in actors)
                if (actor != null) Object.Destroy(actor);
            actors.Clear();
            yield return null;
        }

        [UnityTest]
        public IEnumerator HighServerSpeedStillHonorsLocalPreviewMaximum()
        {
            var actor = CreateActor(out var follower);
            var route = CreateRoute("local-speed-cap", 10.0, 5.0);
            Assert.That(follower.ApplyRoute(route, route.MapVersion, Time.realtimeSinceStartupAsDouble), Is.True);
            follower.SetMotionAuthorized(true);
            float until = Time.realtimeSinceStartup + 3f;
            while (Time.realtimeSinceStartup < until)
            {
                follower.ApplyRoute(route, route.MapVersion, Time.realtimeSinceStartupAsDouble);
                yield return new WaitForFixedUpdate();
                Assert.That(follower.SpeedMps, Is.LessThanOrEqualTo(1.0001f));
            }
            Assert.That(actor.transform.position.x, Is.GreaterThan(0.5f));
            Assert.That(follower.SpeedMps, Is.GreaterThan(0.9f));
        }

        [UnityTest]
        public IEnumerator FollowerMovesAlongSyntheticRouteAndStopsNearEndpoint()
        {
            var actor = CreateActor(out var follower);
            var route = CreateRoute("short-route", 0.6);
            Assert.That(follower.ApplyRoute(route, route.MapVersion, Time.realtimeSinceStartupAsDouble), Is.True);
            follower.SetMotionAuthorized(true);

            float timeoutAt = Time.realtimeSinceStartup + 4f;
            while (Time.realtimeSinceStartup < timeoutAt &&
                (follower.WaypointIndex < route.Polyline.Count || follower.SpeedMps >= 0.1f))
            {
                follower.ApplyRoute(route, route.MapVersion, Time.realtimeSinceStartupAsDouble);
                yield return new WaitForFixedUpdate();
            }

            Assert.That(follower.WaypointIndex, Is.EqualTo(route.Polyline.Count));
            Assert.That(actor.transform.position.x, Is.InRange(0.4f, 0.61f));
            Assert.That(follower.SpeedMps, Is.LessThan(0.1f));
            Object.Destroy(actor);
            yield return null;
        }

        [UnityTest]
        public IEnumerator FollowerHonorsServerSegmentSpeedLimit()
        {
            var actor = CreateActor(out var follower);
            var route = CreateRoute("slow-route", 2.0, 0.2);
            Assert.That(follower.ApplyRoute(route, route.MapVersion, Time.realtimeSinceStartupAsDouble), Is.True);
            follower.SetMotionAuthorized(true);

            float observeUntil = Time.realtimeSinceStartup + 0.5f;
            while (Time.realtimeSinceStartup < observeUntil)
            {
                follower.ApplyRoute(route, route.MapVersion, Time.realtimeSinceStartupAsDouble);
                yield return new WaitForFixedUpdate();
            }

            Assert.That(follower.SpeedMps, Is.LessThanOrEqualTo(0.2f));
            Assert.That(actor.transform.position.x, Is.LessThan(0.2f));
            var slowerRoute = CreateRoute("slow-route", 2.0, 0.1);
            Assert.That(follower.ApplyRoute(
                slowerRoute, slowerRoute.MapVersion, Time.realtimeSinceStartupAsDouble), Is.True);
            Assert.That(follower.MotionAuthorized, Is.True);
            float slowerUntil = Time.realtimeSinceStartup + 0.3f;
            while (Time.realtimeSinceStartup < slowerUntil)
            {
                follower.ApplyRoute(slowerRoute, slowerRoute.MapVersion, Time.realtimeSinceStartupAsDouble);
                yield return new WaitForFixedUpdate();
            }
            Assert.That(follower.SpeedMps, Is.LessThanOrEqualTo(0.1f));
            Object.Destroy(actor);
            yield return null;
        }

        [UnityTest]
        public IEnumerator NewFollowerAttachesToNearestRemainingRouteSegment()
        {
            var actor = CreateActor(out var follower);
            actor.GetComponent<Rigidbody>().position = new Vector3(1.3f, 0f, 0f);
            var route = new RouteDto("resume-route", "synthetic-route-test-v1", new[]
            {
                new MapPositionDto(0.0, 0.0, 0.0),
                new MapPositionDto(1.0, 0.0, 0.0),
                new MapPositionDto(2.0, 0.0, 0.0)
            }, segmentSpeedsMps: new[] { 0.2, 0.2 });

            Assert.That(follower.ApplyRoute(route, route.MapVersion, Time.realtimeSinceStartupAsDouble), Is.True);
            Assert.That(follower.WaypointIndex, Is.EqualTo(2));
            follower.SetMotionAuthorized(true);
            float moveUntil = Time.realtimeSinceStartup + 0.3f;
            while (Time.realtimeSinceStartup < moveUntil)
            {
                follower.ApplyRoute(route, route.MapVersion, Time.realtimeSinceStartupAsDouble);
                yield return new WaitForFixedUpdate();
            }

            Assert.That(actor.transform.position.x, Is.GreaterThan(1.3f));
            Object.Destroy(actor);
            yield return null;
        }

        [UnityTest]
        public IEnumerator NewFollowerRejectsRouteOutsideAttachTolerance()
        {
            var actor = CreateActor(out var follower);
            actor.GetComponent<Rigidbody>().position = new Vector3(10f, 0f, 0f);
            var route = CreateRoute("detached-route", 2.0, 0.2);

            Assert.That(follower.ApplyRoute(route, route.MapVersion, Time.realtimeSinceStartupAsDouble), Is.False);
            Assert.That(follower.RouteId, Is.Null);
            Assert.That(follower.MotionAuthorized, Is.False);
            Object.Destroy(actor);
            yield return null;
        }

        [UnityTest]
        public IEnumerator StaleRouteStopsFurtherRigidBodyMovement()
        {
            var actor = CreateActor(out var follower);
            var route = CreateRoute("stale-route", 5.0);
            Assert.That(follower.ApplyRoute(route, route.MapVersion, Time.realtimeSinceStartupAsDouble), Is.True);
            follower.SetMotionAuthorized(true);

            float refreshUntil = Time.realtimeSinceStartup + 0.2f;
            while (Time.realtimeSinceStartup < refreshUntil)
            {
                follower.ApplyRoute(route, route.MapVersion, Time.realtimeSinceStartupAsDouble);
                yield return new WaitForFixedUpdate();
            }

            yield return new WaitForSeconds(0.7f);
            float stoppedAt = actor.transform.position.x;
            yield return new WaitForSeconds(0.15f);

            Assert.That(actor.transform.position.x, Is.EqualTo(stoppedAt).Within(0.001f));
            Assert.That(follower.SpeedMps, Is.EqualTo(0f));
            Object.Destroy(actor);
            yield return null;
        }

        [UnityTest]
        public IEnumerator RevokedAuthorityStopsFreshSnapshotsAndResumesCurrentRoute()
        {
            var actor = CreateActor(out var follower);
            var route = CreateRoute("held-route", 5.0);
            Assert.That(follower.ApplyRoute(route, route.MapVersion, Time.realtimeSinceStartupAsDouble), Is.True);
            follower.SetMotionAuthorized(true);

            float driveUntil = Time.realtimeSinceStartup + 0.25f;
            while (Time.realtimeSinceStartup < driveUntil)
            {
                follower.ApplyRoute(route, route.MapVersion, Time.realtimeSinceStartupAsDouble);
                yield return new WaitForFixedUpdate();
            }

            float speedAtRevocation = follower.SpeedMps;
            Assert.That(speedAtRevocation, Is.GreaterThan(0.05f), "Brake a moving actor.");
            follower.SetMotionAuthorized(false);
            yield return new WaitForFixedUpdate();
            Vector3 heldPosition = actor.transform.position;
            int heldWaypointIndex = follower.WaypointIndex;
            float holdUntil = Time.realtimeSinceStartup + 0.3f;
            while (Time.realtimeSinceStartup < holdUntil)
            {
                // Model the server continuing to publish the same route while motion is withheld.
                follower.ApplyRoute(route, route.MapVersion, Time.realtimeSinceStartupAsDouble);
                yield return new WaitForFixedUpdate();
            }

            float brakingDistance = Vector3.Distance(actor.transform.position, heldPosition);
            float theoreticalStopDistance = speedAtRevocation * speedAtRevocation / (2f * 2f);
            Assert.That(brakingDistance, Is.LessThanOrEqualTo(theoreticalStopDistance + 0.01f));
            Assert.That(follower.SpeedMps, Is.EqualTo(0f));
            Assert.That(follower.WaypointIndex, Is.EqualTo(heldWaypointIndex));

            Vector3 stoppedPosition = actor.transform.position;
            yield return new WaitForSeconds(0.15f);
            Assert.That(Vector3.Distance(actor.transform.position, stoppedPosition), Is.LessThan(0.001f));

            follower.SetMotionAuthorized(true);
            float resumeUntil = Time.realtimeSinceStartup + 0.35f;
            while (Time.realtimeSinceStartup < resumeUntil)
            {
                follower.ApplyRoute(route, route.MapVersion, Time.realtimeSinceStartupAsDouble);
                yield return new WaitForFixedUpdate();
            }

            Assert.That(actor.transform.position.x, Is.GreaterThan(heldPosition.x + 0.01f));
            Assert.That(follower.RouteId, Is.EqualTo(route.Id));
            Assert.That(follower.WaypointIndex, Is.GreaterThanOrEqualTo(heldWaypointIndex));
            Object.Destroy(actor);
            yield return null;
        }

        [UnityTest]
        public IEnumerator TerminalWaypointPreservesPhysicalBrakingTravel()
        {
            var actor = CreateActor(out var follower);
            var route = CreateRoute("terminal-braking", 0.6);
            follower.ApplyRoute(route, route.MapVersion, Time.realtimeSinceStartupAsDouble);
            follower.SetMotionAuthorized(true);
            float deadline = Time.realtimeSinceStartup + 4f;
            while (follower.WaypointIndex < route.Polyline.Count && Time.realtimeSinceStartup < deadline)
            {
                follower.ApplyRoute(route, route.MapVersion, Time.realtimeSinceStartupAsDouble);
                yield return new WaitForFixedUpdate();
            }
            Assert.That(follower.WaypointIndex, Is.EqualTo(route.Polyline.Count));
            float remainingSpeed = follower.SpeedMps;
            float startX = actor.transform.position.x;
            Assert.That(remainingSpeed, Is.GreaterThan(0.1f), "Exercise residual momentum at terminal acceptance.");
            while (follower.SpeedMps > 0 && Time.realtimeSinceStartup < deadline)
            {
                follower.ApplyRoute(route, route.MapVersion, Time.realtimeSinceStartupAsDouble);
                yield return new WaitForFixedUpdate();
            }
            yield return new WaitForFixedUpdate();
            float travel = actor.transform.position.x - startX;
            Assert.That(travel, Is.GreaterThan(0.01f), "Decreasing a speed field without moving hides braking distance.");
            Assert.That(travel, Is.EqualTo(remainingSpeed * remainingSpeed / 4f).Within(0.02f));
            Assert.That(follower.SpeedMps, Is.Zero);
        }

        [UnityTest]
        public IEnumerator ReverseRouteBrakesAlongExistingDirectionBeforeRotating()
        {
            var actor = CreateActor(out var follower);
            var route = CreateRoute("forward-before-reverse", 5);
            follower.ApplyRoute(route, route.MapVersion, Time.realtimeSinceStartupAsDouble);
            follower.SetMotionAuthorized(true);
            float deadline = Time.realtimeSinceStartup + 2f;
            while (follower.SpeedMps < 0.5f && Time.realtimeSinceStartup < deadline)
            {
                follower.ApplyRoute(route, route.MapVersion, Time.realtimeSinceStartupAsDouble);
                yield return new WaitForFixedUpdate();
            }
            float startX = actor.transform.position.x;
            var rotation = actor.transform.rotation;
            var reverse = new RouteDto("reverse", route.MapVersion, new[] {
                new MapPositionDto(startX, 0, 0), new MapPositionDto(startX - 2, 0, 0)
            });
            Assert.That(follower.ApplyRoute(reverse, reverse.MapVersion, Time.realtimeSinceStartupAsDouble), Is.True);
            float speedBefore = follower.SpeedMps;
            yield return new WaitForFixedUpdate();
            yield return new WaitForFixedUpdate();
            Assert.That(actor.transform.position.x, Is.GreaterThan(startX), "Momentum cannot flip with the route.");
            Assert.That(Quaternion.Angle(rotation, actor.transform.rotation), Is.LessThan(0.01f));
            Assert.That(follower.SpeedMps, Is.InRange(speedBefore - 2f * Time.fixedDeltaTime * 3, speedBefore));
            deadline = Time.realtimeSinceStartup + 5f;
            while (actor.transform.position.x >= startX - 0.1f && Time.realtimeSinceStartup < deadline)
            {
                follower.ApplyRoute(reverse, reverse.MapVersion, Time.realtimeSinceStartupAsDouble);
                yield return new WaitForFixedUpdate();
            }
            Assert.That(actor.transform.position.x, Is.LessThan(startX - 0.1f), "Resume after stopping and turning.");
        }

        [UnityTest]
        public IEnumerator PythonCommandExpiryBrakesWithPhysicalTravelAndDisablesFollower()
        {
            var actor = CreateActor(out var follower);
            var actuator = actor.AddComponent<VehicleCommandActuator>();
            actuator.Configure("V01", "synthetic-control-test", "session");
            Assert.That(follower.enabled, Is.False);
            long sequence = 0;
            double until = Time.realtimeSinceStartupAsDouble + 1.2;
            while (Time.realtimeSinceStartupAsDouble < until)
            {
                double now = Time.realtimeSinceStartupAsDouble;
                Assert.That(actuator.ApplyControl(Control(sequence, sequence), "route", sequence, now, now), Is.True);
                sequence++;
                yield return new WaitForFixedUpdate();
            }
            Assert.That(actuator.SpeedMps, Is.GreaterThan(0.4f));
            while (actuator.CommandFresh) yield return new WaitForFixedUpdate();
            float speed = actuator.SpeedMps;
            float position = actor.transform.position.x;
            yield return new WaitForSeconds(0.6f);
            Assert.That(actuator.SpeedMps, Is.Zero);
            Assert.That(actor.transform.position.x - position, Is.GreaterThan(0.01f));
            Assert.That(actor.transform.position.x - position, Is.EqualTo(speed * speed / 4f).Within(0.025f));
            double received = Time.realtimeSinceStartupAsDouble;
            Assert.That(actuator.ApplyControl(Control(sequence - 1, sequence - 1), "route", sequence,
                received, received), Is.False, "Replay cannot renew the command lease.");
            Assert.That(actuator.CommandFresh, Is.False);
        }

        [UnityTest]
        public IEnumerator PythonCommandRejectsWrongContextAndOldOrFuturePose()
        {
            var actor = CreateActor(out _);
            var actuator = actor.AddComponent<VehicleCommandActuator>();
            actuator.Configure("V01", "synthetic-control-test", "session");
            double now = Time.realtimeSinceStartupAsDouble;
            Assert.That(actuator.ApplyControl(Control(0, 10), "route", 10, now, now), Is.True);
            Assert.That(actuator.ApplyControl(Control(1, 10, "other"), "route", 10, now, now), Is.False);
            Assert.That(actuator.ApplyControl(Control(2, 11), "route", 10, now, now), Is.False);
            Assert.That(actuator.ApplyControl(Control(3, 7), "route", 10, now, now), Is.False);
            Assert.That(actuator.ApplyControl(Control(4, 10), "different-route", 10, now, now), Is.False);
            Assert.That(actuator.ApplyControl(Control(5, 10), "route", 10, now, now + 0.21), Is.False);
            Assert.That(actuator.CommandFresh, Is.False);
            yield return new WaitForFixedUpdate();
            Assert.That(actuator.SpeedMps, Is.Zero);
        }

        private static VehicleControlDto Control(long sequence, long pose, string session = "session") =>
            new VehicleControlDto("V01", "synthetic-control-test", session, "route", sequence,
                pose, 0.2, 1, 0, "ROUTE_CONTROL");

        [UnityTest]
        public IEnumerator RecreatedActuatorCannotReplayAcceptedCommands()
        {
            var history = new ControlSequenceGuard();
            var first = CreateActor(out _).AddComponent<VehicleCommandActuator>();
            first.Configure("V01", "synthetic-control-test", "session", history, "run-a");
            double now = Time.realtimeSinceStartupAsDouble;
            Assert.That(first.ApplyControl(Control(10, 10), "route", 10, now, now), Is.True);
            Object.Destroy(first.gameObject);
            yield return null;
            var replacement = CreateActor(out _).AddComponent<VehicleCommandActuator>();
            replacement.Configure("V01", "synthetic-control-test", "session", history, "run-a");
            now = Time.realtimeSinceStartupAsDouble;
            Assert.That(replacement.ApplyControl(Control(10, 10), "route", 10, now, now), Is.False);
            Assert.That(replacement.ApplyControl(Control(9, 10), "route", 10, now, now), Is.False);
            Assert.That(replacement.ApplyControl(Control(11, 10), "route", 10, now, now), Is.True);
            var restarted = CreateActor(out _).AddComponent<VehicleCommandActuator>();
            restarted.Configure("V01", "synthetic-control-test", "session", history, "run-b");
            Assert.That(restarted.ApplyControl(Control(0, 10), "route", 10, now, now), Is.True);
        }

        private GameObject CreateActor(out VehicleRouteFollower follower)
        {
            var actor = new GameObject("Route follower PlayMode test actor");
            actors.Add(actor);
            // These route tests use eastbound geometry; alignment is not the condition
            // under test. Set the initial pose before creating the Physics body.
            actor.transform.rotation = Quaternion.Euler(0f, 90f, 0f);
            var body = actor.AddComponent<Rigidbody>();
            body.isKinematic = true;
            body.useGravity = false;
            follower = actor.AddComponent<VehicleRouteFollower>();
            return actor;
        }

        private static RouteDto CreateRoute(string id, double endX, double? speedMps = null) =>
            new RouteDto(id, "synthetic-route-test-v1", new[]
            {
                new MapPositionDto(0.0, 0.0, 0.0),
                new MapPositionDto(endX, 0.0, 0.0)
            }, segmentSpeedsMps: speedMps.HasValue ? new[] { speedMps.Value } : null);
    }
}
