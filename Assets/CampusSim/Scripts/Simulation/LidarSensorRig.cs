using System;
using UnityEngine;
using UnityEngine.Serialization;

namespace InhaExpress.Client.Simulation
{
    /// <summary>
    /// Multi-channel, raycast-based LiDAR abstraction for the Unity simulation.
    /// This produces local observations only; it does not access target transforms
    /// beyond the Physics hit and does not send data to Python.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class LidarSensorRig : MonoBehaviour
    {
        private const int MaxRaysPerScan = 8192;

        [Header("Identity")]
        [SerializeField] private string vehicleId = "vehicle-unassigned";
        [SerializeField] private string sensorId = "lidar-front";
        [SerializeField] private Transform sensorMount;
        [SerializeField] private Transform vehicleRoot;

        [Header("Prototype scan settings — measure before fixing for experiments")]
        [SerializeField, Range(1, 360)] private int horizontalFieldOfViewDegrees = 360;
        [SerializeField, FormerlySerializedAs("rayCount"), Range(1, 4096)] private int horizontalRayCount = 360;
        [SerializeField, Range(0f, 120f), Tooltip("Vertical scan span below the horizontal channel (0 degrees).")]
        private float verticalFieldOfViewDegrees = 30f;
        [SerializeField, Range(1, 32)] private int verticalChannelCount = 7;
        [SerializeField, Min(0.1f)] private float maxRangeM = 15f;
        [SerializeField, Min(0.1f)] private float scanRateHz = 10f;
        [SerializeField, Min(1)] private int maxHitsPerRay = 32;
        [SerializeField] private LayerMask layerMask = ~0;
        [SerializeField, Min(0f)] private float worldUpOffsetM = 0.1f;

        private bool externallyClocked, filterGround;
        private float groundSurfaceHeight, upperElevationDegrees;
        private RaycastHit[] hitBuffer;
        private SensorObservation latestObservation;
        private double nextScanTime;
        private long scanSequence;
        private bool hasPendingFrameContext;
        private string pendingVehicleId;
        private long pendingObservedTick;
        private int pendingPoseVersion;

        public event Action<SensorObservation> ObservationUpdated;

        public SensorObservation LatestObservation => latestObservation;
        public float ScanRateHz => scanRateHz;
        public int RayCount => horizontalRayCount * verticalChannelCount;
        public int HorizontalRayCount => horizontalRayCount;
        public int VerticalChannelCount => verticalChannelCount;
        public float MaxRangeM => maxRangeM;
        public string VehicleId => vehicleId;
        public string SensorId => sensorId;

        private Transform Mount => sensorMount != null ? sensorMount : transform;
        private Transform VehicleRoot => vehicleRoot != null ? vehicleRoot : transform.root;

        private void Awake()
        {
            NormalizeSettings();
            EnsureBuffers();
        }

        private void OnEnable()
        {
            nextScanTime = Time.fixedTimeAsDouble;
        }

        private void FixedUpdate()
        {
            if (externallyClocked) return;
            double now = Time.fixedTimeAsDouble;
            if (now < nextScanTime)
                return;

            nextScanTime = now + 1d / Math.Max(0.1f, scanRateHz);
            Scan();
        }

        /// <summary>
        /// Supplies the authoritative run tick and ego localization pose version for
        /// the next scan. Until a simulation/localization owner calls this, those
        /// contract fields remain null and the frame must not be sent to Python.
        /// Call on Unity's main thread.
        /// </summary>
        public void SetFrameContext(string contextVehicleId, long observedTick, int poseVersion)
        {
            if (string.IsNullOrWhiteSpace(contextVehicleId))
                throw new ArgumentException("Vehicle ID is required.", nameof(contextVehicleId));
            if (observedTick < 0)
                throw new ArgumentOutOfRangeException(nameof(observedTick));
            if (poseVersion < 0)
                throw new ArgumentOutOfRangeException(nameof(poseVersion));

            pendingVehicleId = contextVehicleId.Trim();
            pendingObservedTick = observedTick;
            pendingPoseVersion = poseVersion;
            hasPendingFrameContext = true;
        }

        /// <summary>For editor/debug harnesses that need an immediate scan.</summary>
        public void ScanNow()
        {
            EnsureBuffers();
            Scan();
            nextScanTime = Time.fixedTimeAsDouble + 1d / Math.Max(0.1f, scanRateHz);
        }

        public void ConfigureForPc(Transform root, string id, float groundHeight)
        {
            vehicleRoot = root;
            vehicleId = id;
            sensorId = "pc-lidar";
            externallyClocked = true;
            filterGround = true;
            groundSurfaceHeight = groundHeight;
            horizontalRayCount = 360;
            verticalChannelCount = 7;
            upperElevationDegrees = 15f;
            horizontalFieldOfViewDegrees = 360;
            maxRangeM = 15f;
            scanRateHz = 10f;
            NormalizeSettings();
            EnsureBuffers();
        }

        public void SetGroundSurfaceHeight(float height) => groundSurfaceHeight = height;

        private void Scan()
        {
            EnsureBuffers();
            Transform mount = Mount;
            Vector3 origin = mount.position + Vector3.up * worldUpOffsetM;
            Vector3 forward = Vector3.ProjectOnPlane(mount.forward, Vector3.up);
            if (forward.sqrMagnitude < 0.0001f)
                forward = Vector3.forward;
            else
                forward.Normalize();

            long? observedTick = hasPendingFrameContext ? pendingObservedTick : (long?)null;
            int? poseVersion = hasPendingFrameContext ? pendingPoseVersion : (int?)null;
            string frameVehicleId = hasPendingFrameContext ? pendingVehicleId : vehicleId;
            hasPendingFrameContext = false;
            pendingVehicleId = null;

            scanSequence++;
            int totalRayCount = RayCount;
            latestObservation.BeginScan(frameVehicleId, sensorId, scanSequence,
                observedTick, poseVersion, totalRayCount);

            bool fullCircle = horizontalFieldOfViewDegrees >= 360;
            for (int verticalIndex = 0; verticalIndex < verticalChannelCount; verticalIndex++)
            {
                float elevationDegrees = verticalChannelCount <= 1
                    ? 0f
                    : -verticalFieldOfViewDegrees +
                      (verticalFieldOfViewDegrees + upperElevationDegrees) * verticalIndex / (verticalChannelCount - 1);
                float elevationRad = elevationDegrees * Mathf.Deg2Rad;
                float horizontalScale = Mathf.Cos(elevationRad);
                float verticalScale = Mathf.Sin(elevationRad);

                for (int horizontalIndex = 0; horizontalIndex < horizontalRayCount; horizontalIndex++)
                {
                    int rayIndex = verticalIndex * horizontalRayCount + horizontalIndex;
                    float yawDegrees = fullCircle
                        ? -180f + 360f * horizontalIndex / horizontalRayCount
                        : -horizontalFieldOfViewDegrees * 0.5f +
                          horizontalFieldOfViewDegrees * horizontalIndex / Mathf.Max(1, horizontalRayCount - 1);
                    Vector3 horizontalDirection = Quaternion.AngleAxis(yawDegrees, Vector3.up) * forward;
                    Vector3 direction = (horizontalDirection * horizontalScale +
                                         Vector3.up * verticalScale).normalized;
                    int hitCount = Physics.RaycastNonAlloc(origin, direction, hitBuffer, maxRangeM,
                        layerMask, QueryTriggerInteraction.Ignore);

                    bool overflowed = hitCount >= hitBuffer.Length;
                    int nearestHitIndex = -1;
                    float nearestDistance = float.PositiveInfinity;
                    for (int hitIndex = 0; hitIndex < hitCount; hitIndex++)
                    {
                        RaycastHit candidate = hitBuffer[hitIndex];
                        Collider collider = candidate.collider;
                        if (collider == null || IsVehicleCollider(collider.transform))
                            continue;
                        if (candidate.distance < nearestDistance)
                        {
                            nearestDistance = candidate.distance;
                            nearestHitIndex = hitIndex;
                        }
                    }

                    int detectionIndex = -1;
                    Vector3 endpoint = origin + direction * maxRangeM;
                    bool hasVisualReturn = false;
                    if (nearestHitIndex >= 0)
                    {
                        RaycastHit hit = hitBuffer[nearestHitIndex];
                        endpoint = hit.point;
                        LidarIgnore ignoredSurface = hit.collider.GetComponentInParent<LidarIgnore>();
                        hasVisualReturn = ignoredSurface == null || ignoredSurface.ShowReturnInPointCloud;
                        if (ignoredSurface == null && !(filterGround && hit.normal.y > .75f && hit.point.y <= groundSurfaceHeight))
                        {
                            Vector3 sensorLocalPosition = Mount.InverseTransformPoint(hit.point);
                            float bearingRad = Mathf.Atan2(sensorLocalPosition.x, sensorLocalPosition.z);
                            LidarTarget target = hit.collider.GetComponentInParent<LidarTarget>();
                            LidarEntityClass entityClass = target != null ? target.EntityClass : LidarEntityClass.Unknown;
                            string entityId = target != null ? target.EntityId : null;
                            detectionIndex = latestObservation.AddDetection(new SensorDetection(
                                hit.distance, bearingRad, sensorLocalPosition, entityClass, entityId));
                        }
                    }

                    latestObservation.AddRay(new LidarRaySample(origin, endpoint, detectionIndex,
                        overflowed, hasVisualReturn));
                }
            }

            ObservationUpdated?.Invoke(latestObservation);
        }

        private bool IsVehicleCollider(Transform colliderTransform)
        {
            Transform root = VehicleRoot;
            return colliderTransform == root || colliderTransform.IsChildOf(root);
        }

        private void EnsureBuffers()
        {
            int requiredRayCount = Mathf.Max(1, RayCount);
            int requiredHitCount = Mathf.Max(1, maxHitsPerRay);
            if (latestObservation == null || latestObservation.RayCount > requiredRayCount)
                latestObservation = new SensorObservation(requiredRayCount);
            else
                latestObservation.EnsureCapacity(requiredRayCount);

            if (hitBuffer == null || hitBuffer.Length != requiredHitCount)
                hitBuffer = new RaycastHit[requiredHitCount];
        }

        private void OnValidate() => NormalizeSettings();

        private void NormalizeSettings()
        {
            if (string.IsNullOrWhiteSpace(vehicleId)) vehicleId = "vehicle-unassigned";
            if (string.IsNullOrWhiteSpace(sensorId)) sensorId = "lidar-front";
            horizontalFieldOfViewDegrees = Mathf.Clamp(horizontalFieldOfViewDegrees, 1, 360);
            horizontalRayCount = Mathf.Clamp(horizontalRayCount, 1, 4096);
            verticalFieldOfViewDegrees = IsFinite(verticalFieldOfViewDegrees)
                ? Mathf.Clamp(verticalFieldOfViewDegrees, 0f, 120f)
                : 30f;
            verticalChannelCount = Mathf.Clamp(verticalChannelCount, 1,
                Mathf.Max(1, MaxRaysPerScan / Mathf.Max(1, horizontalRayCount)));
            maxRangeM = IsFinite(maxRangeM) ? Mathf.Max(0.1f, maxRangeM) : 15f;
            scanRateHz = IsFinite(scanRateHz) ? Mathf.Max(0.1f, scanRateHz) : 10f;
            maxHitsPerRay = Mathf.Clamp(maxHitsPerRay, 1, 256);
            worldUpOffsetM = IsFinite(worldUpOffsetM) ? Mathf.Max(0f, worldUpOffsetM) : 0.1f;
        }

        private static bool IsFinite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
    }
}
