using System;
using UnityEngine;
namespace InhaExpress.Client.Presentation
{
    [Serializable] public sealed class PhysicalLidarHit
    {
        public float lateral_m, forward_m;
        public string entity_class;
    }
    [Serializable] public sealed class PhysicalLidarFrame
    {
        public int tick, ray_count;
        public float max_range_m;
        public bool valid;
        public string weather = "clear", road_condition = "dry";
        public bool observation_complete = true;
        public PhysicalLidarHit[] hits;
    }
    public interface IPhysicalLidarSource
    {
        PhysicalLidarFrame Capture(int tick);
        void SetSafetyFeedback(bool blocked);
    }
    public sealed class PhysicalEnvironmentState
    {
        public string weather, road_condition, visual_weather_status;
        public int sensor_count, matching_sensor_count;
        public float min_range_m;
    }

    // Simulation registers the concrete sensor, avoiding a Presentation/Simulation assembly cycle.
    public static class PhysicalLidarSource
    {
        public static Func<Transform, string, Bounds, IPhysicalLidarSource> Create;
        public static Func<PhysicalEnvironmentState> ReadEnvironment;
        public static Func<string, string, bool> ApplyEnvironment;
    }
}
