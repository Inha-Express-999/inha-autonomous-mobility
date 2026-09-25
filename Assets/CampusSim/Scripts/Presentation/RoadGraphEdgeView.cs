using System;
using System.Collections.Generic;
using InhaExpress.Client.Domain;
using UnityEngine;

namespace InhaExpress.Client.Presentation
{
    [RequireComponent(typeof(LineRenderer))]
    public sealed class RoadGraphEdgeView : RoadGraphMarkerView
    {
        [SerializeField] private string edgeId;
        [SerializeField] private string fromNodeId;
        [SerializeField] private string toNodeId;
        [SerializeField, Min(0.01f)] private float widthMeters = 0.18f;

        private LineRenderer line;
        public string EdgeId => edgeId;
        public string FromNodeId => fromNodeId;
        public string ToNodeId => toNodeId;

        public override void SetVerificationStatus(RoadGraphVerificationStatus status)
        {
            base.SetVerificationStatus(status);
            if (!line) line = GetComponent<LineRenderer>();
            if (line) line.startColor = line.endColor = StatusColor(status);
        }

        public void Bind(string id, string from, string to, IReadOnlyList<MapPositionDto> geometry,
            RoadGraphVerificationStatus status)
        {
            if (string.IsNullOrWhiteSpace(id)) throw new ArgumentException("Edge ID is required.", nameof(id));
            if (geometry == null || geometry.Count < 2) throw new ArgumentException("An edge needs at least two geometry points.", nameof(geometry));
            edgeId = id;
            fromNodeId = from;
            toNodeId = to;
            if (!line) line = GetComponent<LineRenderer>();
            line.useWorldSpace = false;
            line.positionCount = geometry.Count;
            for (int i = 0; i < geometry.Count; i++) line.SetPosition(i, MapCoordinateConverter.ToUnity(geometry[i]));
            line.startWidth = widthMeters;
            line.endWidth = widthMeters;
            SetStatusRenderers(new Renderer[] { line });
            SetVerificationStatus(status);
        }

        private void Awake() => line = GetComponent<LineRenderer>();
        private void OnValidate()
        {
            line = GetComponent<LineRenderer>();
            if (line) { line.startWidth = widthMeters; line.endWidth = widthMeters; }
            SetStatusRenderers(line ? new Renderer[] { line } : Array.Empty<Renderer>());
            SetVerificationStatus(VerificationStatus);
        }
    }
}
