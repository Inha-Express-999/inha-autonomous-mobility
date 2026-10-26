using InhaExpress.Client.Domain;
using UnityEngine;

namespace InhaExpress.Client.Presentation
{
    // One-way v3 connection -> stop relay. Deliberately cannot authorize restart or convert yaw to steering.
    [DefaultExecutionOrder(-150)]
    [DisallowMultipleComponent, RequireComponent(typeof(TransportVehicleBrakeBridge))]
    public sealed class ClientConnectionBrakeRelay : MonoBehaviour
    {
        [SerializeField] private ClientRuntimeHost runtime;
        private TransportVehicleBrakeBridge brake;
        private bool holding;
        private int observedGeneration = -1;
        private string observedRun;
        public string LastStopReason { get; private set; }
        public ClientRuntimeHost Runtime => runtime;

        private void Awake() => brake = GetComponent<TransportVehicleBrakeBridge>();

        public void Bind(ClientRuntimeHost host)
        {
            runtime = host;
            observedGeneration = host != null ? host.ConnectionGeneration : -1;
            observedRun = null;
            // A change of transport invalidates all existing movement authority.
            holding = false;
            // Inactive world vehicles bind before Awake; their bridge starts braked on activation.
            if (isActiveAndEnabled) Stop("TRANSPORT_REBOUND");
        }

        private void Update()
        {
            if (runtime == null || !runtime.isActiveAndEnabled || runtime.Store == null || !runtime.IsLivePcTransport)
            {
                Stop("NO_LIVE_PC_TRANSPORT");
                return;
            }
            if (observedGeneration != runtime.ConnectionGeneration)
            {
                observedGeneration = runtime.ConnectionGeneration;
                Stop("CONNECTION_REPLACED");
                return;
            }
            if (runtime.ConnectionState != ConnectionState.Connected)
            {
                Stop("TRANSPORT_DISCONNECTED");
                return;
            }
            if (!runtime.HasCurrentConnectionSnapshot)
            {
                Stop("WAITING_FOR_CURRENT_SNAPSHOT");
                return;
            }
            string currentRun = runtime.Store.Current.RunId;
            if (observedRun != null && observedRun != currentRun)
            {
                observedRun = currentRun;
                Stop("SERVER_RUN_CHANGED");
                return;
            }
            observedRun = currentRun;
            if (runtime.Store.IsStale(Time.realtimeSinceStartupAsDouble))
            {
                Stop("SNAPSHOT_STALE");
                return;
            }
            // Healthy transport does not grant motion. Explicit recovery and a new validated
            // command must pass through TransportVehicleBrakeBridge independently.
            holding = false;
        }

        private void Stop(string reason)
        {
            LastStopReason = reason;
            if (brake == null) brake = GetComponent<TransportVehicleBrakeBridge>();
            if (holding && brake.BrakeRequired) return;
            brake.NotifyDisconnected();
            holding = true;
        }

        private void OnDisable()
        {
            if (brake != null) brake.NotifyDisconnected();
        }
    }
}
