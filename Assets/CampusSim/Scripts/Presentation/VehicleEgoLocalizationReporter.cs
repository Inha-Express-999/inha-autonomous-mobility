using System;
using InhaExpress.Client.Domain;
using InhaExpress.Client.Networking;
using UnityEngine;

namespace InhaExpress.Client.Presentation
{
    /// <summary>
    /// Sends only this vehicle's Rigidbody pose to the Python server.
    /// Attach to a configured vehicle actor; this component does not drive it.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Rigidbody))]
    [DefaultExecutionOrder(-100)]
    public sealed class VehicleEgoLocalizationReporter : MonoBehaviour
    {
        private const double ReportIntervalS = 0.1;

        [SerializeField] private string vehicleId;

        private Rigidbody body;
        private ClientRuntimeHost runtime;
        private Vector3 previousReportedPosition;
        private double previousReportAtS;
        private double nextReportAtS;
        private bool hasPreviousPosition;
        private long observedTick = -1;

        public string VehicleId => vehicleId;
        public long LatestObservedTick => observedTick;
        public ClientRuntimeHost Runtime => runtime;

        private void Awake() => body = GetComponent<Rigidbody>();

        public void Configure(string id, ClientRuntimeHost owner)
        {
            if (owner == null || owner.Store == null || owner.Role != ClientRole.PC_Operator)
                throw new ArgumentException("Ego telemetry requires an initialized PC operator runtime.", nameof(owner));
            if (string.IsNullOrWhiteSpace(id))
                throw new ArgumentException("A vehicle ID is required.", nameof(id));
            runtime = owner;
            if (body == null) body = GetComponent<Rigidbody>();
            vehicleId = string.IsNullOrWhiteSpace(id) ? null : id.Trim();
            observedTick = -1;
            hasPreviousPosition = false;
            previousReportAtS = 0.0;
            nextReportAtS = 0.0;
        }

        private void FixedUpdate()
        {
            if (string.IsNullOrEmpty(vehicleId) || body == null) return;
            double nowS = Time.realtimeSinceStartupAsDouble;
            if (nowS < nextReportAtS) return;

            if (runtime == null || runtime.Role != ClientRole.PC_Operator ||
                runtime.Localization == null || runtime.Store?.Current == null)
                return;

            var position = body.position;
            double speedMps = 0.0;
            if (hasPreviousPosition && nowS > previousReportAtS)
            {
                var planarDelta = position - previousReportedPosition;
                planarDelta.y = 0f;
                speedMps = planarDelta.magnitude / (nowS - previousReportAtS);
            }

            var headingRad = body.rotation.eulerAngles.y * Mathf.Deg2Rad;
            var mapPosition = MapCoordinateConverter.FromUnity(position);
            long nextTick = runtime.NextEgoTick(vehicleId);
            runtime.Localization.SendEgoLocalization(new EgoLocalizationDto(
                vehicleId,
                nextTick,
                runtime.Store.Current.MapVersion,
                mapPosition,
                headingRad,
                speedMps));
            observedTick = nextTick;

            previousReportedPosition = position;
            previousReportAtS = nowS;
            nextReportAtS = nowS + ReportIntervalS;
            hasPreviousPosition = true;
        }
    }
}
