using System.Collections.Generic;
using UnityEngine;

namespace InhaExpress.Client.Simulation
{
    /// <summary>Opt-in synthetic crossing fixture. Never publishes ground truth to Python.</summary>
    [RequireComponent(typeof(BoxCollider))]
    public sealed class PedestrianSpawnTrigger : MonoBehaviour
    {
        public GameObject pedestrianPrefab;
        public Transform spawnPoint;
        public Transform destination;
        [Min(.1f)] public float walkingSpeed = 1.3f;
        [Min(1)] public int maximumAlive = 3;
        [Min(0)] public float cooldownSeconds = 2;
        public bool oneShot = true;
        [Min(0)] public float initialWaitSeconds;
        [HideInInspector] public Vector3[] walkingPath;
        private bool fired;
        private float nextSpawn;
        private readonly List<GameObject> alive = new List<GameObject>();
        public int SpawnCount { get; private set; }
        public GameObject LastSpawned { get; private set; }

        private void Reset() => GetComponent<BoxCollider>().isTrigger = true;

        private void OnTriggerEnter(Collider other)
        {
            var label = other.GetComponentInParent<LidarTarget>();
            if (label != null && label.EntityClass == LidarEntityClass.Vehicle) TrySpawn();
        }

        [ContextMenu("Spawn pedestrian (Play mode)")]
        public void SpawnNow() => TrySpawn();

        public bool TrySpawn()
        {
            if (!Application.isPlaying || !isActiveAndEnabled || pedestrianPrefab == null ||
                spawnPoint == null || destination == null || (oneShot && fired) || Time.time < nextSpawn)
                return false;
            alive.RemoveAll(item => item == null);
            if (alive.Count >= Mathf.Clamp(maximumAlive, 1, 30)) return false;
            var root = new GameObject($"Synthetic Pedestrian {++SpawnCount}");
            // The fixture owns the actor so removing the fixture cleans up the entire test.
            root.transform.SetParent(transform, true);
            root.transform.position = spawnPoint.position;
            var body = root.AddComponent<Rigidbody>();
            body.isKinematic = true;
            body.useGravity = false;
            var capsule = root.AddComponent<CapsuleCollider>();
            capsule.height = 1.75f;
            capsule.radius = .3f;
            capsule.center = Vector3.up * .875f;
            root.AddComponent<LidarTarget>().SetObservedClass(LidarEntityClass.Pedestrian);
            var visual = Instantiate(pedestrianPrefab, root.transform, false);
            visual.transform.localPosition = Vector3.zero;
            visual.transform.localRotation = Quaternion.identity;
            foreach (var collider in visual.GetComponentsInChildren<Collider>()) collider.enabled = false;
            foreach (var animator in visual.GetComponentsInChildren<Animator>())
            {
                animator.applyRootMotion = false;
                // Animation culling must never stop the actor's simulation or collider.
                animator.cullingMode = AnimatorCullingMode.CullUpdateTransforms;
            }
            root.AddComponent<SyntheticPedestrianWalker>().Initialize(body, destination.position,
                Mathf.Clamp(walkingSpeed, .1f, 3f), initialWaitSeconds, walkingPath);
            LastSpawned = root;
            alive.Add(root);
            fired = true;
            nextSpawn = Time.time + Mathf.Max(0, cooldownSeconds);
            return true;
        }

        private void OnDrawGizmosSelected()
        {
            if (spawnPoint == null || destination == null) return;
            Gizmos.color = Color.cyan;
            Gizmos.DrawLine(spawnPoint.position, destination.position);
            Gizmos.DrawWireSphere(spawnPoint.position, .3f);
        }
    }
}
