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
                Assert.That(controls.FollowCamera.Follow, Is.EqualTo(vehicle.transform));
                for (int i = 0; i < 8; i++) yield return null;
                Assert.That(camera.orthographic, Is.False);
                var before = camera.transform.position;
                // Synthetic target only: production camera controls never modify vehicle transforms.
                vehicle.transform.position += Vector3.right * 5;
                for (int i = 0; i < 12; i++) yield return null;
                Assert.That(camera.transform.position.x, Is.GreaterThan(before.x + .1f));
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
