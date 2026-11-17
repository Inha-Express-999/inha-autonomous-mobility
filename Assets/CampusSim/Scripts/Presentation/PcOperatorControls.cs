using System.Collections.Generic;
using Unity.Cinemachine;
using Unity.Cinemachine.TargetTracking;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using UnityEngine.Rendering;
using InhaExpress.Client.MapClient;

namespace InhaExpress.Client.Presentation
{
    /// <summary>PC camera navigation only. Never sends drive commands or writes a vehicle Transform.</summary>
    [DefaultExecutionOrder(-200)]
    public sealed class PcOperatorControls : MonoBehaviour
    {
        private readonly Dictionary<string, IOperatorVehicle> vehicles = new Dictionary<string, IOperatorVehicle>();
        private Camera output;
        private CinemachineBrain brain;
        private bool ownsBrain, originalBrainEnabled;
        private CinemachineBrain.UpdateMethods originalBrainUpdate;
        private CinemachineBlendDefinition originalBlend;
        private CinemachineBrain.LensModeOverrideSettings originalLensMode;
        private CinemachineCamera followCamera;
        private CinemachineCamera mapCamera;
        public bool InputEnabled { get; set; } = true;
        private float initialPitch = 65, initialYaw;
        private CinemachineFollow follow;
        private Transform stabilizedTarget;
        private float heightVelocity;
        private Vector3 mapCenter, initialCenter;
        private float mapYaw, mapPitch = 65, fit, mapZoom = 1, followDistance = 8;
        private Bounds bounds;
        private bool initialized;
        private bool panGesture, rotateGesture;
        private Vector3 trackedPoint, trackVelocity;
        private float trackedViewSize, trackZoomVelocity;
        private const float TrackSmoothTime = .6f;
        /// <summary>True while the map view eases toward a reported world point.</summary>
        public bool IsTrackingPoint { get; private set; }
        private float originalNearClip, originalFarClip, originalFov;
        private readonly List<Renderer> garageDisplays = new List<Renderer>();
        private readonly Dictionary<Renderer, bool> hiddenGarageDisplays = new Dictionary<Renderer, bool>();
        public IOperatorVehicle SelectedVehicle { get; private set; }
        public bool IsFollowing { get; private set; }
        public Camera OutputCamera => output;
        public string SelectionMessage { get; private set; } = "차량을 선택하면 추적합니다";
        public CinemachineCamera FollowCamera => followCamera;
        public IEnumerable<IOperatorVehicle> Vehicles => vehicles.Values;

        // "전체" view: one chase camera per vehicle, side by side inside the map viewport.
        // Presentation only; the selection/follow state underneath is left untouched.
        private sealed class FleetPane
        {
            public IOperatorVehicle vehicle;
            public Camera camera;
            public Vector3 position;
            public Quaternion rotation = Quaternion.identity;
            public float heightVelocity;
        }
        private const int MaxFleetPanes = 3;
        private readonly List<FleetPane> fleetPanes = new List<FleetPane>();
        private float fleetDistance = 8;
        private bool outputEnabledBeforeFleet;
        public bool FleetView { get; private set; }
        public int FleetPaneCount => fleetPanes.Count;
        public IOperatorVehicle FleetPaneVehicle(int index) =>
            index >= 0 && index < fleetPanes.Count ? fleetPanes[index].vehicle : null;
        public Camera FleetPaneCamera(int index) =>
            index >= 0 && index < fleetPanes.Count ? fleetPanes[index].camera : null;

