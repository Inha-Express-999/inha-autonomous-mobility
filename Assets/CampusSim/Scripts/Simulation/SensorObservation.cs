using UnityEngine;

namespace InhaExpress.Client.Simulation
{
    /// <summary>
    /// One hit returned by the simulated sensor. Position is in Unity sensor-local
    /// coordinates (right, up, forward); convert it at the Python transport boundary.
    /// </summary>
    public readonly struct SensorDetection
    {
        public float RangeM { get; }
        public float BearingRad { get; }
        public Vector3 SensorLocalPosition { get; }
        public LidarEntityClass EntityClass { get; }
        public string EntityId { get; }

        public SensorDetection(float rangeM, float bearingRad, Vector3 sensorLocalPosition,
            LidarEntityClass entityClass, string entityId)
        {
            RangeM = rangeM;
            BearingRad = bearingRad;
            SensorLocalPosition = sensorLocalPosition;
            EntityClass = entityClass;
            EntityId = entityId;
        }
    }

    /// <summary>
    /// The ray segment used to render one LiDAR beam. DetectionIndex is -1 on a miss.
    /// </summary>
    public readonly struct LidarRaySample
    {
        public Vector3 OriginWorld { get; }
        public Vector3 EndpointWorld { get; }
        public int DetectionIndex { get; }
        public bool IsOverflowed { get; }
        public bool HasHit => DetectionIndex >= 0;
        public bool HasVisualReturn { get; }

        public LidarRaySample(Vector3 originWorld, Vector3 endpointWorld, int detectionIndex, bool isOverflowed)
            : this(originWorld, endpointWorld, detectionIndex, isOverflowed, detectionIndex >= 0)
        {
        }

        public LidarRaySample(Vector3 originWorld, Vector3 endpointWorld, int detectionIndex,
            bool isOverflowed, bool hasVisualReturn)
        {
            OriginWorld = originWorld;
            EndpointWorld = endpointWorld;
            DetectionIndex = detectionIndex;
            IsOverflowed = isOverflowed;
            HasVisualReturn = hasVisualReturn;
        }
    }

    /// <summary>
    /// Reused output buffer for a LiDAR scan. The next scan overwrites its contents;
    /// subscribers should consume or copy values during ObservationUpdated.
    /// </summary>
    public sealed class SensorObservation
    {
        private SensorDetection[] detections;
        private LidarRaySample[] rays;

        public string VehicleId { get; private set; }
        public string SensorId { get; private set; }
        public string SensorType => "LIDAR";
        public long ScanSequence { get; private set; }
        public long? ObservedTick { get; private set; }
        public int? PoseVersion { get; private set; }
        public bool HasAuthoritativeFrameContext => ObservedTick.HasValue && PoseVersion.HasValue;
        public bool IsValid { get; private set; }
        public int OverflowedRayCount { get; private set; }
        public int DetectionCount { get; private set; }
        public int RayCount { get; private set; }

        internal SensorObservation(int rayCapacity)
        {
            detections = new SensorDetection[Mathf.Max(1, rayCapacity)];
            rays = new LidarRaySample[Mathf.Max(1, rayCapacity)];
        }

        internal void BeginScan(string vehicleId, string sensorId, long scanSequence,
            long? observedTick, int? poseVersion, int rayCapacity)
        {
            EnsureCapacity(rayCapacity);
            VehicleId = vehicleId;
            SensorId = sensorId;
            ScanSequence = scanSequence;
            ObservedTick = observedTick;
            PoseVersion = poseVersion;
            IsValid = true;
            OverflowedRayCount = 0;
            DetectionCount = 0;
            RayCount = 0;
        }

        internal int AddDetection(SensorDetection detection)
        {
            if (DetectionCount >= detections.Length)
            {
                IsValid = false;
                return -1;
            }

            detections[DetectionCount] = detection;
            return DetectionCount++;
        }

        internal void AddRay(LidarRaySample sample)
        {
            if (RayCount >= rays.Length)
            {
                IsValid = false;
                return;
            }

            rays[RayCount++] = sample;
            if (sample.IsOverflowed)
            {
                IsValid = false;
                OverflowedRayCount++;
            }
        }

        public SensorDetection GetDetection(int index)
        {
            if (index < 0 || index >= DetectionCount)
                throw new System.ArgumentOutOfRangeException(nameof(index));
            return detections[index];
        }

        public LidarRaySample GetRay(int index)
        {
            if (index < 0 || index >= RayCount)
                throw new System.ArgumentOutOfRangeException(nameof(index));
            return rays[index];
        }

        internal void EnsureCapacity(int capacity)
        {
            int required = Mathf.Max(1, capacity);
            if (detections.Length != required)
                detections = new SensorDetection[required];
            if (rays.Length != required)
                rays = new LidarRaySample[required];
        }
    }
}
