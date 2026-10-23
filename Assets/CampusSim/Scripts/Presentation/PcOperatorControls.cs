using System.Collections.Generic;
using Unity.Cinemachine;
using Unity.Cinemachine.TargetTracking;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace InhaExpress.Client.Presentation
{
    /// <summary>PC camera navigation only. Never sends drive commands or writes a vehicle Transform.</summary>
    [DefaultExecutionOrder(-200)]
    public sealed class PcOperatorControls : MonoBehaviour
    {
        private readonly Dictionary<string, IOperatorVehicle> vehicles = new Dictionary<string, IOperatorVehicle>();
        private Camera output;
        private CinemachineBrain brain;
        private CinemachineCamera followCamera;
        private CinemachineFollow follow;
        private Transform stabilizedTarget;
        private float heightVelocity;
        private Vector3 mapCenter, initialCenter;
        private float mapYaw, mapPitch = 65, fit, mapZoom = 1, followDistance = 8;
        private Bounds bounds;
        private bool initialized;
        private bool panGesture, rotateGesture;
        private float originalNearClip, originalFarClip, originalFov;
        public IOperatorVehicle SelectedVehicle { get; private set; }
        public bool IsFollowing { get; private set; }
        public string SelectionMessage { get; private set; } = "차량을 선택하면 추적합니다";
        public CinemachineCamera FollowCamera => followCamera;
        public IEnumerable<IOperatorVehicle> Vehicles => vehicles.Values;

        public void Initialize(Camera camera, Bounds mapBounds, float fittedSize)
        {
            output = camera;
            originalNearClip = output.nearClipPlane;
            originalFarClip = output.farClipPlane;
            originalFov = output.fieldOfView;
            bounds = mapBounds;
            initialCenter = mapCenter = bounds.center;
            fit = fittedSize;
            // Only controllers in the loaded simulation world participate. IDs are explicit, never array order.
            foreach (var component in FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None))
                if (component is IOperatorVehicle vehicle)
                {
                    if (string.IsNullOrWhiteSpace(vehicle.OperatorVehicleId) || vehicles.ContainsKey(vehicle.OperatorVehicleId))
                    {
                        Debug.LogError("Duplicate/empty operator vehicle ID: " + vehicle.OperatorVehicleId);
                        continue;
                    }
                    vehicles.Add(vehicle.OperatorVehicleId, vehicle);
                }
            brain = output.gameObject.AddComponent<CinemachineBrain>();
            brain.enabled = false;
            brain.UpdateMethod = CinemachineBrain.UpdateMethods.LateUpdate;
            brain.DefaultBlend = new CinemachineBlendDefinition(CinemachineBlendDefinition.Styles.Cut, 0);
            brain.LensModeOverride = new CinemachineBrain.LensModeOverrideSettings
                { Enabled = true, DefaultMode = LensSettings.OverrideModes.Orthographic };
            var rig = new GameObject("PC selected vehicle Cinemachine");
            rig.transform.SetParent(transform, false);
            rig.SetActive(false);
            followCamera = rig.AddComponent<CinemachineCamera>();
            followCamera.Lens.FieldOfView = 55;
            followCamera.Lens.NearClipPlane = .1f;
            followCamera.Lens.FarClipPlane = 2000;
            followCamera.Lens.ModeOverride = LensSettings.OverrideModes.Perspective;
            follow = rig.AddComponent<CinemachineFollow>();
            follow.TrackerSettings.BindingMode = BindingMode.LockToTargetWithWorldUp;
            follow.TrackerSettings.PositionDamping = new Vector3(.2f, .3f, .2f);
            var aim = rig.AddComponent<CinemachineHardLookAt>();
            aim.LookAtOffset = Vector3.up * .6f;
            stabilizedTarget = new GameObject("PC stabilized camera target").transform;
            stabilizedTarget.SetParent(transform, false);
            UpdateFollowDistance();
            initialized = true;
            ApplyMapPose();
        }

        public bool HasVehicle(string id) => id != null && vehicles.TryGetValue(id, out var v) &&
            v is Component component && component != null && v.VehicleTransform != null;

        public bool SelectVehicle(string id)
        {
            if (!HasVehicle(id))
            {
                SelectionMessage = id + " · Unity 차량 미연결";
                return false;
            }
            SelectedVehicle = vehicles[id];
            UpdateStabilizedTarget(true);
            followCamera.Follow = stabilizedTarget;
            followCamera.LookAt = stabilizedTarget;
            followCamera.PreviousStateIsValid = false;
            followCamera.gameObject.SetActive(true);
            IsFollowing = true;
            brain.enabled = true;
            SelectionMessage = id + " 추적 중";
            Debug.Log("CAMPUS_PC_VEHICLE_SELECTED id=" + id + " target=" + SelectedVehicle.VehicleTransform.name);
            return true;
        }

        public void ReleaseFollow()
        {
            if (!initialized || !IsFollowing) return;
            mapCenter = SelectedVehicle.VehicleTransform.position;
            mapYaw = output.transform.eulerAngles.y;
            mapPitch = Mathf.Clamp(output.transform.eulerAngles.x, 25, 85);
            mapZoom = Mathf.Clamp(Mathf.Max(20, followDistance * 2) / fit, 20 / fit, 2);
            ShowMap();
            SelectionMessage = "추적 해제 · 자유 지도";
        }

        public void ShowMap(bool reset = false)
        {
            if (!initialized || output == null) return;
            IsFollowing = false;
            brain.enabled = false;
            followCamera.gameObject.SetActive(false);
            followCamera.Follow = null;
            followCamera.LookAt = null;
            panGesture = rotateGesture = false;
            if (reset) { mapCenter = initialCenter; mapYaw = 0; mapPitch = 65; mapZoom = 1; }
            ApplyMapPose();
            SelectionMessage = "운영 지도 · 드래그 이동 / 우클릭 회전 / 휠 확대";
        }

        public void Resize(float fittedSize)
        {
            fit = fittedSize;
            if (!IsFollowing) ApplyMapPose();
        }

        public void Zoom(float factor)
        {
            if (!initialized || !float.IsFinite(factor) || factor <= 0) return;
            if (IsFollowing) { followDistance = Mathf.Clamp(followDistance * factor, 3, 40); UpdateFollowDistance(); }
            else { mapZoom = Mathf.Clamp(mapZoom * factor, 20 / fit, 2); ApplyMapPose(); }
        }

        public void Pan(Vector2 pixels)
        {
            if (!initialized || IsFollowing) return;
            var rotation = Quaternion.Euler(0, mapYaw, 0);
            float units = 2 * output.orthographicSize / Mathf.Max(1, output.pixelHeight);
            mapCenter -= rotation * new Vector3(pixels.x, 0, pixels.y / Mathf.Sin(mapPitch * Mathf.Deg2Rad)) * units;
            float limit = Mathf.Max(bounds.size.x, bounds.size.z);
            mapCenter.x = Mathf.Clamp(mapCenter.x, initialCenter.x - limit, initialCenter.x + limit);
            mapCenter.z = Mathf.Clamp(mapCenter.z, initialCenter.z - limit, initialCenter.z + limit);
            ApplyMapPose();
        }

        public void Rotate(Vector2 degrees)
        {
            if (!initialized || IsFollowing) return;
            mapYaw = Mathf.Repeat(mapYaw + degrees.x, 360);
            mapPitch = Mathf.Clamp(mapPitch - degrees.y, 25, 85);
            ApplyMapPose();
        }

        private void ApplyMapPose()
        {
            output.orthographic = true;
            output.nearClipPlane = originalNearClip;
            output.farClipPlane = originalFarClip;
            output.fieldOfView = originalFov;
            output.orthographicSize = fit * mapZoom;
            var rotation = Quaternion.Euler(mapPitch, mapYaw, 0);
            output.transform.SetPositionAndRotation(mapCenter - rotation * Vector3.forward * 1000, rotation);
        }

        private void UpdateFollowDistance() => follow.FollowOffset = new Vector3(0, followDistance * .45f, -followDistance);

        // Presentation only: preserve physical vehicle pose for localization and sensors.
        // Run before the Cinemachine brain, after physics interpolation has updated the target.
        private void LateUpdate()
        {
            if (IsFollowing && SelectedVehicle is Component component && component != null &&
                SelectedVehicle.VehicleTransform != null) UpdateStabilizedTarget(false);
        }

        private void UpdateStabilizedTarget(bool reset)
        {
            var target = SelectedVehicle.VehicleTransform;
            var position = target.position;
            reset |= (position - stabilizedTarget.position).sqrMagnitude > 400;
            if (reset) heightVelocity = 0;
            else position.y = Mathf.SmoothDamp(stabilizedTarget.position.y, position.y,
                ref heightVelocity, .45f, Mathf.Infinity, Time.deltaTime);
            var heading = Vector3.ProjectOnPlane(target.forward, Vector3.up);
            var rotation = heading.sqrMagnitude > .0001f
                ? Quaternion.LookRotation(heading, Vector3.up) : stabilizedTarget.rotation;
            if (!reset) rotation = Quaternion.Slerp(stabilizedTarget.rotation, rotation,
                1 - Mathf.Exp(-Time.deltaTime / .18f));
            stabilizedTarget.SetPositionAndRotation(position, rotation);
        }

        private void Update()
        {
            if (!initialized || output == null) return;
            if (SelectedVehicle != null && (SelectedVehicle is Component selected && selected == null || SelectedVehicle.VehicleTransform == null))
            { ShowMap(); SelectedVehicle = null; }
            var keyboard = Keyboard.current;
            if (keyboard != null && keyboard.escapeKey.wasPressedThisFrame) { ReleaseFollow(); return; }
            bool typing = EventSystem.current != null && EventSystem.current.currentSelectedGameObject != null &&
                EventSystem.current.currentSelectedGameObject.GetComponent<InputField>() != null;
            var mouse = Mouse.current;
            if (mouse == null || !Application.isFocused || typing) return;
            bool allowed = output.pixelRect.Contains(mouse.position.ReadValue()) &&
                (EventSystem.current == null || !EventSystem.current.IsPointerOverGameObject());
            if (mouse.leftButton.wasReleasedThisFrame || mouse.middleButton.wasReleasedThisFrame) panGesture = false;
            if (mouse.rightButton.wasReleasedThisFrame) rotateGesture = false;
            if (mouse.leftButton.wasPressedThisFrame || mouse.middleButton.wasPressedThisFrame) panGesture = allowed;
            if (mouse.rightButton.wasPressedThisFrame) rotateGesture = allowed;
            if (!allowed) return;
            float wheel = mouse.scroll.ReadValue().y;
            if (wheel != 0) Zoom(Mathf.Exp(-wheel * .0015f));
            var delta = mouse.delta.ReadValue();
            if (rotateGesture && mouse.rightButton.isPressed) Rotate(delta * .2f);
            else if (panGesture && (mouse.leftButton.isPressed || mouse.middleButton.isPressed)) Pan(delta);
        }

        private void OnApplicationFocus(bool focused) { if (!focused) panGesture = rotateGesture = false; }
        private void OnDisable() { if (brain != null) brain.enabled = false; if (followCamera != null) followCamera.gameObject.SetActive(false); }
        private void OnDestroy()
        {
            if (brain != null) Destroy(brain);
            if (followCamera != null) Destroy(followCamera.gameObject);
            if (stabilizedTarget != null) Destroy(stabilizedTarget.gameObject);
        }
    }
}
