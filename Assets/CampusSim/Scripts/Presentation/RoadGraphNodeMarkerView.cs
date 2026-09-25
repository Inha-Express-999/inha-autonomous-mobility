using System;
using InhaExpress.Client.Domain;
using UnityEngine;

namespace InhaExpress.Client.Presentation
{
    public sealed class RoadGraphNodeMarkerView : RoadGraphMarkerView
    {
        [SerializeField] private string nodeId;
        [SerializeField] private string landmarkId;

        public string NodeId => nodeId;
        public string LandmarkId => landmarkId;

        public void Bind(string id, string associatedLandmarkId, MapPositionDto position,
            RoadGraphVerificationStatus status)
        {
            if (string.IsNullOrWhiteSpace(id)) throw new ArgumentException("Node ID is required.", nameof(id));
            nodeId = id;
            landmarkId = associatedLandmarkId;
            transform.localPosition = MapCoordinateConverter.ToUnity(position);
            SetVerificationStatus(status);
        }

        private void Awake() => SetStatusRenderers(GetComponentsInChildren<Renderer>(true));
        private void OnValidate() => SetStatusRenderers(GetComponentsInChildren<Renderer>(true));
    }
}
