using System;
using InhaExpress.Client.Domain;
using InhaExpress.Client.Networking;
using UnityEngine;

namespace InhaExpress.Client.Presentation
{
    public interface IClientView
    {
        void Bind(ClientRuntimeHost host);
    }

    // Bootstrap owns one host/session. Fixture and WebSocket transports share this boundary.
    public sealed class ClientRuntimeHost : MonoBehaviour
    {
        private IClientDataSource source;
        public WorldStateStore Store { get; private set; }
        public FixtureClientDataSource Fixture => source as FixtureClientDataSource;
        public IClientCommandSource Commands => source as IClientCommandSource;
        public ConnectionState ConnectionState => source?.ConnectionState ?? ConnectionState.Disconnected;
        public ClientRole Role { get; private set; }

        public void Initialize(ClientRole role, IClientDataSource dataSource, string subscriberId = null)
        {
            if (source != null) throw new InvalidOperationException("A client session already exists.");
            if (dataSource == null) throw new ArgumentNullException(nameof(dataSource));
            var store = new WorldStateStore(role, subscriberId);
            source = dataSource;
            Role = role;
            Store = store;
            source.SnapshotReceived += Receive;
        }

        public void StartSource() => source.Start(Time.realtimeSinceStartupAsDouble);
        private void Update() => source?.Pump(Time.realtimeSinceStartupAsDouble);
        private void Receive(WorldSnapshotDto snapshot, double receivedAt) => Store.ApplySnapshot(snapshot, receivedAt);

        private void OnDestroy()
        {
            if (source == null) return;
            source.SnapshotReceived -= Receive;
            source.Dispose();
            source = null;
        }
    }
}
