using System.Collections.Generic;
using InhaExpress.Client.Presentation;
using UnityEngine;

namespace InhaExpress.Client.Simulation
{
    /// <summary>Only Physics hit samples cross the control boundary. No target transforms/velocities.</summary>
    public sealed class PcLidarIntegration : MonoBehaviour, IPhysicalLidarSource
    {
        private Transform owner;
        private Bounds body;
        private LidarSensorRig rig;
        private LidarPointCloudVisualizer overlay;
        private string vehicleId;
        private bool blocked;
        private WheelCollider[] wheels;
        private float encounterUntil;
        public LidarSensorRig Sensor => rig;
        public bool EffectActive => overlay != null && overlay.VisualizationEnabled;

        private static readonly List<PcLidarIntegration> sources = new List<PcLidarIntegration>();
        private static string selectedWeather, selectedRoad;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void Register()
        {
            sources.Clear(); selectedWeather = selectedRoad = null;
            PhysicalLidarSource.Create = Attach;
            PhysicalLidarSource.ReadEnvironment = ReadEnvironment;
            PhysicalLidarSource.ApplyEnvironment = ApplyEnvironment;
        }

        private static void ReadSelectedEnvironment(out LidarSensorRig.WeatherCondition weather,
            out LidarSensorRig.RoadCondition road)
        {
            string weatherSetting = selectedWeather ?? System.Environment.GetEnvironmentVariable("INHA_SIM_WEATHER") ?? "Clear";
            string roadSetting = selectedRoad ?? System.Environment.GetEnvironmentVariable("INHA_SIM_ROAD");
            bool weatherValid = System.Enum.TryParse(weatherSetting, true, out weather)
                && System.Enum.IsDefined(typeof(LidarSensorRig.WeatherCondition), weather);
            if (roadSetting == null) roadSetting = weather == LidarSensorRig.WeatherCondition.Rain ? "Wet" :
                weather == LidarSensorRig.WeatherCondition.Snow ? "Snow" : "Dry";
            bool roadValid = System.Enum.TryParse(roadSetting, true, out road)
                && System.Enum.IsDefined(typeof(LidarSensorRig.RoadCondition), road);
            if (!weatherValid || !roadValid)
            {
                weather = LidarSensorRig.WeatherCondition.Clear;
                road = LidarSensorRig.RoadCondition.Unknown;
            }
        }

        private static bool ApplyEnvironment(string weather, string road)
        {
            if (!System.Enum.TryParse(weather, true, out LidarSensorRig.WeatherCondition condition)
                || !System.Enum.IsDefined(typeof(LidarSensorRig.WeatherCondition), condition)
                || !System.Enum.TryParse(road, true, out LidarSensorRig.RoadCondition surface)
                || !System.Enum.IsDefined(typeof(LidarSensorRig.RoadCondition), surface)) return false;
            selectedWeather = condition.ToString(); selectedRoad = surface.ToString();
            sources.RemoveAll(source => source == null);
            foreach (var source in sources)
                source.rig.SetEnvironment(condition, surface);
            CozyOperatorWeather.Apply(condition.ToString().ToLowerInvariant());
            return true;
        }

        private static PhysicalEnvironmentState ReadEnvironment()
        {
            ReadSelectedEnvironment(out var weather, out var road);
            var result = new PhysicalEnvironmentState { weather = weather.ToString().ToLowerInvariant(),
                road_condition = road.ToString().ToLowerInvariant(), min_range_m = 0, visual_weather_status = CozyOperatorWeather.Status };
            sources.RemoveAll(source => source == null);
            result.sensor_count = sources.Count;
            foreach (var source in sources)
            {
                if (source.rig.Weather == weather && source.rig.Road == road) result.matching_sensor_count++;
                result.min_range_m = result.min_range_m == 0 ? source.rig.EffectiveRangeM :
                    Mathf.Min(result.min_range_m, source.rig.EffectiveRangeM);
            }
            return result;
        }

        private void OnDestroy() => sources.Remove(this);

