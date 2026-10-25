using System;
using System.Threading;
using InhaExpress.Client.Domain;
using UnityEngine;

namespace InhaExpress.Client.Presentation
{
    // Explicit integration boundary: upstream must validate JSON/authority/vehicle/pose/command ID.
    // Not auto-added to existing scenes. All methods except QueueDisconnected run on main thread.
    [DefaultExecutionOrder(-200)]
    [DisallowMultipleComponent, RequireComponent(typeof(VehicleController))]
    public sealed class TransportVehicleBrakeBridge : MonoBehaviour
    {
        [SerializeField, Min(0.05f)] private float commandSilenceTimeoutS = 0.5f;
        private VehicleController vehicle;
        private TransportSafetyGate gate;
        private double simulationNow;
        private int mainThreadId;
        private int disconnectPending;
        private bool bound;
        public bool BrakeRequired => gate == null || gate.BrakeRequired;
        public string Reason => gate?.Reason ?? "NOT_INITIALIZED";
        public int BrakeRequestCount { get; private set; }
        public event Action<string> BrakeRequested;

        private void Awake()
        {
            mainThreadId = Thread.CurrentThread.ManagedThreadId;
            vehicle = GetComponent<VehicleController>();
            gate = new TransportSafetyGate(commandSilenceTimeoutS);
            gate.BrakeRequested += OnBrakeRequired;
            ApplyBrake();
        }

        public bool BeginSession(string run, string session, string map, double currentSimulationTime)
        {
            RequireMainThread();
            // A queued disconnect must be processed before accepting a new connection.
            DrainDisconnect();
            if (!Finite(currentSimulationTime) || currentSimulationTime < 0)
            {
                NotifyDisconnected();
                return false;
            }
            simulationNow = currentSimulationTime;
            bound = gate.BeginSession(run, session, map, Time.realtimeSinceStartupAsDouble);
            ApplyBrake();
            return bound;
        }

        public bool ConfirmFullSnapshot(string run, string session, string map)
        {
            RequireMainThread();
            DrainDisconnect();
            return bound && gate.ConfirmFullSnapshot(run, session, map);
        }

        public bool ConfirmFreshObservations(string run, string session, string map)
        {
            RequireMainThread();
            DrainDisconnect();
            return bound && gate.ConfirmFreshObservations(run, session, map);
        }

        // Advance with validated authoritative simulation time, not arbitrary remote packet time.
        public void SetSimulationTime(double currentSimulationTime)
        {
            RequireMainThread();
            simulationNow = currentSimulationTime;
            CheckSafety();
        }

        public bool ApplyValidatedCommand(string run, string session, string map, long sequence,
            double issuedAt, double validUntil, float targetSpeedKmh, float steeringAngleRad,
            bool emergencyStop)
        {
            RequireMainThread();
            DrainDisconnect();
            if (!Finite(targetSpeedKmh) || !Finite(steeringAngleRad))
            {
                NotifyDisconnected();
                return false;
            }
            if (!bound || !isActiveAndEnabled || vehicle == null || !vehicle.isActiveAndEnabled ||
                !gate.AcceptCommand(run, session, map, sequence, issuedAt, validUntil,
                    simulationNow, Time.realtimeSinceStartupAsDouble))
            {
                if (BrakeRequired) ApplyBrake();
                return false;
            }
            return vehicle.SetDriveCommand(targetSpeedKmh, steeringAngleRad, emergencyStop);
        }

        // May be called by a socket worker; no Unity object access on the worker.
        public void QueueDisconnected() => Interlocked.Exchange(ref disconnectPending, 1);

        public void NotifyDisconnected()
        {
            RequireMainThread();
            bound = false;
            gate.Disconnect("SOCKET_DISCONNECTED");
            ApplyBrake();
        }

        private void DrainDisconnect()
        {
            if (Interlocked.Exchange(ref disconnectPending, 0) != 0) NotifyDisconnected();
        }

        private void Update() => CheckSafety();
        private void FixedUpdate() => CheckSafety();

        private void CheckSafety()
        {
            DrainDisconnect();
            if (gate.Poll(simulationNow, Time.realtimeSinceStartupAsDouble)) ApplyBrake();
        }

        private void OnBrakeRequired(string reason)
        {
            ApplyBrake();
            BrakeRequestCount++;
            BrakeRequested?.Invoke(reason);
        }

        private void ApplyBrake()
        {
            if (vehicle != null) vehicle.SetDriveCommand(0f, 0f, true);
        }

        private void OnDisable()
        {
            bound = false;
            gate?.Disconnect("BRIDGE_DISABLED");
            ApplyBrake();
        }

        private void OnDestroy()
        {
            if (gate != null) gate.BrakeRequested -= OnBrakeRequired;
        }

        private void RequireMainThread()
        {
            if (Thread.CurrentThread.ManagedThreadId != mainThreadId)
                throw new InvalidOperationException("Enqueue socket data and call from Unity main thread.");
        }

        private static bool Finite(double v) => !double.IsNaN(v) && !double.IsInfinity(v);
    }
}
