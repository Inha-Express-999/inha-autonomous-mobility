using System;
using InhaExpress.Client.Domain;

namespace InhaExpress.Client.Networking
{
    // Recorded UI example, NOT a dispatch, motion, safety or network simulation.
    public sealed class FixtureClientDataSource : IClientDataSource
    {
        public const double TickIntervalS = 0.05;
        public const double SnapshotIntervalS = 0.1;
        private readonly ClientRole role;
        private readonly string subscriberId;
        private readonly string projectVersion;
        private readonly string sessionId;
        private double lastNow, elapsed, lastDelivery;
        private long sequence;
        private int runNumber;
        private bool started, disposed;
        public bool IsPaused { get; private set; }
        public bool DeliveryEnabled { get; private set; } = true;
        public ConnectionState ConnectionState { get; private set; } = ConnectionState.Disconnected;
        public event Action<WorldSnapshotDto, double> SnapshotReceived;

        public FixtureClientDataSource(ClientRole role, string projectVersion,
            string subscriberId = null, string sessionId = null)
        {
            if (!Enum.IsDefined(typeof(ClientRole), role)) throw new ArgumentOutOfRangeException(nameof(role));
            if (role == ClientRole.Mobile_Passenger && string.IsNullOrWhiteSpace(subscriberId))
                throw new ArgumentException("A fixture subscriber is required.", nameof(subscriberId));
            this.role = role;
            this.projectVersion = projectVersion;
            this.subscriberId = subscriberId;
            this.sessionId = sessionId ?? Guid.NewGuid().ToString("N");
            // Validate version/configuration before starting or emitting events.
            FixtureScenario.Create(role, subscriberId, projectVersion, this.sessionId, 0, 0);
        }

        public void Start(double monotonicNowS)
        {
            CheckClock(monotonicNowS);
            if (started) return;
            started = true;
            lastNow = monotonicNowS;
            ConnectionState = ConnectionState.Connected;
            Publish(monotonicNowS);
        }

        public void Pump(double monotonicNowS)
        {
            CheckClock(monotonicNowS);
            if (!started) return;
            if (!IsPaused) elapsed = Math.Min(FixtureScenario.DurationS, elapsed + monotonicNowS - lastNow);
            lastNow = monotonicNowS;
            // Skip obsolete frames after a stall; never flood the UI with catch-up snapshots.
            if (DeliveryEnabled && monotonicNowS - lastDelivery + 1e-9 >= SnapshotIntervalS)
                Publish(monotonicNowS);
        }

        public void SetPaused(bool paused, double monotonicNowS)
        {
            RequireStarted();
            Pump(monotonicNowS);
            IsPaused = paused;
        }

        public void SetDeliveryEnabled(bool enabled, double monotonicNowS)
        {
            RequireStarted();
            Pump(monotonicNowS);
            if (DeliveryEnabled == enabled) return;
            DeliveryEnabled = enabled;
            ConnectionState = enabled ? ConnectionState.Connected : ConnectionState.Disconnected;
            if (enabled) Publish(monotonicNowS); // Complete current snapshot, no stale backlog.
        }

        public void Restart(double monotonicNowS)
        {
            RequireStarted();
            CheckClock(monotonicNowS);
            lastNow = monotonicNowS;
            elapsed = 0;
            sequence = 0;
            runNumber++;
            IsPaused = false;
            if (DeliveryEnabled) Publish(monotonicNowS);
        }

        private void Publish(double now)
        {
            long tick = (long)Math.Floor((elapsed + 1e-9) / TickIntervalS);
            var snapshot = FixtureScenario.Create(role, subscriberId, projectVersion,
                "fixture-" + sessionId + "-" + runNumber, sequence++, tick);
            lastDelivery = now;
            SnapshotReceived?.Invoke(snapshot, now);
        }

        private void CheckClock(double now)
        {
            if (disposed) throw new ObjectDisposedException(nameof(FixtureClientDataSource));
            if (double.IsNaN(now) || double.IsInfinity(now) || now < 0 || (started && now < lastNow))
                throw new ArgumentOutOfRangeException(nameof(now), "Use a monotonic real-time clock.");
        }

        private void RequireStarted()
        {
            if (disposed) throw new ObjectDisposedException(nameof(FixtureClientDataSource));
            if (!started) throw new InvalidOperationException("Start the source first.");
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            ConnectionState = ConnectionState.Disconnected;
            SnapshotReceived = null;
        }
    }
}