        public static IPhysicalLidarSource Attach(Transform root, string id, Bounds bounds)
        {
            var previous = root.GetComponentInChildren<PcLidarIntegration>();
            if (previous != null) return previous;
            var mount = new GameObject("PC Continuous LiDAR");
            mount.transform.SetParent(root, false);
            mount.transform.localPosition = new Vector3(bounds.center.x, bounds.max.y + .15f, bounds.center.z);
            var integration = mount.AddComponent<PcLidarIntegration>();
            integration.owner = root;
            integration.body = bounds;
            integration.wheels = root.GetComponentsInChildren<WheelCollider>();
            integration.vehicleId = id;
            integration.rig = mount.AddComponent<LidarSensorRig>();
            integration.rig.ConfigureForPc(root, id, integration.GroundHeight());
            ReadSelectedEnvironment(out var weather, out var road);
            integration.rig.ConfigureWeather(weather, road);
            sources.Add(integration);
            CozyOperatorWeather.Apply(weather.ToString().ToLowerInvariant());
            integration.overlay = mount.AddComponent<LidarPointCloudVisualizer>();
            integration.overlay.ConfigurePcOverlay();
            var semantic = root.GetComponent<LidarTarget>() ?? root.gameObject.AddComponent<LidarTarget>();
            semantic.SetObservedClass(LidarEntityClass.Vehicle);
            return integration;
        }

        private float GroundHeight() => owner.TransformPoint(new Vector3(0, body.min.y + .08f, 0)).y;

        public PhysicalLidarFrame Capture(int tick)
        {
            rig.SetGroundSurfaceHeight(GroundHeight());
            Vector3 point = Vector3.zero, normal = Vector3.zero;
            int contacts = 0;
            foreach (var wheel in wheels)
                if (wheel != null && wheel.GetGroundHit(out var contact))
                {
                    point += contact.point;
                    normal += contact.normal;
                    contacts++;
                }
            // Missing contacts still stop the client through sensor_valid. Never reuse
            // a stale plane; standalone sensor fixtures retain the legacy flat fallback.
            if (contacts >= 3)
            {
                point /= contacts;
                normal.Normalize();
                // Preserve the existing chassis-underbody clearance, but along the
                // measured road normal. Using just 8cm above tyre contacts would
                // newly classify low road/kerb tops as obstacles on curved grades.
                Vector3 bodyFloor = owner.TransformPoint(new Vector3(0, body.min.y, 0));
                point += normal * Mathf.Max(0, Vector3.Dot(bodyFloor - point, normal));
                rig.SetGroundPlane(point, normal);
            }
            else rig.SetGroundPlane(Vector3.zero, Vector3.zero);
            rig.SetFrameContext(vehicleId, tick, tick);
            rig.ScanNow();
            var frame = rig.LatestObservation;
            var points = new List<PhysicalLidarHit>(frame.DetectionCount);
            Vector3 forward = Vector3.ProjectOnPlane(owner.forward, Vector3.up).normalized;
            Vector3 right = Vector3.Cross(Vector3.up, forward);
            bool encounter = false;
            float encounterWidth = owner.TransformVector(Vector3.right * Mathf.Max(
                Mathf.Abs(body.min.x), Mathf.Abs(body.max.x))).magnitude + .8f;
            for (int i = 0; i < frame.RayCount; i++)
            {
                var ray = frame.GetRay(i);
                if (!ray.HasHit) continue;
                var hit = frame.GetDetection(ray.DetectionIndex);
                Vector3 offset = ray.EndpointWorld - owner.position;
                float lateral = Vector3.Dot(offset, right), ahead = Vector3.Dot(offset, forward);
                string kind = hit.EntityClass == LidarEntityClass.StaticObstacle ? "static" :
                    hit.EntityClass == LidarEntityClass.Pedestrian ? "pedestrian" :
                    hit.EntityClass == LidarEntityClass.Vehicle ? "vehicle" : "unknown";
                points.Add(new PhysicalLidarHit { lateral_m = lateral, forward_m = ahead, entity_class = kind });
                // A roadside kerb/facade return is not an encounter. Keep every return
                // in the server safety input; static hazards activate the display only
                // when the server reports an actual swept-path risk. Moving/unknown
                // objects may activate it early, in front of the vehicle only.
                if (hit.EntityClass != LidarEntityClass.StaticObstacle &&
                    Mathf.Abs(lateral) < encounterWidth && ahead > 0 && ahead < 7f) encounter = true;
            }
            if (encounter) encounterUntil = Time.unscaledTime + .7f;
            overlay.VisualizationEnabled = blocked || Time.unscaledTime < encounterUntil;
            return new PhysicalLidarFrame { tick = tick, valid = frame.IsValid && frame.ObservedTick == tick && frame.PoseVersion == tick,
                ray_count = frame.RayCount, max_range_m = rig.EffectiveRangeM, hits = points.ToArray(),
                weather = rig.Weather.ToString().ToLowerInvariant(),
                road_condition = rig.Road.ToString().ToLowerInvariant(),
                observation_complete = rig.ObservationComplete };
        }

        public void SetSafetyFeedback(bool value)
        {
            blocked = value;
            if (blocked) overlay.VisualizationEnabled = true;
        }
    }
}
