using InhaExpress.Client.Presentation;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

namespace InhaExpress.Client.Simulation
{
    // The imported LiDAR demo previously used the controller's removed Editor input.
    // Keep manual demonstration input confined to that standalone scene.
    [DisallowMultipleComponent, RequireComponent(typeof(VehicleController))]
    public sealed class LidarDemoManualDriver : MonoBehaviour
    {
        private const string DemoScene = "LidarSensorDemo";
        private VehicleController vehicle;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Install()
        {
            SceneManager.sceneLoaded -= SceneLoaded;
            SceneManager.sceneLoaded += SceneLoaded;
            for (int i = 0; i < SceneManager.sceneCount; i++)
                BindDemo(SceneManager.GetSceneAt(i));
        }

        private static void SceneLoaded(Scene scene, LoadSceneMode mode) => BindDemo(scene);

        private static void BindDemo(Scene scene)
        {
            if (scene.name != DemoScene || !scene.isLoaded) return;
            foreach (var root in scene.GetRootGameObjects())
                foreach (var sensor in root.GetComponentsInChildren<LidarSensorRig>(true))
                {
                    var car = sensor.GetComponentInParent<VehicleController>();
                    if (car != null && car.GetComponent<LidarDemoManualDriver>() == null)
                        car.gameObject.AddComponent<LidarDemoManualDriver>();
                }
        }

        private void Awake()
        {
            vehicle = GetComponent<VehicleController>();
            if (gameObject.scene.name != DemoScene) enabled = false;
        }

        private void Update()
        {
            if (gameObject.scene.name != DemoScene) { enabled = false; return; }
            var keyboard = Keyboard.current;
            if (keyboard == null) { vehicle.SetDriveCommand(0, 0, true); return; }
            bool forward = keyboard.wKey.isPressed || keyboard.upArrowKey.isPressed;
            bool reverse = keyboard.sKey.isPressed || keyboard.downArrowKey.isPressed;
            bool left = keyboard.aKey.isPressed || keyboard.leftArrowKey.isPressed;
            bool right = keyboard.dKey.isPressed || keyboard.rightArrowKey.isPressed;
            float speed = forward == reverse ? 0 : forward ? 12f : -5f;
            float steer = left == right ? 0 : (right ? 1 : -1) * vehicle.MaximumSteeringRad;
            vehicle.SetDriveCommand(speed, steer, keyboard.spaceKey.isPressed);
        }

        private void OnDisable()
        {
            if (vehicle != null && gameObject.scene.name == DemoScene)
                vehicle.SetDriveCommand(0, 0, true);
        }
    }
}
