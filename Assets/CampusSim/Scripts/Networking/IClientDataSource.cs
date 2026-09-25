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

    public interface IClientCommandSource
    {
        event Action<ServiceCommandAckDto> CommandAcknowledged;
        void SendPassengerRequest(PassengerRequestCommandDto command);
        void SendCargoRequest(CargoRequestCommandDto command);
        void SendCancelRequest(CancelRequestCommandDto command);
    }

    // Ego-only pose reports from the Unity simulation to Python; no other actor transforms are sent.
    public interface IEgoLocalizationSource
    {
        event Action<EgoLocalizationAckDto> EgoLocalizationAcknowledged;
        void SendEgoLocalization(EgoLocalizationDto observation);
    }

    public interface ISensorObservationSource
    {
        event Action<SensorObservationAckDto> SensorObservationAcknowledged;
        void SendSensorObservation(SensorObservationDto observation);
    }
}
