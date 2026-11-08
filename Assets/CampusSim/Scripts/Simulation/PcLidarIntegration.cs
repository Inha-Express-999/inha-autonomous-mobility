using InhaExpress.Client.Presentation;
using UnityEngine;

namespace InhaExpress.Client.Simulation
{
    /// <summary>
    /// Only the raw range image crosses the control boundary (protocol 6): no target
    /// transforms, velocities, semantic labels or ground pre-filtering. The rig's own
    /// ground plane/labels drive the PC point-cloud display only.
    /// </summary>
    public sealed class PcLidarIntegration : MonoBehaviour, IPhysicalLidarSource
    {
        private Transform owner;
        private Bounds body;
        private LidarSensorRig rig;
        private LidarPointCloudVisualizer overlay;
        private WheelCollider[] wheels;
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
            integration.wheels = root.GetComponentsInChildren<WheelCollider>();
            integration.rig = mount.AddComponent<LidarSensorRig>();
            integration.rig.ConfigureForPc(root, id, integration.DisplayGroundHeight());
            integration.overlay = mount.AddComponent<LidarPointCloudVisualizer>();
            integration.overlay.ConfigurePcOverlay();
            // Display/debug class for other vehicles' rigs; never sent to Python.
            var semantic = root.GetComponent<LidarTarget>() ?? root.gameObject.AddComponent<LidarTarget>();
            semantic.SetObservedClass(LidarEntityClass.Vehicle);
            return integration;
        }

        private float DisplayGroundHeight() => owner.TransformPoint(new Vector3(0, body.min.y + .08f, 0)).y;

        public PhysicalLidarFrame Capture(int tick)
        {
            rig.SetGroundSurfaceHeight(DisplayGroundHeight());
            rig.SetFrameContext(vehicleId, tick, tick);
            rig.ScanNow();
            var frame = rig.LatestObservation;
            Vector3 forward = Vector3.ProjectOnPlane(owner.forward, Vector3.up).normalized;
            Vector3 right = Vector3.Cross(Vector3.up, forward);
            int columns = rig.HorizontalRayCount, channels = rig.VerticalChannelCount;
            float maxRangeMm = rig.MaxRangeM * 1000f;
            var ranges = new int[frame.RayCount];
            bool encounter = false;
            for (int i = 0; i < frame.RayCount; i++)
            {
                var ray = frame.GetRay(i);
                ranges[i] = Mathf.Clamp(Mathf.RoundToInt(ray.ReturnRangeM * 1000f), 0, Mathf.FloorToInt(maxRangeMm));
                if (!ray.HasHit) continue;  // Overlay trigger only (display ground plane).
                Vector3 offset = ray.EndpointWorld - owner.position;
                if (Mathf.Abs(Vector3.Dot(offset, right)) < 2.5f && Mathf.Abs(Vector3.Dot(offset, forward)) < 7f) encounter = true;
            }
            if (encounter) encounterUntil = Time.unscaledTime + .7f;
            overlay.VisualizationEnabled = blocked || Time.unscaledTime < encounterUntil;

            var elevations = new float[channels];
            for (int c = 0; c < channels; c++) elevations[c] = rig.GetChannelElevationDegrees(c);
            Vector3 sensor = rig.ScanOriginWorld - owner.position;
            float sensorLateral = Vector3.Dot(sensor, right), sensorForward = Vector3.Dot(sensor, forward);
            MeasureContactPlane(forward, right, sensorLateral, sensorForward,
                out float groundUp, out float gradeForward, out float gradeLateral);
            return new PhysicalLidarFrame
            {
                tick = tick,
                valid = frame.IsValid && frame.ObservedTick == tick && frame.PoseVersion == tick &&
                        frame.RayCount == columns * channels && rig.HorizontalFieldOfViewDegrees >= 360,
                max_range_m = rig.MaxRangeM, columns = columns,
                azimuth_start_deg = -180f, azimuth_span_deg = 360f,
                channel_elevations_deg = elevations,
                sensor_lateral_m = sensorLateral, sensor_forward_m = sensorForward,
                sensor_up_m = sensor.y,
                ground_up_m = groundUp, ground_grade_forward = gradeForward, ground_grade_lateral = gradeLateral,
                ranges_mm = ranges
            };
        }

        /// <summary>
        /// Wheel-contact plane from the vehicle's own WheelColliders (proprioception),
        /// least-squares grade along leveled forward/right, evaluated beneath the sensor.
        /// Falls back to the body bottom and a level plane when fewer than three
        /// wheels touch the ground.
        /// </summary>
        private void MeasureContactPlane(Vector3 forward, Vector3 right, float sensorLateral, float sensorForward,
            out float up, out float gradeForward, out float gradeLateral)
        {
            up = owner.TransformPoint(new Vector3(0, body.min.y, 0)).y - owner.position.y;
            gradeForward = gradeLateral = 0f;
            if (wheels == null) return;
            int count = 0;
            float sumF = 0, sumL = 0, sumU = 0;
            var samples = new Vector3[wheels.Length];
            foreach (var wheel in wheels)
            {
                if (wheel == null || !wheel.GetGroundHit(out var hit)) continue;
                Vector3 offset = hit.point - owner.position;
                var sample = new Vector3(Vector3.Dot(offset, right), offset.y, Vector3.Dot(offset, forward));
                samples[count++] = sample;
                sumL += sample.x; sumU += sample.y; sumF += sample.z;
            }
            if (count < 3) return;
            float meanL = sumL / count, meanU = sumU / count, meanF = sumF / count;
            float varF = 0, varL = 0, covF = 0, covL = 0;
            for (int i = 0; i < count; i++)
            {
                float f = samples[i].z - meanF, l = samples[i].x - meanL, u = samples[i].y - meanU;
                varF += f * f; covF += f * u; varL += l * l; covL += l * u;
            }
            if (varF > .01f) gradeForward = Mathf.Clamp(covF / varF, -1f, 1f);
            if (varL > .01f) gradeLateral = Mathf.Clamp(covL / varL, -1f, 1f);
            up = meanU + gradeForward * (sensorForward - meanF) + gradeLateral * (sensorLateral - meanL);
        }

        public void SetSafetyFeedback(bool value)
        {
            blocked = value;
            if (blocked) overlay.VisualizationEnabled = true;
        }
    }
}
