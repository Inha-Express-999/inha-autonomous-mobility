using UnityEngine;

namespace InhaExpress.Client.MapClient
{
    public sealed class ServerMapMarker : MonoBehaviour
    {
        public enum MarkerKind { RoadNode, Hub, Stop }
        public MarkerKind kind;
        public string serverId, nodeId, displayName, surfaceSource;
        public string[] vehicleIds;
        public Vector3 serverPosition;
        public bool operationallyVerified;

        private void OnDrawGizmosSelected()
        {
            Gizmos.color = Color.yellow;
            Gizmos.DrawWireSphere(transform.position, 2f);
#if UNITY_EDITOR
            UnityEditor.Handles.Label(transform.position + Vector3.up * 2,
                displayName + "\n" + serverId + "\n" + surfaceSource);
#endif
        }
    }
}
