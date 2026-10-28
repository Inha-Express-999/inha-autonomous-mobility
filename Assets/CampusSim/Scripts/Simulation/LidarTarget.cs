using UnityEngine;

namespace InhaExpress.Client.Simulation
{
    public enum LidarEntityClass
    {
        Unknown = 0,
        Pedestrian = 1,
        Vehicle = 2,
        StaticObstacle = 3
    }

    /// <summary>
    /// Optional semantic label for a collider hit by the simulated LiDAR.
    /// The sensor still reports geometry when this component is absent.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class LidarTarget : MonoBehaviour
    {
        [SerializeField] private string entityId;
        [SerializeField] private LidarEntityClass entityClass = LidarEntityClass.Unknown;

        public string EntityId => entityId;
        public LidarEntityClass EntityClass => entityClass;

        public void SetObservedClass(LidarEntityClass value) => entityClass = value;

        private void OnValidate()
        {
            entityId = entityId?.Trim();
            if (!System.Enum.IsDefined(typeof(LidarEntityClass), entityClass))
                entityClass = LidarEntityClass.Unknown;
        }
    }
}
