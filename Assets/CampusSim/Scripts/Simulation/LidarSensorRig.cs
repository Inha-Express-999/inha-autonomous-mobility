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

        // Synthetic presets, not calibrated optical/tyre measurements.
        public enum WeatherCondition { Clear, Rain, Snow, Fog }
        public enum RoadCondition { Dry, Wet, Snow, Ice, Unknown }
        [SerializeField] private WeatherCondition weather;
        [SerializeField] private RoadCondition roadCondition;
        [SerializeField, Range(0f, 1f)] private float scanDropoutProbability;
        [SerializeField] private int weatherSeed = 42;
        public WeatherCondition Weather => weather;
        public RoadCondition Road => roadCondition;
        public bool ObservationComplete { get; private set; } = true;
        public float EffectiveRangeM => weather == WeatherCondition.Clear ? maxRangeM : Mathf.Min(maxRangeM,
            weather == WeatherCondition.Rain ? 12f : weather == WeatherCondition.Snow ? 8f :
            weather == WeatherCondition.Fog ? 6f : 15f);

        // UI changes must preserve experiment dropout probability and seed.
        public void SetEnvironment(WeatherCondition condition, RoadCondition road)
        {
            weather = condition;
            roadCondition = road;
        }

        public void ConfigureWeather(WeatherCondition condition, RoadCondition road, float dropout = 0f, int seed = 42)
        {
            weather = condition;
            roadCondition = road;
            scanDropoutProbability = Mathf.Clamp01(dropout);
            weatherSeed = seed;
        }

        private bool externallyClocked, filterGround;
        private float groundSurfaceHeight, upperElevationDegrees;
        private Vector3 groundPlanePoint, groundPlaneNormal = Vector3.up;
        private bool hasGroundPlane;
        public int GroundReturnCount { get; private set; }
        // Editor/operator diagnostics only; collider identity never enters the transport DTO.
        public string NearestObstacleSurface { get; private set; }
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
        public int HorizontalFieldOfViewDegrees => horizontalFieldOfViewDegrees;
        /// <summary>World position every beam of the next scan starts from.</summary>
        public Vector3 ScanOriginWorld => Mount.position + Vector3.up * worldUpOffsetM;
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

        /// <summary>Display-only ground plane for point colours/detections. The control
        /// path uses raw ReturnRangeM and Python geometry-only ground segmentation.</summary>
        public void SetGroundSurfaceHeight(float height) => groundSurfaceHeight = height;

        public void SetGroundPlane(Vector3 point, Vector3 normal)
        {
            hasGroundPlane = normal.sqrMagnitude > .5f && normal.normalized.y > .75f;
            groundPlanePoint = point;
            groundPlaneNormal = hasGroundPlane ? normal.normalized : Vector3.up;
        }

        private bool IsGroundReturn(RaycastHit hit)
        {
            if (!filterGround || hit.normal.y <= .75f) return false;
            // Wheel contact observations define the local supporting surface. A world-Y
            // cutoff mistakes an uphill road for an obstacle as soon as it rises above ego.
            // Retain vertical kerb/obstacle faces and raised surfaces above the plane.
            return hit.point.y <= groundSurfaceHeight || (hasGroundPlane &&
                Vector3.Dot(hit.point - groundPlanePoint, groundPlaneNormal) <= .08f);
        }

        /// <summary>Beam elevation above the world-leveled horizontal plane.</summary>
        public float GetChannelElevationDegrees(int verticalIndex)
        {
            if (verticalIndex < 0 || verticalIndex >= verticalChannelCount)
                throw new ArgumentOutOfRangeException(nameof(verticalIndex));
            return verticalChannelCount <= 1
                ? 0f
                : -verticalFieldOfViewDegrees +
                  (verticalFieldOfViewDegrees + upperElevationDegrees) * verticalIndex / (verticalChannelCount - 1);
        }

        private void Scan()
        {
            EnsureBuffers();
            Transform mount = Mount;
            Vector3 origin = ScanOriginWorld;
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
            var random = new System.Random(unchecked(weatherSeed * 397 ^ (int)scanSequence));
            // An explicit outage must never be interpreted as clear space.
            ObservationComplete = random.NextDouble() >= scanDropoutProbability;
            int totalRayCount = RayCount;
            GroundReturnCount = 0;
            NearestObstacleSurface = null;
            float nearestObstacle = float.PositiveInfinity;
            latestObservation.BeginScan(frameVehicleId, sensorId, scanSequence,
                observedTick, poseVersion, totalRayCount);

            bool fullCircle = horizontalFieldOfViewDegrees >= 360;
            for (int verticalIndex = 0; verticalIndex < verticalChannelCount; verticalIndex++)
            {
                float elevationDegrees = GetChannelElevationDegrees(verticalIndex);
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
                    int hitCount = Physics.RaycastNonAlloc(origin, direction, hitBuffer, EffectiveRangeM,
                        layerMask, QueryTriggerInteraction.Ignore);

                    bool overflowed = hitCount >= hitBuffer.Length;
                    if (!ObservationComplete) hitCount = 0;
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
                    Vector3 endpoint = origin + direction * EffectiveRangeM;
                    bool hasVisualReturn = false;
                    float returnRange = 0f;
                    if (nearestHitIndex >= 0)
                    {
                        RaycastHit hit = hitBuffer[nearestHitIndex];
                        endpoint = hit.point;
                        returnRange = hit.distance;
                        LidarIgnore ignoredSurface = hit.collider.GetComponentInParent<LidarIgnore>();
                        hasVisualReturn = ignoredSurface == null || ignoredSurface.ShowReturnInPointCloud;
                        bool ground = IsGroundReturn(hit);
                        if (ground) GroundReturnCount++;
                        if (ignoredSurface == null && !ground)
                        {
                            if (hit.distance < nearestObstacle)
                            {
                                nearestObstacle = hit.distance;
                                NearestObstacleSurface = $"{hit.collider.name} / {hit.distance:F2}m / normal {hit.normal:F3}";
                            }
                            Vector3 sensorLocalPosition = Mount.InverseTransformPoint(hit.point);
                            float bearingRad = Mathf.Atan2(sensorLocalPosition.x, sensorLocalPosition.z);
                            LidarTarget target = hit.collider.GetComponentInParent<LidarTarget>();
                            LidarEntityClass entityClass = target != null ? target.EntityClass : LidarEntityClass.Unknown;
                            if (target == null && hit.collider.attachedRigidbody == null &&
                                hit.collider.GetComponentInParent<LidarStaticMapSurface>() != null)
                                entityClass = LidarEntityClass.StaticObstacle;
                            string entityId = target != null ? target.EntityId : null;
                            detectionIndex = latestObservation.AddDetection(new SensorDetection(
                                hit.distance, bearingRad, sensorLocalPosition, entityClass, entityId));
                        }
                    }

                    latestObservation.AddRay(new LidarRaySample(origin, endpoint, detectionIndex,
                        overflowed, hasVisualReturn, returnRange));
                }
            }

            if (!ObservationComplete) latestObservation.Invalidate();
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
