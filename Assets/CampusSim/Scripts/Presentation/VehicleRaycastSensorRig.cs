using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using InhaExpress.Client.Domain;
using InhaExpress.Client.Networking;
using UnityEngine;

namespace InhaExpress.Client.Presentation
{
    /// <summary>
    /// Low-resolution 2D raycast lidar/radar abstraction for the synthetic preview.
    /// it does not infer hidden actors or issue a safety/control decision.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(VehicleEgoLocalizationReporter))]
    [DefaultExecutionOrder(100)]
    public sealed class VehicleRaycastSensorRig : MonoBehaviour
    {
        private const int MaximumDetections = 64;
        private const double ScanIntervalS = 0.1;
        private sealed class ObservedIdentity
        {
            public readonly string Id = Guid.NewGuid().ToString("N");
        }
        // Shared across rigs, weakly owned by actually hit Physics objects. No scene enumeration.
        private static readonly ConditionalWeakTable<UnityEngine.Object, ObservedIdentity> identities =
            new ConditionalWeakTable<UnityEngine.Object, ObservedIdentity>();

        [SerializeField] private string sensorId = "front-lidar";
        [SerializeField] private SensorType sensorType = SensorType.LIDAR_2D;
        [SerializeField] private Transform sensorOrigin;
        [SerializeField, Range(8, MaximumDetections)] private int beamCount = 64;
        [SerializeField, Min(0.1f)] private float maxRangeM = 30f;
        [SerializeField, Range(1f, 360f)] private float fieldOfViewDegrees = 360f;
        [SerializeField] private LayerMask obstacleLayers = ~0;
        [SerializeField] private LayerMask pedestrianLayers;
        [SerializeField] private LayerMask vehicleLayers;

        private readonly RaycastHit[] hitBuffer = new RaycastHit[64];
        private readonly List<SensorDetectionDto> detections = new List<SensorDetectionDto>(MaximumDetections);
        private readonly Dictionary<int, SensorDetectionDto> radarHits = new Dictionary<int, SensorDetectionDto>();
        private readonly Dictionary<int, double> previousRadarRanges = new Dictionary<int, double>();
        private double previousRadarScanAtS = double.NaN;
        private VehicleEgoLocalizationReporter localizationReporter;
        private long observedTick = -1;
        private double nextScanAtS;

        public string SensorId => sensorId;
        public long LatestObservedTick => observedTick;

        private void Awake()
        {
            localizationReporter = GetComponent<VehicleEgoLocalizationReporter>();
            if (sensorOrigin == null) sensorOrigin = transform;
            pedestrianLayers |= LayerMask.GetMask("Pedestrian");
            vehicleLayers |= LayerMask.GetMask("Vehicle");
        }

        private void OnValidate()
        {
            beamCount = Mathf.Clamp(beamCount, 8, MaximumDetections);
            maxRangeM = Mathf.Max(0.1f, maxRangeM);
            fieldOfViewDegrees = Mathf.Clamp(fieldOfViewDegrees, 1f, 360f);
            if (string.IsNullOrWhiteSpace(sensorId)) sensorId = "front-lidar";
        }

        private void FixedUpdate()
        {
            if (localizationReporter == null || localizationReporter.LatestObservedTick < 0 ||
                string.IsNullOrWhiteSpace(localizationReporter.VehicleId)) return;

            double nowS = Time.realtimeSinceStartupAsDouble;
            if (nowS < nextScanAtS) return;
            nextScanAtS = nowS + ScanIntervalS;

            // Pose and sensor observations must use the same explicitly bound session.
            var runtime = localizationReporter.Runtime;
            if (runtime == null || runtime.Role != ClientRole.PC_Operator ||
                runtime.Sensors == null || runtime.Store?.Current == null) return;

            bool scanComplete = Scan(nowS);
            var origin = sensorOrigin != null ? sensorOrigin : transform;
            long nextTick = runtime.NextSensorTick(localizationReporter.VehicleId, sensorId);
            var observation = new SensorObservationDto(
                localizationReporter.VehicleId,
                sensorId,
                sensorType,
                nextTick,
                localizationReporter.LatestObservedTick,
                runtime.Store.Current.MapVersion,
                scanComplete,
                detections, Time.fixedTimeAsDouble, MapCoordinateConverter.FromUnity(origin.position),
                origin.eulerAngles.y * Mathf.Deg2Rad);
            runtime.Sensors.SendSensorObservation(observation);
            observedTick = nextTick;
        }

