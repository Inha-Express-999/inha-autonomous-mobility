using System.Collections;
using InhaExpress.Client.Presentation;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Unity.Cinemachine;

namespace InhaExpress.Client.Tests
{
    public sealed class CameraTestVehicle : MonoBehaviour, IOperatorVehicle
    {
        public string id;
        public string OperatorVehicleId => id;
        public Transform VehicleTransform => transform;
        public Rigidbody VehicleBody => GetComponent<Rigidbody>();
    }

    public sealed class PcCameraNavigationTests
    {
        [UnityTest]
        public IEnumerator AuthoredBrainIsReusedAndSceneViewResumesAfterControlsAreDestroyed()
        {
            var cameraObject = new GameObject("Shared output camera test");
            var sceneViewObject = new GameObject("Authored scene viewpoint test");
            var controlsObject = new GameObject("Shared brain controls test");
            try
            {
                var camera = cameraObject.AddComponent<Camera>();
                var brain = cameraObject.AddComponent<CinemachineBrain>();
                brain.UpdateMethod = CinemachineBrain.UpdateMethods.LateUpdate;
                brain.DefaultBlend = new CinemachineBlendDefinition(CinemachineBlendDefinition.Styles.Cut, 0);
                var sceneView = sceneViewObject.AddComponent<CinemachineCamera>();
                sceneView.Priority = 10;
                var controls = controlsObject.AddComponent<PcOperatorControls>();
                controls.Initialize(camera, new Bounds(Vector3.zero, Vector3.one * 100), 100);
                yield return null;
                yield return null;
                Assert.That(cameraObject.GetComponents<CinemachineBrain>().Length, Is.EqualTo(1));
                Assert.That(brain.ActiveVirtualCamera.Name, Is.EqualTo("Campus map Cinemachine"));
                Assert.That(camera.orthographic, Is.True);
                Assert.That(camera.farClipPlane, Is.GreaterThanOrEqualTo(2500));
                Object.Destroy(controlsObject);
                yield return null;
                yield return null;
                Assert.That(brain != null && brain.enabled, Is.True);
                Assert.That(brain.ActiveVirtualCamera, Is.SameAs(sceneView));
            }
            finally
            {
                if (controlsObject != null) Object.DestroyImmediate(controlsObject);
                Object.DestroyImmediate(sceneViewObject);
                Object.DestroyImmediate(cameraObject);
            }
        }

        [UnityTest]
        public IEnumerator FollowRejectsRoadVibrationButTracksSustainedElevationAndResetsOnSelection()
        {
            var cameraObject = new GameObject("Vibration camera test");
            var vehicleObject = new GameObject("Synthetic vibrating target");
            var controlsObject = new GameObject("Vibration controls test");
            try
            {
                var camera = cameraObject.AddComponent<Camera>();
                var vehicle = vehicleObject.AddComponent<CameraTestVehicle>();
                vehicle.id = "vibration-test";
                var controls = controlsObject.AddComponent<PcOperatorControls>();
                controls.Initialize(camera, new Bounds(Vector3.zero, Vector3.one * 100), 100);
                controls.SelectVehicle(vehicle.id);
                var target = controls.FollowCamera.Follow;
                for (int i = 0; i < 30; i++)
                {
                    vehicle.transform.SetPositionAndRotation(new Vector3(0, i % 2 == 0 ? .2f : -.2f, 0),
                        Quaternion.Euler(i % 2 == 0 ? 8 : -8, 0, 6));
                    yield return null;
                    Assert.That(Mathf.Abs(target.position.y), Is.LessThan(.15f));
                    Assert.That(Vector3.Angle(target.up, Vector3.up), Is.LessThan(.01f));
                }
                vehicle.transform.SetPositionAndRotation(Vector3.up * 2, Quaternion.identity);
                yield return new WaitForSeconds(2);
                Assert.That(target.position.y, Is.EqualTo(2).Within(.05f));
                Assert.That(vehicle.transform.position.y, Is.EqualTo(2));
                controls.ShowMap();
                vehicle.transform.position = new Vector3(50, 5, 0);
                controls.SelectVehicle(vehicle.id);
                Assert.That(target.position, Is.EqualTo(vehicle.transform.position));
            }
            finally
            {
                Object.DestroyImmediate(controlsObject);
                Object.DestroyImmediate(cameraObject);
                Object.DestroyImmediate(vehicleObject);
            }
        }

