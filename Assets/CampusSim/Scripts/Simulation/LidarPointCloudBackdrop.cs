using System.Collections.Generic;
using InhaExpress.Client.Presentation;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace InhaExpress.Client.Simulation
{
    /// <summary>Camera-scoped LiDAR feedback. Never hides geometry or changes sensor/physics state.</summary>
    public sealed class LidarPointCloudBackdrop : MonoBehaviour
    {
        private static LidarPointCloudBackdrop instance;
        private readonly HashSet<LidarPointCloudVisualizer> owners = new HashSet<LidarPointCloudVisualizer>();
        private Volume volume;
        private VolumeProfile profile;
        private Camera renderingCamera;
        private UniversalAdditionalCameraData cameraData;
        private bool previousPostProcessing;
        private LayerMask previousVolumeMask;

        public static void Acquire(LidarPointCloudVisualizer owner)
        {
            if (instance == null)
            {
                var host = new GameObject("PC LiDAR Monochrome (Runtime)") { hideFlags = HideFlags.DontSave };
                instance = host.AddComponent<LidarPointCloudBackdrop>();
            }
            instance.owners.Add(owner);
        }

        public static void Release(LidarPointCloudVisualizer owner)
        {
            if (instance == null) return;
            instance.owners.Remove(owner);
            if (instance.owners.Count != 0) return;
            var previous = instance;
            instance = null;
            previous.RestoreCamera();
            // Unsubscribe immediately: Destroy is deferred until the end of the frame.
            previous.enabled = false;
            Destroy(previous.gameObject);
        }

        private void Awake()
        {
            profile = ScriptableObject.CreateInstance<VolumeProfile>();
            profile.hideFlags = HideFlags.DontSave;
            profile.Add<ColorAdjustments>().saturation.Override(-100f);
            volume = gameObject.AddComponent<Volume>();
            volume.isGlobal = true;
            volume.priority = float.MaxValue;
            volume.sharedProfile = profile;
            volume.enabled = false;
        }

        private void OnEnable()
        {
            RenderPipelineManager.beginCameraRendering += BeginCamera;
            RenderPipelineManager.endCameraRendering += EndCamera;
        }

        public bool IsMonochromeFor(Camera camera)
        {
            if (camera == null || camera.cameraType != CameraType.Game) return false;
            foreach (var controls in FindObjectsByType<PcOperatorControls>(FindObjectsSortMode.None))
            {
                if (!controls.isActiveAndEnabled || controls.OutputCamera != camera || !controls.IsFollowing ||
                    controls.SelectedVehicle == null) continue;
                var target = controls.SelectedVehicle.VehicleTransform;
                if (target == null) continue;
                foreach (var owner in owners)
                    if (owner != null && owner.isActiveAndEnabled && owner.VisualizationEnabled &&
                        owner.transform.IsChildOf(target)) return true;
            }
            return false;
        }

        private void BeginCamera(ScriptableRenderContext context, Camera camera)
        {
            RestoreCamera();
            if (!IsMonochromeFor(camera)) return;
            renderingCamera = camera;
            cameraData = camera.GetUniversalAdditionalCameraData();
            previousPostProcessing = cameraData.renderPostProcessing;
            previousVolumeMask = cameraData.volumeLayerMask;
            cameraData.renderPostProcessing = true;
            cameraData.volumeLayerMask = previousVolumeMask.value | (1 << gameObject.layer);
            // URP updates the volume stack after beginCameraRendering. For cameras
            // configured for manual updates, refresh their cached stack explicitly.
            volume.enabled = true;
            if (!cameraData.requiresVolumeFrameworkUpdate) camera.UpdateVolumeStack();
        }

        private void EndCamera(ScriptableRenderContext context, Camera camera)
        {
            if (camera == renderingCamera) RestoreCamera();
        }

        private void RestoreCamera()
        {
            if (volume != null) volume.enabled = false;
            if (cameraData != null)
            {
                cameraData.renderPostProcessing = previousPostProcessing;
                cameraData.volumeLayerMask = previousVolumeMask;
                if (renderingCamera != null && !cameraData.requiresVolumeFrameworkUpdate)
                    renderingCamera.UpdateVolumeStack();
            }
            renderingCamera = null;
            cameraData = null;
        }

        private void OnDisable()
        {
            RenderPipelineManager.beginCameraRendering -= BeginCamera;
            RenderPipelineManager.endCameraRendering -= EndCamera;
            RestoreCamera();
        }

        private void OnDestroy()
        {
            if (profile != null) Destroy(profile);
            if (instance == this) instance = null;
        }
    }
}
