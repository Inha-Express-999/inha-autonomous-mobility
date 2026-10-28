using UnityEngine;
using UnityEngine.Rendering;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
using UnityEngine.InputSystem;
#endif

namespace InhaExpress.Client.Simulation
{
    /// <summary>
    /// Accumulates actual LiDAR hit endpoints as a short-lived world-space point cloud.
    /// The points are visualization only and are never sent back to planning.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(LidarSensorRig))]
    public sealed class LidarPointCloudVisualizer : MonoBehaviour
    {
        [SerializeField] private bool visualizationEnabled;
        [SerializeField, Min(0.005f)] private float pointSizeM = 0.07f;
        [SerializeField, Min(0.1f)] private float pointLifetimeSeconds = 1.8f;
        [SerializeField] private Color pointColor = new Color(1f, 0.68f, 0.08f, 1f);
        [SerializeField] private Color objectPointColor = new Color(1f, 0.05f, 0.05f, 1f);
        [SerializeField, Min(64)] private int maxPointCount = 8192;

        private bool pcOverlay;
        private LidarSensorRig sensor;

        public void ConfigurePcOverlay()
        {
            RestoreSceneView();
            pcOverlay = true;
            pointLifetimeSeconds = .25f;
            VisualizationEnabled = false;
        }
        private ParticleSystem pointCloud;
        private Material pointMaterial;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        private Renderer[] isolatedRenderers;
        private bool[] rendererEnabledBeforeIsolation;
        private Canvas[] isolatedCanvases;
        private bool[] canvasEnabledBeforeIsolation;
        private Camera isolatedCamera;
        private CameraClearFlags cameraClearFlagsBeforeIsolation;
        private Color cameraBackgroundBeforeIsolation;
        private LidarDebugVisualizer rayVisualizer;
        private bool rayVisualizerEnabledBeforeIsolation;
        private bool rayVisualizationBeforeIsolation;
        private bool isolatedViewActive;
#endif

        public bool VisualizationEnabled
        {
            get => visualizationEnabled;
            set
            {
                if (visualizationEnabled == value)
                    return;

                visualizationEnabled = value;
                if (!visualizationEnabled && pointCloud != null)
                    pointCloud.Clear(true);
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                if (visualizationEnabled && !pcOverlay)
                    EnterIsolatedView();
                else
                    RestoreSceneView();
#endif
            }
        }

        private void Awake()
        {
            sensor = GetComponent<LidarSensorRig>();
            CreatePointCloud();
        }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
        private void Update()
        {
            Keyboard keyboard = Keyboard.current;
            if (!pcOverlay && keyboard != null && keyboard.pKey.wasPressedThisFrame)
                VisualizationEnabled = !VisualizationEnabled;
        }
#endif

        private void OnEnable()
        {
            if (sensor == null)
                sensor = GetComponent<LidarSensorRig>();
            if (sensor != null)
                sensor.ObservationUpdated += AddObservationPoints;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            if (visualizationEnabled && !pcOverlay)
                EnterIsolatedView();
#endif
        }

        private void OnDisable()
        {
            if (sensor != null)
                sensor.ObservationUpdated -= AddObservationPoints;
            if (pointCloud != null)
                pointCloud.Clear(true);
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            RestoreSceneView();
#endif
        }

        private void OnDestroy()
        {
            if (pointMaterial != null)
                Destroy(pointMaterial);
        }

        private void AddObservationPoints(SensorObservation observation)
        {
            if (!visualizationEnabled || pointCloud == null || observation == null)
                return;

            for (int rayIndex = 0; rayIndex < observation.RayCount; rayIndex += pcOverlay ? 5 : 1)
            {
                LidarRaySample ray = observation.GetRay(rayIndex);
                if (!ray.HasVisualReturn)
                    continue;

                ParticleSystem.EmitParams point = new ParticleSystem.EmitParams
                {
                    position = ray.EndpointWorld,
                    velocity = Vector3.zero,
                    startColor = ray.HasHit ? objectPointColor : pointColor,
                    startSize = pointSizeM,
                    startLifetime = pointLifetimeSeconds,
                    applyShapeToPosition = false
                };
                pointCloud.Emit(point, 1);
            }
        }

        private void EnterIsolatedView()
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            if (isolatedViewActive)
                return;

            isolatedViewActive = true;
            rayVisualizer = GetComponent<LidarDebugVisualizer>();
            if (rayVisualizer != null)
            {
                rayVisualizerEnabledBeforeIsolation = rayVisualizer.enabled;
                rayVisualizationBeforeIsolation = rayVisualizer.VisualizationEnabled;
                rayVisualizer.enabled = false;
            }

            Transform vehicleRoot = sensor != null ? sensor.transform.root : transform.root;
            Renderer pointCloudRenderer = pointCloud != null ? pointCloud.GetComponent<Renderer>() : null;
            isolatedRenderers = FindObjectsByType<Renderer>(FindObjectsSortMode.None);
            rendererEnabledBeforeIsolation = new bool[isolatedRenderers.Length];
            for (int i = 0; i < isolatedRenderers.Length; i++)
            {
                Renderer sceneRenderer = isolatedRenderers[i];
                if (sceneRenderer == null)
                    continue;

                bool wasEnabled = sceneRenderer.enabled;
                rendererEnabledBeforeIsolation[i] = wasEnabled;
                bool belongsToVehicle = sceneRenderer.transform == vehicleRoot ||
                                        sceneRenderer.transform.IsChildOf(vehicleRoot);
                bool isPointCloud = sceneRenderer == pointCloudRenderer;
                sceneRenderer.enabled = wasEnabled && (belongsToVehicle || isPointCloud);
            }