        [UnityTest]
        public IEnumerator ExplicitTargetSelectionTracksMovementAndFallsBackWhenTargetIsDestroyed()
        {
            var cameraObject = new GameObject("Camera selection test");
            var vehicleObject = new GameObject("Synthetic camera target");
            var controlsObject = new GameObject("PC camera controls test");
            try
            {
                var camera = cameraObject.AddComponent<Camera>();
                var vehicle = vehicleObject.AddComponent<CameraTestVehicle>();
                vehicle.id = "camera-test";
                var controls = controlsObject.AddComponent<PcOperatorControls>();
                controls.Initialize(camera, new Bounds(Vector3.zero, Vector3.one * 100), 100);
                Assert.That(controls.SelectVehicle("missing"), Is.False);
                Assert.That(controls.IsFollowing, Is.False);
                Assert.That(controls.SelectVehicle("camera-test"), Is.True);
                Assert.That(controls.FollowCamera.Follow, Is.Not.SameAs(vehicle.transform));
                Assert.That(controls.FollowCamera.LookAt, Is.SameAs(controls.FollowCamera.Follow));
                for (int i = 0; i < 8; i++) yield return null;
                Assert.That(camera.orthographic, Is.False);
                var before = camera.transform.position;
                // Synthetic target only: production camera controls never modify vehicle transforms.
                vehicle.transform.position += Vector3.right * 5;
                for (int i = 0; i < 12; i++) yield return null;
                Assert.That(camera.transform.position.x, Is.GreaterThan(before.x + .1f));
                controls.ReleaseFollow();
                Assert.That(controls.IsFollowing, Is.False);
                Assert.That(controls.SelectedVehicle, Is.SameAs(vehicle));
                Assert.That(controls.FollowCamera.Follow, Is.Null);
                Assert.That(controls.FollowCamera.LookAt, Is.Null);
                yield return null;
                yield return null; // Allow the Cinemachine LateUpdate to drive the output camera.
                before = camera.transform.position;
                vehicle.transform.position += Vector3.right * 5;
                for (int i = 0; i < 12; i++) yield return null;
                Assert.That(Vector3.Distance(before, camera.transform.position), Is.LessThan(.01));
                controls.Pan(new Vector2(50, 20));
                yield return null;
                yield return null; // Allow the Cinemachine LateUpdate to drive the output camera.
                Assert.That(Vector3.Distance(before, camera.transform.position), Is.GreaterThan(.1));
                Assert.That(controls.SelectVehicle("camera-test"), Is.True);
                Object.Destroy(vehicleObject);
                yield return null;
                yield return null;
                Assert.That(controls.IsFollowing, Is.False);
                Assert.That(camera.orthographic, Is.True);
            }
            finally
            {
                Object.DestroyImmediate(controlsObject);
                Object.DestroyImmediate(cameraObject);
                if (vehicleObject != null) Object.DestroyImmediate(vehicleObject);
            }
        }

