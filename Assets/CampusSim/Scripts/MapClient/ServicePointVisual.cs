using UnityEngine;

namespace InhaExpress.Client.MapClient
{
    /// <summary>Identity of a display model owned by a persistent service point.</summary>
    [DisallowMultipleComponent]
    public sealed class ServicePointVisual : MonoBehaviour
    {
        public ServerMapMarker.MarkerKind pointKind;
        public string pointId;
    }
}
