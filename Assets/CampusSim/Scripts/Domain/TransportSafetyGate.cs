using System;

namespace InhaExpress.Client.Domain
{
    // Pure main-thread state machine. This class requests braking; it does not apply Physics.
    // Caller must validate wire payload/authority and call Poll every Update/FixedUpdate.
    // wallNow must use Time.realtimeSinceStartupAsDouble, never Time.time.
    public sealed class TransportSafetyGate
    {
        private readonly double silenceTimeoutS;
        private string run, session, map;
        private long lastSequence = -1;
        private double lastWall = -1, lastSimulation = -1, lastCommandWall, expiresAtSimulation;
        private bool connected, snapshotReady, observationsReady, hasCommand;
        public bool BrakeRequired { get; private set; } = true;
        public string Reason { get; private set; } = "DISCONNECTED";
        public event Action<string> BrakeRequested;

        public TransportSafetyGate(double silenceTimeoutSeconds)
        {
            if (!Finite(silenceTimeoutSeconds) || silenceTimeoutSeconds <= 0)
                throw new ArgumentOutOfRangeException(nameof(silenceTimeoutSeconds));
            silenceTimeoutS = silenceTimeoutSeconds;
        }

        // Call only after authenticated negotiation of a new session, not on arbitrary packets.
        public bool BeginSession(string runId, string sessionId, string mapVersion, double wallNow)
        {
            if (!WallClock(wallNow)) return false;
            if (string.IsNullOrWhiteSpace(runId) || string.IsNullOrWhiteSpace(sessionId) ||
                string.IsNullOrWhiteSpace(mapVersion) || (sessionId == session && runId == run))
            {
                Disconnect("INVALID_SESSION");
                return false;
            }
            run = runId; session = sessionId; map = mapVersion;
            connected = true;
            snapshotReady = observationsReady = hasCommand = false;
            lastSequence = -1;
            lastSimulation = -1;
            Hold("WAITING_FOR_SNAPSHOT");
            return true;
        }

        public bool ConfirmFullSnapshot(string runId, string sessionId, string mapVersion)
        {
            if (!Matches(runId, sessionId, mapVersion) || snapshotReady) return false;
            snapshotReady = true;
            Hold("WAITING_FOR_OBSERVATIONS");
            return true;
        }

        // Call only after validated fresh ego AND required sensor observations for this session.
        public bool ConfirmFreshObservations(string runId, string sessionId, string mapVersion)
        {
            if (!Matches(runId, sessionId, mapVersion) || !snapshotReady) return false;
            observationsReady = true;
            return true;
        }

        // Duplicate/out-of-order/old-session messages never renew the wall-clock lease.
        // accepted command metadata must already be tied to current vehicle, pose and sensors.
        public bool AcceptCommand(string runId, string sessionId, string mapVersion,
            long sequence, double issuedAtSimulation, double validUntilSimulation,
            double simulationNow, double wallNow)
        {
            if (!WallClock(wallNow) || !SimulationClock(simulationNow)) return false;
            PollCore(simulationNow, wallNow);
            if (!Matches(runId, sessionId, mapVersion) || !snapshotReady || !observationsReady ||
                sequence < 0 || sequence <= lastSequence ||
                !Finite(issuedAtSimulation) || !Finite(validUntilSimulation) ||
                issuedAtSimulation < 0 || issuedAtSimulation > simulationNow ||
                validUntilSimulation <= simulationNow || validUntilSimulation <= issuedAtSimulation)
                return false;
            lastSequence = sequence;
            lastCommandWall = wallNow;
            expiresAtSimulation = validUntilSimulation;
            hasCommand = true;
            BrakeRequired = false;
            Reason = "FRESH_COMMAND";
            return true;
        }

        public bool Poll(double simulationNow, double wallNow)
        {
            if (WallClock(wallNow) && SimulationClock(simulationNow))
                PollCore(simulationNow, wallNow);
            return BrakeRequired;
        }

        public void Disconnect(string reason = "DISCONNECTED")
        {
            connected = snapshotReady = observationsReady = hasCommand = false;
            Hold(reason);
        }

        private void PollCore(double simulationNow, double wallNow)
        {
            if (!connected || !hasCommand) return;
            if (wallNow - lastCommandWall >= silenceTimeoutS)
                Disconnect("COMMAND_SILENCE");
            else if (simulationNow >= expiresAtSimulation)
            {
                hasCommand = observationsReady = false;
                Hold("COMMAND_EXPIRED");
            }
        }

        private bool WallClock(double now)
        {
            if (!Finite(now) || now < 0 || now < lastWall)
            {
                Disconnect("INVALID_WALL_CLOCK");
                return false;
            }
            lastWall = now;
            return true;
        }

        private bool SimulationClock(double now)
        {
            if (!Finite(now) || now < 0 || now < lastSimulation)
            {
                Disconnect("INVALID_SIMULATION_CLOCK");
                return false;
            }
            lastSimulation = now;
            return true;
        }

        private bool Matches(string r, string s, string m) => connected && r == run && s == session && m == map;
        private static bool Finite(double v) => !double.IsNaN(v) && !double.IsInfinity(v);
        private void Hold(string reason)
        {
            bool changed = !BrakeRequired || Reason != reason;
            BrakeRequired = true;
            Reason = reason;
            if (changed) BrakeRequested?.Invoke(reason);
        }
    }
}
