using System;
using InhaExpress.Client.Domain;
using UnityEngine;

namespace InhaExpress.Client.Presentation
{
    /// <summary>
    /// Low-speed, waypoint-to-waypoint kinematic follower for isolated synthetic-map previews.
    /// It is not a safety controller and must not be enabled for real campus maps.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Rigidbody))]
    public sealed class VehicleRouteFollower : MonoBehaviour
    {
        private const string SyntheticMapPrefix = "synthetic-";
        private const float RouteGeometryToleranceM = 0.001f;
        private const float MaxRouteAttachDistanceM = 2f;

        [SerializeField, Min(0.1f)] private float maxSpeedMps = 1.0f;
        [SerializeField, Min(0.1f)] private float accelerationMps2 = 0.5f;
        [SerializeField, Min(0.1f)] private float brakingMps2 = 2.0f;
        [SerializeField, Min(0.01f)] private float waypointRadiusM = 0.15f;
        [SerializeField, Min(0.1f)] private float routeTimeoutS = 0.5f;
        [SerializeField, Min(1f)] private float turnRateDegS = 90f;
        [SerializeField, Range(0f, 45f)] private float alignmentToleranceDeg = 8f;

        private Rigidbody body;
        private Vector3[] routePoints = Array.Empty<Vector3>();
        private float[] segmentSpeedsMps = Array.Empty<float>();
        private int waypointIndex;
        private string mapVersion;
        private string routeId;
        private double lastRouteReceivedAtS;
        private float speedMps;
        private Vector3 lastTravelDirection;
        private bool motionAuthorized;

        public string RouteId => routeId;
        public float SpeedMps => speedMps;
        public int WaypointIndex => waypointIndex;
        public int RoutePointCount => routePoints.Length;
        public bool MotionAuthorized => motionAuthorized;

        public Vector3 GetRoutePoint(int index)
        {
            if (index < 0 || index >= routePoints.Length) throw new ArgumentOutOfRangeException(nameof(index));
            return routePoints[index];
        }

        private void Awake() => body = GetComponent<Rigidbody>();

        private void OnValidate()
        {
            maxSpeedMps = Mathf.Max(0.1f, maxSpeedMps);
            accelerationMps2 = Mathf.Max(0.1f, accelerationMps2);
            brakingMps2 = Mathf.Max(0.1f, brakingMps2);
            waypointRadiusM = Mathf.Max(0.01f, waypointRadiusM);
            routeTimeoutS = Mathf.Max(0.1f, routeTimeoutS);
            turnRateDegS = Mathf.Max(1f, turnRateDegS);
            alignmentToleranceDeg = Mathf.Clamp(alignmentToleranceDeg, 0f, 45f);
        }

        /// <summary>Accept only complete route updates whose version matches a synthetic map.</summary>
        public bool ApplyRoute(RouteDto route, string snapshotMapVersion, double receivedAtS)
        {
            // Authoring/EditMode callers can apply a route before Awake has run.
            if (body == null) body = GetComponent<Rigidbody>();
            if (body == null) return RejectRoute();
            if (route == null || string.IsNullOrWhiteSpace(snapshotMapVersion) ||
                !snapshotMapVersion.StartsWith(SyntheticMapPrefix, StringComparison.Ordinal) ||
                !string.Equals(route.MapVersion, snapshotMapVersion, StringComparison.Ordinal) ||
                route.Polyline.Count < 2 || double.IsNaN(receivedAtS) || double.IsInfinity(receivedAtS) ||
                receivedAtS < 0.0)
                return RejectRoute();

            var converted = new Vector3[route.Polyline.Count];
            try
            {
                for (int i = 0; i < route.Polyline.Count; i++)
                    converted[i] = MapCoordinateConverter.ToUnity(route.Polyline[i]);
            }
            catch (ArgumentOutOfRangeException)
            {
                return RejectRoute();
            }

            if (routeId == route.Id && mapVersion == snapshotMapVersion)
            {
                if (!HasSameGeometry(converted))
                {
                    // Route IDs identify immutable geometry. Reuse with changed points is a
                    // contract violation; revoke motion instead of silently following old data.
                    return RejectRoute();
                }

                segmentSpeedsMps = CopySpeeds(route.SegmentSpeedsMps);
                lastRouteReceivedAtS = receivedAtS;
                return true;
            }

            if (!TryFindNextWaypointIndex(converted, body.position, out int nextWaypointIndex))
                return RejectRoute();

            routePoints = converted;
            segmentSpeedsMps = CopySpeeds(route.SegmentSpeedsMps);
            waypointIndex = nextWaypointIndex;
            routeId = route.Id;
            mapVersion = snapshotMapVersion;
            lastRouteReceivedAtS = receivedAtS;
            return true;
        }

        private bool RejectRoute()
        {
            ClearRoute();
            return false;
        }

        private bool HasSameGeometry(Vector3[] candidate)
        {
            if (candidate == null || candidate.Length != routePoints.Length) return false;
            float toleranceSquared = RouteGeometryToleranceM * RouteGeometryToleranceM;
            for (int index = 0; index < candidate.Length; index++)
                if ((candidate[index] - routePoints[index]).sqrMagnitude > toleranceSquared)
                    return false;
            return true;
        }

