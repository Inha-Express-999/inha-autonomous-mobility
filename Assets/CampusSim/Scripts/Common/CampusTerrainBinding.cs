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
        // Optional road-only authoring profile. Retain the unmodified height source across rebakes.
        public TerrainData roadHeightSource;
        // Physical terrain seed is independent of a synthetic, grade-limited road profile.
        public TerrainData roadSupportSource;
        public bool roadPlanarGridProfile;
        [Min(0)] public float roadSmoothingMeters;
        // Synthetic connection grade anchored to existing asphalt, not building-pad DSM.
        public bool roadEndpointGrade;
        public Vector3 roadGradeStart;
        public Vector3 roadGradeEnd;
    }
}
