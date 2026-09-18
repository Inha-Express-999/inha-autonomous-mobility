using UnityEngine;

namespace CampusSim
{
    // Editor-authored anchors. Spawning and movement belong to the Python server.
    public sealed class CampusVehicleHub : MonoBehaviour
    {
        public string hubId = "west_field_autonomous_hub";
        public string sourceParkingOsmId = "way/568420971";
        public string verificationStatus = "synthetic_design_unverified_access";
        public Transform[] departureBays;
        public Transform entry;
        public Transform exit;
        public Transform departureHold;
        public Transform passengerWaiting;
        public Transform assistanceWaiting;
    }
}
