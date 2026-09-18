using UnityEngine;
namespace CampusSim
{
    // Authoring metadata only. Exporters must preserve this exclusion in vehicle maps;
    // this component never grants vehicle or accessible pedestrian route approval.
    public sealed class CampusPedestrianArea : MonoBehaviour
    {
        public string areaId;
        public Vector3[] localBoundary;
        public bool excludeFromVehicleRouting = true;
        public bool pedestrianAccessVerified = false;
        public string source = "synthetic visual geometry";
    }
}
