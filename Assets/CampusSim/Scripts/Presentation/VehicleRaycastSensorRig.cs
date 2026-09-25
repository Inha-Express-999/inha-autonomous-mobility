using System;
using System.Collections.Generic;
using InhaExpress.Client.Domain;
using InhaExpress.Client.Networking;
using UnityEngine;

namespace InhaExpress.Client.Presentation
{
    /// <summary>
    /// Low-resolution 2D raycast lidar for the synthetic preview. It reports detections only;
    /// it does not infer hidden actors or issue a safety/control decision.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(VehicleEgoLocalizationReporter))]
    [DefaultExecutionOrder(100)]
    public sealed class VehicleRaycastSensorRig : MonoBehaviour
    {
        private const int MaximumDetections = 64;
        private const double ScanIntervalS = 0.1;

        [SerializeField] private string sensorId = "front-lidar";
        [SerializeField] private Transform sensorOrigin;
        [SerializeField, Range(8, MaximumDetections)] private int beamCount = 64;
        [SerializeField, Min(0.1f)] private float maxRangeM = 30f;
        [SerializeField] private LayerMask obstacleLayers = ~0;
        [SerializeField] private LayerMask pedestrianLayers;
        [SerializeField] private LayerMask vehicleLayers;

        private readonly RaycastHit[] hitBuffer = new RaycastHit[64];
        private readonly List<SensorDetectionDto> detections = new List<SensorDetectionDto>(MaximumDetections);
        private VehicleEgoLocalizationReporter localizationReporter;
        private ClientRuntimeHost runtime;
        private long observedTick;
        private double nextScanAtS;

        public string SensorId => sensorId;
        public long LatestObservedTick => observedTick - 1;

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
            if (string.IsNullOrWhiteSpace(sensorId)) sensorId = "front-lidar";
        }

        private void FixedUpdate()
        {
            if (localizationReporter == null || localizationReporter.LatestObservedTick < 0 ||
                string.IsNullOrWhiteSpace(localizationReporter.VehicleId)) return;

            double nowS = Time.realtimeSinceStartupAsDouble;
            if (nowS < nextScanAtS) return;
            nextScanAtS = nowS + ScanIntervalS;

            if (runtime == null) runtime = FindFirstObjectByType<ClientRuntimeHost>();
            if (runtime == null || runtime.Role != ClientRole.PC_Operator ||
                runtime.Sensors == null || runtime.Store?.Current == null) return;

            bool scanComplete = Scan();
            var observation = new SensorObservationDto(
                localizationReporter.VehicleId,
                sensorId,
                SensorType.LIDAR_2D,
                observedTick++,
                localizationReporter.LatestObservedTick,
                runtime.Store.Current.MapVersion,
                scanComplete,
                detections);
            runtime.Sensors.SendSensorObservation(observation);
        }

        private bool Scan()
        {
            detections.Clear();
            bool complete = true;
            var origin = sensorOrigin != null ? sensorOrigin : transform;
            int layerMask = obstacleLayers.value;
            Transform vehicleRoot = transform.root;
            for (int beam = 0; beam < beamCount; beam++)
            {
                float bearing = -Mathf.PI + (2f * Mathf.PI * beam / beamCount);
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

                Vector3 sensorLocal = origin.InverseTransformPoint(hit.point);
                double forward = sensorLocal.z;
                double left = -sensorLocal.x;
                double horizontalRange = Math.Sqrt(forward * forward + left * left);
                if (horizontalRange <= 0.01 || horizontalRange > maxRangeM) continue;

                detections.Add(new SensorDetectionDto(
                    horizontalRange,
                    Math.Atan2(left, forward),
                    new MapPositionDto(forward, left, sensorLocal.y),
                    Classify(hit.collider.gameObject.layer)));
            }
            return complete;
        }

        private bool TryNearestExternalHit(int hitCount, Transform vehicleRoot, out RaycastHit nearest)
        {
            nearest = default;
            float nearestDistance = float.PositiveInfinity;
            bool found = false;
            for (int index = 0; index < hitCount; index++)
            {
                var hit = hitBuffer[index];
                if (hit.collider == null || hit.collider.transform.root == vehicleRoot ||
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
                float bearing = -Mathf.PI + (2f * Mathf.PI * beam / beamCount);
                Vector3 direction = origin.forward * Mathf.Cos(bearing) - origin.right * Mathf.Sin(bearing);
                Gizmos.DrawRay(origin.position, direction * maxRangeM);
            }
        }
    }
}
