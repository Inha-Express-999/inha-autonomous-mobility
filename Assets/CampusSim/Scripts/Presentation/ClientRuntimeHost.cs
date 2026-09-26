using System;
using System.Collections.Generic;
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
        private readonly Dictionary<string, long> egoTicks = new Dictionary<string, long>(StringComparer.Ordinal);
        private readonly Dictionary<(string Vehicle, string Sensor), long> sensorTicks =
            new Dictionary<(string Vehicle, string Sensor), long>();

        // Sequence ownership follows the transport session, not disposable Physics actors.
        public long NextEgoTick(string vehicleId)
        {
            ValidateTelemetryOwner(vehicleId);
            egoTicks.TryGetValue(vehicleId, out long next);
            egoTicks[vehicleId] = checked(next + 1);
            return next;
        }

        public long NextSensorTick(string vehicleId, string sensorId)
        {
            ValidateTelemetryOwner(vehicleId);
            if (string.IsNullOrWhiteSpace(sensorId)) throw new ArgumentException("Sensor ID is required.", nameof(sensorId));
            var key = (vehicleId, sensorId);
            sensorTicks.TryGetValue(key, out long next);
            sensorTicks[key] = checked(next + 1);
            return next;
        }

        private void ValidateTelemetryOwner(string vehicleId)
        {
            if (source == null || Role != ClientRole.PC_Operator)
                throw new InvalidOperationException("Telemetry sequence requires an initialized PC session.");
            if (string.IsNullOrWhiteSpace(vehicleId)) throw new ArgumentException("Vehicle ID is required.", nameof(vehicleId));
        }
        public WorldStateStore Store { get; private set; }
        public FixtureClientDataSource Fixture => source as FixtureClientDataSource;
        public IClientCommandSource Commands => source as IClientCommandSource;
        public IEgoLocalizationSource Localization => source as IEgoLocalizationSource;
        public ISensorObservationSource Sensors => source as ISensorObservationSource;
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
