using System;
using UnityEngine;
namespace InhaExpress.Client.Presentation
{
    /// <summary>
    /// Physical protocol 6 raw range image. ranges_mm is channel-major
    /// (channel * columns + column), 0 = no return. No labels or pre-filtered hits:
    /// Python segments ground from geometry only. Offsets are in the world-leveled
    /// ego frame (right, forward, up) relative to the vehicle localization origin;
    /// ground_* is the measured wheel-contact plane (proprioception).
    /// </summary>
    [Serializable] public sealed class PhysicalLidarFrame
    {
        public int tick, columns;
        public bool valid;
        public float max_range_m, azimuth_start_deg, azimuth_span_deg;
        public float[] channel_elevations_deg;
        public float sensor_lateral_m, sensor_forward_m, sensor_up_m;
        public float ground_up_m, ground_grade_forward, ground_grade_lateral;
        public int[] ranges_mm;
    }
    public interface IPhysicalLidarSource
    {
        PhysicalLidarFrame Capture(int tick);
        void SetSafetyFeedback(bool blocked);
    }
    // Simulation registers the concrete sensor, avoiding a Presentation/Simulation assembly cycle.
    public static class PhysicalLidarSource
    {
        public static Func<Transform, string, Bounds, IPhysicalLidarSource> Create;
    }
}