        public void Initialize(Camera camera, Bounds mapBounds, float fittedSize, float pitch = 65, float yaw = 0)
        {
            output = camera;
            originalNearClip = output.nearClipPlane;
            originalFarClip = output.farClipPlane;
            originalFov = output.fieldOfView;
            bounds = mapBounds;
            initialCenter = mapCenter = bounds.center;
            fit = fittedSize;
            initialPitch = mapPitch = pitch;
            initialYaw = mapYaw = yaw;
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
            foreach (var model in FindObjectsByType<ServicePointVisual>(FindObjectsSortMode.None))
                if (model.pointKind == ServerMapMarker.MarkerKind.Hub)
                    garageDisplays.AddRange(model.GetComponentsInChildren<Renderer>(true));
            foreach (var map in FindObjectsByType<CampusServerMap>(FindObjectsSortMode.None))
            {
                var markers = map.transform.Find("Hub visual markers");
                if (markers != null) garageDisplays.AddRange(markers.GetComponentsInChildren<Renderer>(true));
            }
            brain = output.GetComponent<CinemachineBrain>();
            ownsBrain = brain == null;
            if (ownsBrain) brain = output.gameObject.AddComponent<CinemachineBrain>();
            originalBrainEnabled = brain.enabled;
            originalBrainUpdate = brain.UpdateMethod;
            originalBlend = brain.DefaultBlend;
            originalLensMode = brain.LensModeOverride;
            brain.enabled = true;
            brain.UpdateMethod = CinemachineBrain.UpdateMethods.LateUpdate;
            brain.DefaultBlend = new CinemachineBlendDefinition(CinemachineBlendDefinition.Styles.Cut, 0);
            brain.LensModeOverride = new CinemachineBrain.LensModeOverrideSettings
                { Enabled = true, DefaultMode = LensSettings.OverrideModes.Orthographic };
            var mapRig = new GameObject("Campus map Cinemachine");
            mapRig.transform.SetParent(transform, false);
            mapCamera = mapRig.AddComponent<CinemachineCamera>();
            mapCamera.Priority = 100;
            mapCamera.Lens.ModeOverride = LensSettings.OverrideModes.Orthographic;
            mapCamera.Lens.NearClipPlane = originalNearClip;
            // The map rig sits 1 km from its center. A close-up scene view may
            // leave the shared output at a 1 km far clip; retain campus coverage.
            mapCamera.Lens.FarClipPlane = Mathf.Max(originalFarClip, 2500);
            var rig = new GameObject("PC selected vehicle Cinemachine");
            rig.transform.SetParent(transform, false);
            rig.SetActive(false);
            followCamera = rig.AddComponent<CinemachineCamera>();
            followCamera.Priority = 100;
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
            StopTrackingPoint();
            UpdateStabilizedTarget(true);
            followCamera.Follow = stabilizedTarget;
            followCamera.LookAt = stabilizedTarget;
            followCamera.PreviousStateIsValid = false;
            followCamera.gameObject.SetActive(true);
            mapCamera.gameObject.SetActive(false);
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

        public void DeselectVehicle()
        {
            SelectedVehicle = null;
            ShowMap(true);
        }

        public void ShowMap(bool reset = false)
        {
            if (!initialized || output == null) return;
            IsFollowing = false;
            brain.enabled = true;
            mapCamera.gameObject.SetActive(true);
            followCamera.gameObject.SetActive(false);
            followCamera.Follow = null;
            followCamera.LookAt = null;
            panGesture = rotateGesture = false;
            StopTrackingPoint();
            if (reset) { mapCenter = initialCenter; mapYaw = initialYaw; mapPitch = initialPitch; mapZoom = 1; }
            ApplyMapPose();
            SelectionMessage = "운영 지도 · 드래그 이동 / 우클릭 회전 / 휠 확대";
        }

        /// <summary>
        /// Eases the map view onto a reported world position and keeps it centered as the caller
        /// reports new positions. Presentation only: no vehicle Transform is read or written.
        /// </summary>
        /// <param name="viewHalfHeight">Orthographic half-height in meters while focused.</param>
        public void TrackMapPoint(Vector3 point, float viewHalfHeight)
        {
            if (!initialized || output == null || !float.IsFinite(point.x) || !float.IsFinite(point.y) ||
                !float.IsFinite(point.z) || !float.IsFinite(viewHalfHeight) || viewHalfHeight <= 0) return;
            if (IsFollowing) ShowMap();
            trackedPoint = ClampToMap(point);
            trackedViewSize = viewHalfHeight;
            IsTrackingPoint = true;
        }

        public void StopTrackingPoint()
        {
            IsTrackingPoint = false;
            trackVelocity = Vector3.zero;
            trackZoomVelocity = 0;
        }

        public void Resize(float fittedSize)
        {
            fit = fittedSize;
            if (!IsFollowing) ApplyMapPose();
        }

        /// <summary>Switches between the single map/follow view and the all-vehicle camera wall.</summary>
        public bool SetFleetView(bool on)
        {
            if (!initialized || output == null || on == FleetView) return FleetView;
            if (!on)
            {
                FleetView = false;
                foreach (var pane in fleetPanes)
                    if (pane.camera != null)
                    {
                        pane.camera.enabled = false;
                        VegetationDisplaySettings.ExtraViewpoints.Remove(pane.camera.transform);
                    }
                output.enabled = outputEnabledBeforeFleet;
                return false;
            }
            if (fleetPanes.Count == 0) BuildFleetPanes();
            if (fleetPanes.Count == 0) return false;
            outputEnabledBeforeFleet = output.enabled;
            output.enabled = false;
            FleetView = true;
            panGesture = rotateGesture = false;
            foreach (var pane in fleetPanes)
            {
                pane.camera.enabled = true;
                if (!VegetationDisplaySettings.ExtraViewpoints.Contains(pane.camera.transform))
                    VegetationDisplaySettings.ExtraViewpoints.Add(pane.camera.transform);
            }
            UpdateFleetPanes(true);
            return true;
        }

        private void BuildFleetPanes()
        {
            var ids = new List<string>(vehicles.Keys);
            ids.Sort(string.CompareOrdinal);
            foreach (var id in ids)
            {
                if (fleetPanes.Count >= MaxFleetPanes) break;
                if (!HasVehicle(id)) continue;
                var rig = new GameObject("PC fleet view " + id);
                rig.transform.SetParent(transform, false);
                var camera = rig.AddComponent<Camera>();
                // Same layers, clear and HDR/MSAA as the map output; lens matches the follow view.
                camera.CopyFrom(output);
                camera.targetTexture = null;
                camera.orthographic = false;
                camera.fieldOfView = 55;
                camera.nearClipPlane = .1f;
                camera.farClipPlane = 2000;
                camera.ResetAspect();
                camera.enabled = false;
                fleetPanes.Add(new FleetPane { vehicle = vehicles[id], camera = camera });
            }
        }

        private void UpdateFleetPanes(bool reset)
        {
            var area = output.rect;
            float width = area.width / fleetPanes.Count;
            for (int i = 0; i < fleetPanes.Count; i++)
            {
                var pane = fleetPanes[i];
                if (pane.camera == null) continue;
                var rect = new Rect(area.x + width * i, area.y, width, area.height);
                if (pane.camera.rect != rect) pane.camera.rect = rect;
                // A destroyed vehicle keeps its last view rather than leaving the pane uncleared.
                if (pane.vehicle is Component component && component == null || pane.vehicle.VehicleTransform == null) continue;
                Stabilize(pane.vehicle.VehicleTransform, ref pane.position, ref pane.rotation, ref pane.heightVelocity, reset);
                var eye = pane.position + pane.rotation * new Vector3(0, fleetDistance * .45f, -fleetDistance);
                var aim = pane.position + Vector3.up * .6f - eye;
                pane.camera.transform.SetPositionAndRotation(eye, Quaternion.LookRotation(aim, Vector3.up));
            }
        }

        private bool IsFleetCamera(Camera camera)
        {
            foreach (var pane in fleetPanes) if (pane.camera == camera) return true;
            return false;
        }

        public void Zoom(float factor)
        {
            if (!initialized || !float.IsFinite(factor) || factor <= 0) return;
            if (FleetView) { fleetDistance = Mathf.Clamp(fleetDistance * factor, 3, 40); return; }
            if (IsFollowing) { followDistance = Mathf.Clamp(followDistance * factor, 3, 40); UpdateFollowDistance(); }
            else if (IsTrackingPoint) trackedViewSize = Mathf.Clamp(trackedViewSize * factor, 20, fit * 2);
            else { mapZoom = Mathf.Clamp(mapZoom * factor, 20 / fit, 2); ApplyMapPose(); }
        }

        public void Pan(Vector2 pixels)
        {
            if (!initialized || IsFollowing) return;
            StopTrackingPoint();
            var rotation = Quaternion.Euler(0, mapYaw, 0);
            float units = 2 * output.orthographicSize / Mathf.Max(1, output.pixelHeight);
            mapCenter = ClampToMap(mapCenter -
                rotation * new Vector3(pixels.x, 0, pixels.y / Mathf.Sin(mapPitch * Mathf.Deg2Rad)) * units);
            ApplyMapPose();
        }

        private Vector3 ClampToMap(Vector3 point)
        {
            float limit = Mathf.Max(bounds.size.x, bounds.size.z);
            point.x = Mathf.Clamp(point.x, initialCenter.x - limit, initialCenter.x + limit);
            point.z = Mathf.Clamp(point.z, initialCenter.z - limit, initialCenter.z + limit);
            return point;
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
            mapCamera.Lens.OrthographicSize = fit * mapZoom;
            var rotation = Quaternion.Euler(mapPitch, mapYaw, 0);
            mapCamera.transform.SetPositionAndRotation(mapCenter - rotation * Vector3.forward * 1000, rotation);
        }

        private void UpdateFollowDistance() => follow.FollowOffset = new Vector3(0, followDistance * .45f, -followDistance);

        // Presentation only: preserve physical vehicle pose for localization and sensors.
        // Run before the Cinemachine brain, after physics interpolation has updated the target.
        private void LateUpdate()
        {
            if (IsFollowing && SelectedVehicle is Component component && component != null &&
                SelectedVehicle.VehicleTransform != null) UpdateStabilizedTarget(false);
            else if (IsTrackingPoint && initialized && !IsFollowing)
            {
                // Positions arrive at the server poll rate; ease between them and settle when they stop.
                float dt = Time.unscaledDeltaTime;
                mapCenter = Vector3.SmoothDamp(mapCenter, trackedPoint, ref trackVelocity,
                    TrackSmoothTime, Mathf.Infinity, dt);
                mapZoom = Mathf.SmoothDamp(mapZoom, Mathf.Clamp(trackedViewSize / fit, 20 / fit, 2),
                    ref trackZoomVelocity, TrackSmoothTime, Mathf.Infinity, dt);
                ApplyMapPose();
            }
            if (FleetView) UpdateFleetPanes(false);
        }

        private void UpdateStabilizedTarget(bool reset)
        {
            var position = stabilizedTarget.position;
            var rotation = stabilizedTarget.rotation;
            Stabilize(SelectedVehicle.VehicleTransform, ref position, ref rotation, ref heightVelocity, reset);
            stabilizedTarget.SetPositionAndRotation(position, rotation);
        }

        // Camera anchor only: follows position, damps road vibration in height and keeps world up.
        private static void Stabilize(Transform target, ref Vector3 position, ref Quaternion rotation,
            ref float heightVelocity, bool reset)
        {
            var next = target.position;
            reset |= (next - position).sqrMagnitude > 400;
            if (reset) heightVelocity = 0;
            else next.y = Mathf.SmoothDamp(position.y, next.y, ref heightVelocity, .45f, Mathf.Infinity, Time.deltaTime);
            var heading = Vector3.ProjectOnPlane(target.forward, Vector3.up);
            var facing = heading.sqrMagnitude > .0001f ? Quaternion.LookRotation(heading, Vector3.up) : rotation;
            if (!reset) facing = Quaternion.Slerp(rotation, facing, 1 - Mathf.Exp(-Time.deltaTime / .18f));
            position = next;
            rotation = facing;
        }

        private void Update()
        {
            if (!initialized || output == null || !InputEnabled) return;
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
            // The camera wall has no free map pose; only its shared chase distance zooms.
            if (FleetView) { panGesture = rotateGesture = false; return; }
            var delta = mouse.delta.ReadValue();
            if (rotateGesture && mouse.rightButton.isPressed) Rotate(delta * .2f);
            else if (panGesture && (mouse.leftButton.isPressed || mouse.middleButton.isPressed)) Pan(delta);
        }

        private void OnApplicationFocus(bool focused) { if (!focused) panGesture = rotateGesture = false; }
        private void OnEnable()
        {
            RenderPipelineManager.beginCameraRendering += BeginFollowView;
            RenderPipelineManager.endCameraRendering += EndFollowView;
        }
        private void BeginFollowView(ScriptableRenderContext context, Camera camera)
        {
            RestoreGarageDisplays();
            if (FleetView ? !IsFleetCamera(camera) : !IsFollowing || camera != output) return;
            // Garage models and hub cubes are collider-free presentation overlays.
            // At a parked vehicle they surround the follow camera and obscure the road.
            foreach (var renderer in garageDisplays)
                if (renderer != null && !hiddenGarageDisplays.ContainsKey(renderer))
                {
                    hiddenGarageDisplays.Add(renderer, renderer.forceRenderingOff);
                    renderer.forceRenderingOff = true;
                }
        }
        private void EndFollowView(ScriptableRenderContext context, Camera camera)
        {
            if (camera == output || IsFleetCamera(camera)) RestoreGarageDisplays();
        }
        private void RestoreGarageDisplays()
        {
            foreach (var pair in hiddenGarageDisplays)
                if (pair.Key != null) pair.Key.forceRenderingOff = pair.Value;
            hiddenGarageDisplays.Clear();
        }
        private void OnDisable()
        {
            RenderPipelineManager.beginCameraRendering -= BeginFollowView;
            RenderPipelineManager.endCameraRendering -= EndFollowView;
            RestoreGarageDisplays();
            SetFleetView(false);
            if (brain != null && ownsBrain) brain.enabled = false;
            if (followCamera != null) followCamera.gameObject.SetActive(false);
            if (mapCamera != null) mapCamera.gameObject.SetActive(false);
        }
        private void OnDestroy()
        {
            if (brain != null)
            {
                if (ownsBrain) Destroy(brain);
                else
                {
                    brain.UpdateMethod = originalBrainUpdate;
                    brain.DefaultBlend = originalBlend;
                    brain.LensModeOverride = originalLensMode;
                    brain.enabled = originalBrainEnabled;
                }
            }
            if (followCamera != null) Destroy(followCamera.gameObject);
            if (mapCamera != null) Destroy(mapCamera.gameObject);
            if (stabilizedTarget != null) Destroy(stabilizedTarget.gameObject);
            foreach (var pane in fleetPanes)
                if (pane.camera != null)
                {
                    VegetationDisplaySettings.ExtraViewpoints.Remove(pane.camera.transform);
                    Destroy(pane.camera.gameObject);
                }
            fleetPanes.Clear();
        }
    }
}
