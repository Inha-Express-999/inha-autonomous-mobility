using UnityEngine;
using UnityEngine.Rendering;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
using UnityEngine.InputSystem;
#endif

namespace InhaExpress.Client.Simulation
{
    /// <summary>
    /// Development-only pooled ray/hit overlay. It visualizes the observation buffer
    /// and never feeds Ground Truth or debug data back into planning.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(LidarSensorRig))]
    public sealed class LidarDebugVisualizer : MonoBehaviour
    {
        [SerializeField] private bool visualizationEnabled = true;
        [SerializeField, Min(0.005f)] private float rayWidthM = 0.018f;
        [SerializeField, Min(0.01f)] private float hitWidthM = 0.07f;

        private LidarSensorRig sensor;
        private LineRenderer[] rayRenderers;
        private Transform lineRoot;
        private Material[] runtimeLineMaterials;

        public bool VisualizationEnabled
        {
            get => visualizationEnabled;
            set
            {
                visualizationEnabled = value;
                if (!visualizationEnabled) SetRenderersEnabled(false);
            }
        }

        private void Awake()
        {
            sensor = GetComponent<LidarSensorRig>();
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            CreateLinePool(sensor.RayCount);
#endif
        }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
        private void Update()
        {
            Keyboard keyboard = Keyboard.current;
            if (keyboard != null && keyboard.lKey.wasPressedThisFrame)
                VisualizationEnabled = !VisualizationEnabled;
        }
#endif

        private void OnEnable()
        {
            if (sensor == null) sensor = GetComponent<LidarSensorRig>();
            if (sensor != null) sensor.ObservationUpdated += DrawObservation;
        }

        private void OnDisable()
        {
            if (sensor != null) sensor.ObservationUpdated -= DrawObservation;
            SetRenderersEnabled(false);
        }

        private void OnDestroy()
        {
            DestroyLineMaterials();
        }

        private void DrawObservation(SensorObservation observation)
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            if (observation == null)
                return;

            EnsureLinePool(observation.RayCount);
            if (!visualizationEnabled || rayRenderers == null)
            {
                SetRenderersEnabled(false);
                return;
            }

            for (int rayIndex = 0; rayIndex < rayRenderers.Length; rayIndex++)
            {
                LineRenderer renderer = rayRenderers[rayIndex];
                if (rayIndex >= observation.RayCount)
                {
                    renderer.enabled = false;
                    continue;
                }

                LidarRaySample ray = observation.GetRay(rayIndex);
                Material material = MaterialFor(ray, observation);
                if (renderer.sharedMaterial != material)
                    renderer.sharedMaterial = material;
                renderer.startColor = Color.white;
                renderer.endColor = Color.white;
                renderer.startWidth = rayWidthM;
                renderer.endWidth = ray.HasHit ? hitWidthM : rayWidthM;
                renderer.SetPosition(0, ray.OriginWorld);
                renderer.SetPosition(1, ray.EndpointWorld);
                renderer.enabled = true;
            }
#endif
        }

        private void EnsureLinePool(int requiredCount)
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            if (rayRenderers != null && rayRenderers.Length == requiredCount)
                return;
            CreateLinePool(requiredCount);
#endif
        }

        private void CreateLinePool(int count)
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            if (lineRoot != null)
                Destroy(lineRoot.gameObject);
            DestroyLineMaterials();

            Shader shader = Shader.Find("Universal Render Pipeline/Unlit");
            if (shader == null) shader = Shader.Find("Unlit/Color");
            if (shader == null)
            {
                rayRenderers = null;
                Debug.LogWarning("LiDAR debug lines are unavailable: no compatible unlit shader was found.", this);
                return;
            }

            Color[] colors =
            {
                Color.green,
                Color.red,
                Color.yellow,
                Color.cyan,
                new Color(1f, 0.45f, 0.05f),
                Color.magenta
            };
            runtimeLineMaterials = new Material[colors.Length];
            for (int i = 0; i < colors.Length; i++)
            {
                Material material = new Material(shader)
                {
                    name = $"LiDAR Debug Lines {i} (Runtime)",
                    hideFlags = HideFlags.DontSave
                };
                if (material.HasProperty("_BaseColor"))
                    material.SetColor("_BaseColor", colors[i]);
                else if (material.HasProperty("_Color"))
                    material.SetColor("_Color", colors[i]);
                runtimeLineMaterials[i] = material;
            }

            GameObject root = new GameObject("LiDAR Debug Rays (Runtime)")
            {
                hideFlags = HideFlags.DontSave
            };
            root.transform.SetParent(transform, false);
            lineRoot = root.transform;
            rayRenderers = new LineRenderer[Mathf.Max(1, count)];
            for (int i = 0; i < rayRenderers.Length; i++)
            {
                GameObject lineObject = new GameObject($"Ray {i:000}")
                {
                    hideFlags = HideFlags.DontSave
                };
                lineObject.transform.SetParent(lineRoot, false);
                LineRenderer line = lineObject.AddComponent<LineRenderer>();
                line.useWorldSpace = true;
                line.positionCount = 2;
                line.sharedMaterial = runtimeLineMaterials[0];
                line.alignment = LineAlignment.View;
                line.shadowCastingMode = ShadowCastingMode.Off;
                line.receiveShadows = false;
                line.numCapVertices = 2;
                line.enabled = false;
                rayRenderers[i] = line;
            }
#endif
        }

        private void DestroyLineMaterials()
        {
            if (runtimeLineMaterials == null)
                return;
            for (int i = 0; i < runtimeLineMaterials.Length; i++)
                if (runtimeLineMaterials[i] != null)
                    Destroy(runtimeLineMaterials[i]);
            runtimeLineMaterials = null;
        }

        private Material MaterialFor(LidarRaySample ray, SensorObservation observation)
        {
            if (ray.IsOverflowed) return runtimeLineMaterials[5];
            if (!ray.HasHit) return runtimeLineMaterials[0];
            switch (observation.GetDetection(ray.DetectionIndex).EntityClass)
            {
                case LidarEntityClass.Pedestrian: return runtimeLineMaterials[2];
                case LidarEntityClass.Vehicle: return runtimeLineMaterials[3];
                case LidarEntityClass.StaticObstacle: return runtimeLineMaterials[4];
                default: return runtimeLineMaterials[1];
            }
        }

        private void SetRenderersEnabled(bool enabled)
        {
            if (rayRenderers == null)
                return;
            for (int i = 0; i < rayRenderers.Length; i++)
                if (rayRenderers[i] != null)
                    rayRenderers[i].enabled = enabled;
        }

    }
}
