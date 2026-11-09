using UnityEngine;
namespace CampusSim
{
    // Authoring anchors: rendering visibility never disables physics.
    public sealed class CampusPlantGroundAnchor : MonoBehaviour
    {
        public float pivotAboveBottom;
        public float footprintRadius=.35f;
        public float clearance=.025f;
        public string sourcePrefab;
    }
}
