using System;
using InhaExpress.Client.Domain;
using UnityEngine;

namespace InhaExpress.Client.Presentation
{
    public sealed class RoadGraphStopMarkerView : RoadGraphMarkerView
    {
        [SerializeField] private string stopId;
        [SerializeField] private string landmarkId;

        public string StopId => stopId;
        public string LandmarkId => landmarkId;

        public void Bind(string id, string associatedLandmarkId, MapPositionDto position,
            RoadGraphVerificationStatus status)
        {
            if (string.IsNullOrWhiteSpace(id)) throw new ArgumentException("Stop ID is required.", nameof(id));
            stopId = id;
            landmarkId = associatedLandmarkId;
            transform.localPosition = MapCoordinateConverter.ToUnity(position);
            SetVerificationStatus(status);
        }

        private void Awake() => SetStatusRenderers(GetComponentsInChildren<Renderer>(true));
        private void OnValidate() => SetStatusRenderers(GetComponentsInChildren<Renderer>(true));
    }
}