            isolatedCanvases = FindObjectsByType<Canvas>(FindObjectsSortMode.None);
            canvasEnabledBeforeIsolation = new bool[isolatedCanvases.Length];
            for (int i = 0; i < isolatedCanvases.Length; i++)
            {
                Canvas canvas = isolatedCanvases[i];
                if (canvas == null)
                    continue;

                canvasEnabledBeforeIsolation[i] = canvas.enabled;
                canvas.enabled = false;
            }

            isolatedCamera = Camera.main;
            if (isolatedCamera != null)
            {
                cameraClearFlagsBeforeIsolation = isolatedCamera.clearFlags;
                cameraBackgroundBeforeIsolation = isolatedCamera.backgroundColor;
                isolatedCamera.clearFlags = CameraClearFlags.SolidColor;
                isolatedCamera.backgroundColor = Color.black;
            }
#endif
        }

        private void RestoreSceneView()
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            if (!isolatedViewActive)
                return;

            if (isolatedRenderers != null && rendererEnabledBeforeIsolation != null)
            {
                int count = Mathf.Min(isolatedRenderers.Length, rendererEnabledBeforeIsolation.Length);
                for (int i = 0; i < count; i++)
                    if (isolatedRenderers[i] != null)
                        isolatedRenderers[i].enabled = rendererEnabledBeforeIsolation[i];
            }

            if (isolatedCanvases != null && canvasEnabledBeforeIsolation != null)
            {
                int count = Mathf.Min(isolatedCanvases.Length, canvasEnabledBeforeIsolation.Length);
                for (int i = 0; i < count; i++)
                    if (isolatedCanvases[i] != null)
                        isolatedCanvases[i].enabled = canvasEnabledBeforeIsolation[i];
            }

            if (isolatedCamera != null)
            {
                isolatedCamera.clearFlags = cameraClearFlagsBeforeIsolation;
                isolatedCamera.backgroundColor = cameraBackgroundBeforeIsolation;
            }

            if (rayVisualizer != null)
            {
                rayVisualizer.VisualizationEnabled = rayVisualizationBeforeIsolation;
                rayVisualizer.enabled = rayVisualizerEnabledBeforeIsolation;
            }

            isolatedRenderers = null;
            rendererEnabledBeforeIsolation = null;
            isolatedCanvases = null;
            canvasEnabledBeforeIsolation = null;
            isolatedCamera = null;
            rayVisualizer = null;
            isolatedViewActive = false;
#endif
        }

        private void CreatePointCloud()
        {
            GameObject cloudObject = new GameObject("LiDAR Detected Point Cloud (Runtime)")
            {
                hideFlags = HideFlags.DontSave
            };
            cloudObject.transform.SetParent(transform, false);

            pointCloud = cloudObject.AddComponent<ParticleSystem>();
            ParticleSystem.MainModule main = pointCloud.main;
            main.loop = true;
            main.playOnAwake = false;
            main.startSpeed = 0f;
            main.startSize = pointSizeM;
            main.startLifetime = pointLifetimeSeconds;
            main.startColor = Color.white;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = Mathf.Clamp(maxPointCount, 64, 65536);

            ParticleSystem.EmissionModule emission = pointCloud.emission;
            emission.enabled = false;
            ParticleSystem.ShapeModule shape = pointCloud.shape;
            shape.enabled = false;

            ParticleSystemRenderer cloudRenderer = cloudObject.GetComponent<ParticleSystemRenderer>();
            cloudRenderer.renderMode = ParticleSystemRenderMode.Billboard;
            cloudRenderer.alignment = ParticleSystemRenderSpace.View;
            cloudRenderer.shadowCastingMode = ShadowCastingMode.Off;
            cloudRenderer.receiveShadows = false;
            pointMaterial = CreatePointMaterial();
            cloudRenderer.sharedMaterial = pointMaterial;
            pointCloud.Play(true);
        }

        private Material CreatePointMaterial()
        {
            Shader shader = Shader.Find("Universal Render Pipeline/Particles/Unlit");
            if (shader == null) shader = Shader.Find("Particles/Standard Unlit");
            if (shader == null) shader = Shader.Find("Universal Render Pipeline/Unlit");
            if (shader == null) shader = Shader.Find("Unlit/Color");
            if (shader == null)
            {
                Debug.LogWarning("LiDAR point cloud is unavailable: no compatible unlit shader was found.", this);
                return null;
            }

            Material material = new Material(shader)
            {
                name = "LiDAR Point Cloud (Runtime)",
                hideFlags = HideFlags.DontSave
            };
            if (material.HasProperty("_BaseColor"))
                material.SetColor("_BaseColor", Color.white);
            else if (material.HasProperty("_Color"))
                material.SetColor("_Color", Color.white);

            return material;
        }

        private void OnValidate()
        {
            pointSizeM = IsFinite(pointSizeM) ? Mathf.Max(0.005f, pointSizeM) : 0.07f;
            pointLifetimeSeconds = IsFinite(pointLifetimeSeconds)
                ? Mathf.Max(0.1f, pointLifetimeSeconds)
                : 1.8f;
            maxPointCount = Mathf.Clamp(maxPointCount, 64, 65536);
        }

        private static bool IsFinite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
    }
}
