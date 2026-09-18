using UnityEngine;
namespace CampusSim
{
    public sealed class CampusLandmarkEntrances : MonoBehaviour
    {
        public string landmarkId;
        public string verificationStatus="synthetic_facade_entrances_not_verified_access";
        public Transform generalEntrance;
        public Transform accessibleEntrance;
        public bool accessibleEntranceExclusive=true;
        public bool routeValidated=false;
    }
}
