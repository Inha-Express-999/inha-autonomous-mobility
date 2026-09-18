using UnityEngine;

namespace CampusSim
{
    // Authoring metadata only. Python remains the future runtime state owner.
    public sealed class CampusTerrainBinding : MonoBehaviour
    {
        public enum BindingKind { Surface, Building, Water, Vegetation }
        public BindingKind kind;
        public Mesh sourceMesh;
        public Mesh originalMesh;
        public CampusTerrainBinding heightAnchor;
        public Mesh bakedMesh;
        public Vector3 samplePoint;
        public Vector3 originalPosition;
        public float surfaceOffset;
        [Min(0.01f)] public float surfaceReliefScale = 1f;
        public string sourceId;
        public bool verifiedAccess;
    }
}
