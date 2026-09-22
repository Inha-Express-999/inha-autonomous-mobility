using System;
using InhaExpress.Client.Domain;

namespace InhaExpress.Client.Networking
{
    // Deliver complete role-scoped snapshots on Unity's main thread. No UI or Physics ownership.
    public interface IClientDataSource : IDisposable
    {
        ConnectionState ConnectionState { get; }
        event Action<WorldSnapshotDto, double> SnapshotReceived;
        void Start(double monotonicNowS);
        void Pump(double monotonicNowS);
    }
}
