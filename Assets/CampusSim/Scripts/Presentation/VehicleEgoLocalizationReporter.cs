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
        private long observedTick;

        public string VehicleId => vehicleId;
        public long LatestObservedTick => observedTick - 1;

        private void Awake() => body = GetComponent<Rigidbody>();

        public void Configure(string id)
        {
            vehicleId = string.IsNullOrWhiteSpace(id) ? null : id.Trim();
            observedTick = 0;
            hasPreviousPosition = false;
            previousReportAtS = 0.0;
            nextReportAtS = 0.0;
        }

        private void FixedUpdate()
        {
            if (string.IsNullOrEmpty(vehicleId) || body == null) return;
            double nowS = Time.realtimeSinceStartupAsDouble;
            if (nowS < nextReportAtS) return;

            if (runtime == null) runtime = FindFirstObjectByType<ClientRuntimeHost>();
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
            runtime.Localization.SendEgoLocalization(new EgoLocalizationDto(
                vehicleId,
                observedTick++,
                runtime.Store.Current.MapVersion,
                mapPosition,
                headingRad,
                speedMps));

            previousReportedPosition = position;
            previousReportAtS = nowS;
            nextReportAtS = nowS + ReportIntervalS;
            hasPreviousPosition = true;
        }
    }
}
