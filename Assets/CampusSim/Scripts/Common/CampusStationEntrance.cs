using UnityEngine;
namespace CampusSim
{
    public sealed class CampusStationEntrance : MonoBehaviour
    {
        public string landmarkId="inha_station";
        public string exitNumber;
        public string osmNodeId;
        public string wheelchair="unknown";
        public Transform entrance;
        public bool routeVerified;
    }
}