        private static bool TryFindNextWaypointIndex(
            Vector3[] points, Vector3 position, out int nextWaypointIndex)
        {
            nextWaypointIndex = 0;
            if (points == null || points.Length < 2) return false;

            float nearestDistanceSquared = float.PositiveInfinity;
            int nearestSegmentIndex = 0;
            for (int index = 0; index < points.Length - 1; index++)
            {
                Vector3 start = points[index];
                Vector3 end = points[index + 1];
                Vector3 segment = end - start;
                segment.y = 0f;
                Vector3 offset = position - start;
                offset.y = 0f;
                float lengthSquared = segment.sqrMagnitude;
                float projection = lengthSquared <= 1e-8f
                    ? 0f
                    : Mathf.Clamp01(Vector3.Dot(offset, segment) / lengthSquared);
                Vector3 closest = start + segment * projection;
                Vector3 difference = position - closest;
                difference.y = 0f;
                float distanceSquared = difference.sqrMagnitude;
                if (distanceSquared < nearestDistanceSquared)
                {
                    nearestDistanceSquared = distanceSquared;
                    nearestSegmentIndex = index;
                }
            }

            if (nearestDistanceSquared > MaxRouteAttachDistanceM * MaxRouteAttachDistanceM)
                return false;
            nextWaypointIndex = nearestSegmentIndex + 1;
            return true;
        }

        private static float[] CopySpeeds(System.Collections.ObjectModel.ReadOnlyCollection<double> speeds)
        {
            if (speeds == null || speeds.Count == 0) return Array.Empty<float>();
            var copy = new float[speeds.Count];
            for (int index = 0; index < speeds.Count; index++)
                copy[index] = (float)speeds[index];
            return copy;
        }

        public void ClearRoute()
        {
            routePoints = Array.Empty<Vector3>();
            segmentSpeedsMps = Array.Empty<float>();
            waypointIndex = 0;
            routeId = null;
            mapVersion = null;
            motionAuthorized = false;
        }

        /// <summary>Preserve route progress and allow bounded deceleration when authority is withheld.</summary>
        public void SetMotionAuthorized(bool authorized)
        {
            motionAuthorized = authorized;
        }

        private void FixedUpdate()
        {
            if (body == null) return;
            float deltaS = Time.fixedDeltaTime;
            if (deltaS <= 0f) return;

            bool routeFresh = routeId != null &&
                Time.realtimeSinceStartupAsDouble - lastRouteReceivedAtS <= routeTimeoutS;
            if (!routeFresh || !motionAuthorized)
            {
                ApplyKinematicBraking(deltaS);
                return;
            }

            AdvancePassedWaypoints();
            if (waypointIndex >= routePoints.Length)
            {
                ApplyKinematicBraking(deltaS);
                return;
            }

            Vector3 offset = routePoints[waypointIndex] - body.position;
            offset.y = 0f;
            float distance = offset.magnitude;
            if (distance <= waypointRadiusM)
            {
                waypointIndex++;
                ApplyKinematicBraking(deltaS);
                return;
            }

            Vector3 direction = offset / distance;
            Quaternion targetRotation = Quaternion.LookRotation(direction, Vector3.up);
            float angle = Quaternion.Angle(body.rotation, targetRotation);
            if (angle > alignmentToleranceDeg && speedMps > 0f)
            {
                // A newly selected corner/reverse route cannot rotate the velocity
                // vector instantaneously. Stop along the existing travel direction
                // before rotating in place under this synthetic preview controller.
                ApplyKinematicBraking(deltaS);
                return;
            }
            Quaternion nextRotation = Quaternion.RotateTowards(body.rotation, targetRotation, turnRateDegS * deltaS);
            body.MoveRotation(nextRotation);

            int segmentIndex = Mathf.Max(0, waypointIndex - 1);
            float routeSpeedMps = segmentIndex < segmentSpeedsMps.Length
                ? segmentSpeedsMps[segmentIndex]
                : maxSpeedMps;
            float desiredSpeed = Mathf.Min(Mathf.Min(maxSpeedMps, routeSpeedMps),
                Mathf.Sqrt(2f * brakingMps2 * distance));
            if (angle > alignmentToleranceDeg) desiredSpeed = 0f;
            float rate = desiredSpeed < speedMps ? brakingMps2 : accelerationMps2;
            speedMps = Mathf.MoveTowards(speedMps, desiredSpeed, rate * deltaS);

            float travel = Mathf.Min(distance, speedMps * deltaS);
            body.MovePosition(body.position + direction * travel);
            if (travel > 0f)
            {
                lastTravelDirection = direction;
                speedMps = travel / deltaS;
            }
        }

        private void ApplyKinematicBraking(float deltaS)
        {
            float previousSpeed = speedMps;
            speedMps = Mathf.MoveTowards(speedMps, 0f, brakingMps2 * deltaS);
            float travel = (previousSpeed + speedMps) * 0.5f * deltaS;
            if (travel <= 0f) return;

            Vector3 direction = lastTravelDirection.sqrMagnitude > 1e-6f
                ? lastTravelDirection
                : body.rotation * Vector3.forward;
            direction.y = 0f;
            if (direction.sqrMagnitude <= 1e-6f) return;
            body.MovePosition(body.position + direction.normalized * travel);
        }

        private void AdvancePassedWaypoints()
        {
            while (waypointIndex < routePoints.Length)
            {
                Vector3 offset = routePoints[waypointIndex] - body.position;
                offset.y = 0f;
                if (offset.sqrMagnitude > waypointRadiusM * waypointRadiusM) break;
                waypointIndex++;
            }
        }
    }
}
