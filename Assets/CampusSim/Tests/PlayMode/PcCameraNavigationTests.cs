using System.Collections;
using InhaExpress.Client.Presentation;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

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
                before = camera.transform.position;
                vehicle.transform.position += Vector3.right * 5;
                for (int i = 0; i < 12; i++) yield return null;
                Assert.That(Vector3.Distance(before, camera.transform.position), Is.LessThan(.01));
                controls.Pan(new Vector2(50, 20));
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
        public IEnumerator MapNavigationPreservesZoomOnResizeAndResetsItsPose()
        {
            var cameraObject = new GameObject("Map camera test");
            var controlsObject = new GameObject("Map controls test");
            try
            {
                var camera = cameraObject.AddComponent<Camera>();
                var controls = controlsObject.AddComponent<PcOperatorControls>();
                controls.Initialize(camera, new Bounds(Vector3.zero, Vector3.one * 100), 100);
                var initialPosition = camera.transform.position;
                controls.Pan(new Vector2(40, 20));
                Assert.That(Vector3.Distance(initialPosition, camera.transform.position), Is.GreaterThan(1));
                controls.Rotate(new Vector2(30, 10));
                Assert.That(camera.transform.eulerAngles.y, Is.EqualTo(30).Within(.01));
                controls.Zoom(.5f);
                controls.Resize(200);
                Assert.That(camera.orthographicSize, Is.EqualTo(100).Within(.01));
                controls.Zoom(float.NaN);
                Assert.That(camera.orthographicSize, Is.EqualTo(100).Within(.01));
                controls.ShowMap(true);
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
