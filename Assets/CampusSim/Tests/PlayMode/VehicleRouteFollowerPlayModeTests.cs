using System.Collections;
using InhaExpress.Client.Domain;
using InhaExpress.Client.Presentation;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace InhaExpress.Client.Tests
{
    public sealed class VehicleRouteFollowerPlayModeTests
    {
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
            actor.transform.position = new Vector3(1.3f, 0f, 0f);
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
            actor.transform.position = new Vector3(10f, 0f, 0f);
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

        private static GameObject CreateActor(out VehicleRouteFollower follower)
        {
            var actor = new GameObject("Route follower PlayMode test actor");
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