        [UnityTest]
        public IEnumerator FleetViewShowsEveryVehicleSideBySideAndRestoresTheSingleView()
        {
            var cameraObject = new GameObject("Fleet output camera test");
            var controlsObject = new GameObject("Fleet controls test");
            var vehicleObjects = new GameObject[3];
            try
            {
                var camera = cameraObject.AddComponent<Camera>();
                camera.rect = new Rect(.1f, .2f, .6f, .5f);
                for (int i = 0; i < vehicleObjects.Length; i++)
                {
                    // Created out of order: panes must follow the vehicle ID, not scene order.
                    vehicleObjects[i] = new GameObject("Synthetic fleet target " + i);
                    vehicleObjects[i].AddComponent<CameraTestVehicle>().id = "V0" + (3 - i);
                    vehicleObjects[i].transform.position = new Vector3(i * 100, 0, 0);
                }
                var controls = controlsObject.AddComponent<PcOperatorControls>();
                controls.Initialize(camera, new Bounds(Vector3.zero, Vector3.one * 100), 100);
                yield return null;
                Assert.That(controls.FleetView, Is.False);
                Assert.That(controls.SetFleetView(true), Is.True);
                Assert.That(controls.FleetPaneCount, Is.EqualTo(3));
                Assert.That(camera.enabled, Is.False);
                yield return null;
                yield return null;
                for (int i = 0; i < 3; i++)
                {
                    var vehicle = controls.FleetPaneVehicle(i);
                    var pane = controls.FleetPaneCamera(i);
                    Assert.That(vehicle.OperatorVehicleId, Is.EqualTo("V0" + (i + 1)));
                    Assert.That(pane.enabled, Is.True);
                    Assert.That(pane.orthographic, Is.False);
                    Assert.That(pane.rect.x, Is.EqualTo(.1f + .2f * i).Within(.0001f));
                    Assert.That(pane.rect.width, Is.EqualTo(.2f).Within(.0001f));
                    Assert.That(pane.rect.y, Is.EqualTo(.2f).Within(.0001f));
                    Assert.That(pane.rect.height, Is.EqualTo(.5f).Within(.0001f));
                    var point = pane.WorldToViewportPoint(vehicle.VehicleTransform.position);
                    Assert.That(point.z, Is.GreaterThan(0));
                    Assert.That(point.x, Is.InRange(.2f, .8f));
                    Assert.That(point.y, Is.InRange(.2f, .8f));
                }
                var moving = controls.FleetPaneVehicle(1).VehicleTransform;
                var before = controls.FleetPaneCamera(1).transform.position;
                var neighbour = controls.FleetPaneCamera(0).transform.position;
                // Synthetic target only: production camera controls never modify vehicle transforms.
                moving.position += Vector3.forward * 5;
                yield return null;
                yield return null;
                Assert.That(Vector3.Distance(before, controls.FleetPaneCamera(1).transform.position), Is.GreaterThan(1));
                Assert.That(Vector3.Distance(neighbour, controls.FleetPaneCamera(0).transform.position), Is.LessThan(.01f));
                Assert.That(moving.position.z, Is.EqualTo(5));
                // The wall is a view mode only; it selects nothing and resumes the map as it was.
                Assert.That(controls.IsFollowing, Is.False);
                Assert.That(controls.SelectedVehicle, Is.Null);
                Assert.That(controls.SetFleetView(false), Is.False);
                Assert.That(camera.enabled, Is.True);
                for (int i = 0; i < 3; i++) Assert.That(controls.FleetPaneCamera(i).enabled, Is.False);
                Object.Destroy(controlsObject);
                yield return null;
                Assert.That(Object.FindObjectsByType<Camera>(FindObjectsInactive.Include, FindObjectsSortMode.None).Length, Is.EqualTo(1));
            }
            finally
            {
                if (controlsObject != null) Object.DestroyImmediate(controlsObject);
                Object.DestroyImmediate(cameraObject);
                foreach (var vehicleObject in vehicleObjects)
                    if (vehicleObject != null) Object.DestroyImmediate(vehicleObject);
            }
        }

        [UnityTest]
        public IEnumerator MapNavigationPreservesZoomOnResizeAndResetsItsPose()
        {
            var cameraObject = new GameObject("Map camera test");
            var controlsObject = new GameObject("Map controls test");
            try
            {
                var camera = cameraObject.AddComponent<Camera>();
                var controls = controlsObject.AddComponent<PcOperatorControls>();
                controls.Initialize(camera, new Bounds(Vector3.zero, Vector3.one * 100), 100);
                yield return null;
                yield return null; // Allow the Cinemachine LateUpdate to drive the output camera.
                var initialPosition = camera.transform.position;
                controls.Pan(new Vector2(40, 20));
                yield return null;
                yield return null; // Allow the Cinemachine LateUpdate to drive the output camera.
                Assert.That(Vector3.Distance(initialPosition, camera.transform.position), Is.GreaterThan(1));
                controls.Rotate(new Vector2(30, 10));
                yield return null;
                yield return null; // Allow the Cinemachine LateUpdate to drive the output camera.
                Assert.That(camera.transform.eulerAngles.y, Is.EqualTo(30).Within(.01));
                controls.Zoom(.5f);
                controls.Resize(200);
                yield return null;
                yield return null; // Allow the Cinemachine LateUpdate to drive the output camera.
                Assert.That(camera.orthographicSize, Is.EqualTo(100).Within(.01));
                controls.Zoom(float.NaN);
                Assert.That(camera.orthographicSize, Is.EqualTo(100).Within(.01));
                controls.DeselectVehicle();
                yield return null;
                yield return null; // Allow the Cinemachine LateUpdate to drive the output camera.
                Assert.That(controls.SelectedVehicle, Is.Null);
                Assert.That(camera.orthographicSize, Is.EqualTo(200).Within(.01));
                Assert.That(Vector3.Distance(initialPosition, camera.transform.position), Is.LessThan(.01));
                yield return null;
            }
            finally
            {
                Object.DestroyImmediate(controlsObject);
                Object.DestroyImmediate(cameraObject);
            }
        }
    }
}