        private bool Scan(double nowS)
        {
            detections.Clear();
            radarHits.Clear();
            bool complete = true;
            var origin = sensorOrigin != null ? sensorOrigin : transform;
            // This contract is planar: never silently interpret a tilted sensor as yaw-only.
            if ((origin.up - Vector3.up).sqrMagnitude > 0.0001f)
            {
                previousRadarRanges.Clear();
                previousRadarScanAtS = double.NaN;
                return false;
            }
            int layerMask = obstacleLayers.value;
            // The rig is on the actor root. A scene-level grouping parent may
            // also contain other vehicles/pedestrians and is not part of ego.
            Transform vehicleRoot = transform;
            for (int beam = 0; beam < beamCount; beam++)
            {
                float bearing = BeamBearing(beam);
                Vector3 direction = origin.forward * Mathf.Cos(bearing) - origin.right * Mathf.Sin(bearing);
                int hitCount = Physics.RaycastNonAlloc(origin.position, direction, hitBuffer,
                    maxRangeM, layerMask, QueryTriggerInteraction.Ignore);
                // Unity does not guarantee that a full NonAlloc buffer contains the nearest
                // hits. Preserve the observation but mark it invalid rather than claiming a
                // potentially truncated scan is complete.
                if (hitCount == hitBuffer.Length)
                {
                    complete = false;
                    continue;
                }
                if (!TryNearestExternalHit(hitCount, vehicleRoot, out var hit)) continue;

                Vector3 sensorLocal = Quaternion.Inverse(origin.rotation) * (hit.point - origin.position);
                double forward = sensorLocal.z;
                double left = -sensorLocal.x;
                double horizontalRange = Math.Sqrt(forward * forward + left * left);
                if (horizontalRange <= 0.01 || horizontalRange > maxRangeM) continue;

                var detection = new SensorDetectionDto(
                    horizontalRange,
                    Math.Atan2(left, forward),
                    new MapPositionDto(forward, left, sensorLocal.y),
                    Classify(hit.collider.gameObject.layer), entityId: IdentityFor(hit.collider));
                if (sensorType == SensorType.RADAR)
                {
                    // Identity comes only from a collider that was actually hit.
                    // Keep one nearest surface return per observed collider.
                    int id = hit.collider.GetInstanceID();
                    if (!radarHits.TryGetValue(id, out var nearest) || detection.RangeM < nearest.RangeM)
                        radarHits[id] = detection;
                }
                else detections.Add(detection);
            }
            if (sensorType == SensorType.RADAR)
            {
                double elapsed = nowS - previousRadarScanAtS;
                bool canEstimate = complete && elapsed >= 0.05 && elapsed <= 0.3;
                foreach (var entry in radarHits)
                {
                    var hit = entry.Value;
                    double? rate = canEstimate && previousRadarRanges.TryGetValue(entry.Key, out double previous)
                        ? (hit.RangeM - previous) / elapsed : (double?)null;
                    detections.Add(new SensorDetectionDto(hit.RangeM, hit.BearingRad,
                        hit.LocalPositionM, hit.EntityClass, rate, hit.EntityId));
                }
            }
            previousRadarRanges.Clear();
            if (complete && sensorType == SensorType.RADAR)
                foreach (var entry in radarHits) previousRadarRanges[entry.Key] = entry.Value.RangeM;
            previousRadarScanAtS = complete ? nowS : double.NaN;
            return complete;
        }

        private void OnDisable()
        {
            previousRadarRanges.Clear();
            previousRadarScanAtS = double.NaN;
        }

        private static string IdentityFor(Collider collider)
        {
            UnityEngine.Object owner = collider.attachedRigidbody != null
                ? (UnityEngine.Object)collider.attachedRigidbody : collider;
            return identities.GetValue(owner, _ => new ObservedIdentity()).Id;
        }

        private float BeamBearing(int beam)
        {
            float fov = fieldOfViewDegrees * Mathf.Deg2Rad;
            // A full revolution excludes the repeated last ray; a partial FOV includes both edges.
            float denominator = fieldOfViewDegrees >= 360f ? beamCount : beamCount - 1;
            return -0.5f * fov + fov * beam / denominator;
        }

        private bool TryNearestExternalHit(int hitCount, Transform vehicleRoot, out RaycastHit nearest)
        {
            nearest = default;
            float nearestDistance = float.PositiveInfinity;
            bool found = false;
            for (int index = 0; index < hitCount; index++)
            {
                var hit = hitBuffer[index];
                if (hit.collider == null || hit.collider.transform.IsChildOf(vehicleRoot) ||
                    hit.distance >= nearestDistance) continue;
                nearest = hit;
                nearestDistance = hit.distance;
                found = true;
            }
            return found;
        }

        private SensorEntityClass Classify(int layer)
        {
            int layerBit = 1 << layer;
            if ((pedestrianLayers.value & layerBit) != 0) return SensorEntityClass.PEDESTRIAN;
            if ((vehicleLayers.value & layerBit) != 0) return SensorEntityClass.VEHICLE;
            if ((obstacleLayers.value & layerBit) != 0) return SensorEntityClass.STATIC_OBSTACLE;
            return SensorEntityClass.UNKNOWN;
        }

        private void OnDrawGizmosSelected()
        {
            var origin = sensorOrigin != null ? sensorOrigin : transform;
            Gizmos.color = Color.cyan;
            for (int beam = 0; beam < beamCount; beam++)
            {
                float bearing = BeamBearing(beam);
                Vector3 direction = origin.forward * Mathf.Cos(bearing) - origin.right * Mathf.Sin(bearing);
                Gizmos.DrawRay(origin.position, direction * maxRangeM);
            }
        }
    }
}
