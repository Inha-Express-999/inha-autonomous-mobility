using UnityEngine;

namespace InhaExpress.Client.Simulation
{
    /// <summary>
    /// Marks geometry such as the demo floor that should occlude rays but not be
    /// reported as a detected object. A visual return can still be drawn in the
    /// isolated LiDAR point cloud.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class LidarIgnore : MonoBehaviour
    {
        [SerializeField] private bool showReturnInPointCloud;

        public bool ShowReturnInPointCloud => showReturnInPointCloud;
    }
}
