using UnityEngine;

namespace InhaExpress.Client.Simulation
{
    /// <summary>Standalone fixture controls, never added to the production campus.</summary>
    public sealed class PedestrianSpawnDemoPanel : MonoBehaviour
    {
        public PedestrianSpawnTrigger trigger;
        private GameObject probe;

        private void OnGUI()
        {
            GUILayout.BeginArea(new Rect(20, 20, 340, 150), GUI.skin.box);
            GUILayout.Label("Synthetic pedestrian crossing test");
            GUILayout.Label($"Spawned: {trigger.SpawnCount} (one shot per Play)");
            if (GUILayout.Button("Drive test probe through trigger") && probe == null)
            {
                probe = GameObject.CreatePrimitive(PrimitiveType.Cube);
                probe.name = "Synthetic vehicle trigger probe";
                probe.transform.position = new Vector3(0, .5f, -9);
                var body = probe.AddComponent<Rigidbody>();
                body.isKinematic = true;
                body.useGravity = false;
                probe.AddComponent<LidarTarget>().SetObservedClass(LidarEntityClass.Vehicle);
                probe.AddComponent<SyntheticPedestrianWalker>().Initialize(body, new Vector3(0, .5f, -3), 3);
            }
            if (GUILayout.Button("Spawn pedestrian now")) trigger.SpawnNow();
            GUILayout.EndArea();
        }
    }
}
