using UnityEngine;

namespace CampusSim
{
    /// <summary>
    /// Provenance for an authored grandstand visualization. This metadata does not
    /// certify dimensions, public access, seating capacity, or a vehicle route.
    /// </summary>
    public sealed class CampusGrandstandMetadata : MonoBehaviour
    {
        [SerializeField] private string sourceOsmWayId = "1203054818";
        [SerializeField] private string verificationStatus = "visual_synthetic_unverified";
        [SerializeField] private string notes = "OSM name/description identify open-air grandstand; geometry is a visual approximation only.";

        public string SourceOsmWayId => sourceOsmWayId;
        public string VerificationStatus => verificationStatus;
        public string Notes => notes;
    }
}
