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
        private float encounterUntil;
        public LidarSensorRig Sensor => rig;
        public bool EffectActive => overlay != null && overlay.VisualizationEnabled;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void Register() => PhysicalLidarSource.Create = Attach;

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
            integration.vehicleId = id;
            integration.rig = mount.AddComponent<LidarSensorRig>();
            integration.rig.ConfigureForPc(root, id, integration.GroundHeight());
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
            rig.SetFrameContext(vehicleId, tick, tick);
            rig.ScanNow();
            var frame = rig.LatestObservation;
            var points = new List<PhysicalLidarHit>(frame.DetectionCount);
            Vector3 forward = Vector3.ProjectOnPlane(owner.forward, Vector3.up).normalized;
            Vector3 right = Vector3.Cross(Vector3.up, forward);
            bool encounter = false;
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
                if (Mathf.Abs(lateral) < 2.5f && Mathf.Abs(ahead) < 7f) encounter = true;
            }
            if (encounter) encounterUntil = Time.unscaledTime + .7f;
            overlay.VisualizationEnabled = blocked || Time.unscaledTime < encounterUntil;
            return new PhysicalLidarFrame { tick = tick, valid = frame.IsValid && frame.ObservedTick == tick && frame.PoseVersion == tick,
                ray_count = frame.RayCount, max_range_m = rig.MaxRangeM, hits = points.ToArray() };
        }

        public void SetSafetyFeedback(bool value)
        {
            blocked = value;
            if (blocked) overlay.VisualizationEnabled = true;
        }
    }
}
