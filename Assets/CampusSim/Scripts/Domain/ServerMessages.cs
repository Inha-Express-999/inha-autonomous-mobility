using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace InhaExpress.Client.Domain
{
    // Event metadata only. Full snapshots remain the sole state update path at this stage.
    public sealed class ServerEventDto
    {
        public string RunId { get; }
        public string MessageId { get; }
        public long Sequence { get; }
        public long SimulationTick { get; }
        public ServerEventType Type { get; }
        public string EntityId { get; }
        public ReasonCode Reason { get; }
        public ServerEventDto(string runId, string messageId, long sequence, long simulationTick,
            ServerEventType type, string entityId, ReasonCode reason)
        {
            RunId = DtoGuard.Text(runId, nameof(runId));
            MessageId = DtoGuard.Text(messageId, nameof(messageId));
            if (sequence < 0 || simulationTick < 0) throw new ArgumentOutOfRangeException(nameof(sequence));
            Sequence = sequence;
            SimulationTick = simulationTick;
            Type = DtoGuard.EnumValue(type, nameof(type));
            EntityId = DtoGuard.OptionalId(entityId, nameof(entityId));
            Reason = DtoGuard.EnumValue(reason, nameof(reason));
        }
    }

    public sealed class CommandAckDto
    {
        public string RunId { get; }
        public string MessageId { get; }
        public bool Accepted { get; }
        public long? AppliedTick { get; }
        public string RejectionCode { get; }
        public CommandAckDto(string runId, string messageId, bool accepted, long? appliedTick,
            string rejectionCode = null)
        {
            RunId = DtoGuard.Text(runId, nameof(runId));
            MessageId = DtoGuard.Text(messageId, nameof(messageId));
            if (appliedTick < 0 || (accepted && (!appliedTick.HasValue || rejectionCode != null)) ||
                (!accepted && (appliedTick.HasValue || string.IsNullOrWhiteSpace(rejectionCode))))
                throw new ArgumentException("Inconsistent command acknowledgement.");
            Accepted = accepted;
            AppliedTick = appliedTick;
            RejectionCode = rejectionCode;
        }
    }

    public sealed class PassengerRequestCommandDto
    {
        public string MessageId { get; }
        public string PickupLandmarkId { get; }
        public string DropoffLandmarkId { get; }
        public ServiceNeedsDto ServiceNeeds { get; }
        public int PartySize { get; }
        public double? LatestArrivalS { get; }

        public PassengerRequestCommandDto(string pickupLandmarkId, string dropoffLandmarkId,
            ServiceNeedsDto serviceNeeds, int partySize = 1, double? latestArrivalS = null,
            string messageId = null)
        {
            MessageId = DtoGuard.Text(messageId ?? Guid.NewGuid().ToString("N"), nameof(messageId));
            PickupLandmarkId = DtoGuard.Text(pickupLandmarkId, nameof(pickupLandmarkId));
            DropoffLandmarkId = DtoGuard.Text(dropoffLandmarkId, nameof(dropoffLandmarkId));
            if (PickupLandmarkId == DropoffLandmarkId)
                throw new ArgumentException("Pickup and dropoff landmarks must differ.");
            ServiceNeeds = serviceNeeds ?? throw new ArgumentNullException(nameof(serviceNeeds));
            if (partySize < 1 || ServiceNeeds.WheelchairSlots > partySize)
                throw new ArgumentOutOfRangeException(nameof(partySize));
            PartySize = partySize;
            LatestArrivalS = DtoGuard.OptionalNumber(latestArrivalS, nameof(latestArrivalS));
        }
    }

    public sealed class CargoRequestCommandDto
    {
        public string MessageId { get; }
        public string PickupLandmarkId { get; }
        public string DropoffLandmarkId { get; }
        public double CargoKg { get; }
        public double? LatestArrivalS { get; }

        public CargoRequestCommandDto(string pickupLandmarkId, string dropoffLandmarkId,
            double cargoKg, double? latestArrivalS = null, string messageId = null)
        {
            MessageId = DtoGuard.Text(messageId ?? Guid.NewGuid().ToString("N"), nameof(messageId));
            PickupLandmarkId = DtoGuard.Text(pickupLandmarkId, nameof(pickupLandmarkId));
            DropoffLandmarkId = DtoGuard.Text(dropoffLandmarkId, nameof(dropoffLandmarkId));
            if (PickupLandmarkId == DropoffLandmarkId)
                throw new ArgumentException("Pickup and dropoff landmarks must differ.");
            CargoKg = DtoGuard.Finite(cargoKg, nameof(cargoKg));
            if (CargoKg <= 0.0) throw new ArgumentOutOfRangeException(nameof(cargoKg));
            LatestArrivalS = DtoGuard.OptionalNumber(latestArrivalS, nameof(latestArrivalS));
        }
    }

    public sealed class CancelRequestCommandDto
    {
        public string MessageId { get; }
        public string RequestId { get; }

        public CancelRequestCommandDto(string requestId, string messageId = null)
        {
            MessageId = DtoGuard.Text(messageId ?? Guid.NewGuid().ToString("N"), nameof(messageId));
            RequestId = DtoGuard.Text(requestId, nameof(requestId));
        }
    }

    public sealed class EgoLocalizationDto
    {
        public string VehicleId { get; }
        public long ObservedTick { get; }
        public string MapVersion { get; }
        public MapPositionDto Position { get; }
        public double HeadingRad { get; }
        public double SpeedMps { get; }

        public EgoLocalizationDto(string vehicleId, long observedTick, string mapVersion,
            MapPositionDto position, double headingRad, double speedMps)
        {
            VehicleId = DtoGuard.Text(vehicleId, nameof(vehicleId));
            if (observedTick < 0) throw new ArgumentOutOfRangeException(nameof(observedTick));
            ObservedTick = observedTick;
            MapVersion = DtoGuard.Text(mapVersion, nameof(mapVersion));
            Position = position;
            HeadingRad = DtoGuard.Finite(headingRad, nameof(headingRad));
            SpeedMps = DtoGuard.NonNegative(speedMps, nameof(speedMps));
        }
    }

    public sealed class EgoLocalizationAckDto
    {
        public string VehicleId { get; }
        public string SessionId { get; }
        public long ObservedTick { get; }
        public bool Accepted { get; }
        public string ErrorCode { get; }

        public EgoLocalizationAckDto(string vehicleId, string sessionId, long observedTick, bool accepted,
            string errorCode)
        {
            VehicleId = DtoGuard.Text(vehicleId, nameof(vehicleId));
            SessionId = DtoGuard.Text(sessionId, nameof(sessionId));
            if (observedTick < 0) throw new ArgumentOutOfRangeException(nameof(observedTick));
            ObservedTick = observedTick;
            if (accepted == (errorCode != null))
                throw new ArgumentException("Accepted localization has no error; rejected localization needs one.");
            Accepted = accepted;
            ErrorCode = errorCode;
        }
    }

    public sealed class SensorDetectionDto
    {
        public double RangeM { get; }
        public double BearingRad { get; }
        public MapPositionDto LocalPositionM { get; }
        public SensorEntityClass EntityClass { get; }
        public double? RelativeSpeedMps { get; }
        public string EntityId { get; }

        public SensorDetectionDto(double rangeM, double bearingRad, MapPositionDto localPositionM,
            SensorEntityClass entityClass, double? relativeSpeedMps = null, string entityId = null)
        {
            RangeM = DtoGuard.Finite(rangeM, nameof(rangeM));
            BearingRad = DtoGuard.Finite(bearingRad, nameof(bearingRad));
            if (RangeM <= 0.0 || BearingRad < -Math.PI || BearingRad > Math.PI)
                throw new ArgumentOutOfRangeException(nameof(rangeM));
            LocalPositionM = localPositionM;
            EntityClass = DtoGuard.EnumValue(entityClass, nameof(entityClass));
            EntityId = DtoGuard.OptionalId(entityId, nameof(entityId));
            if (EntityId != null && EntityId.Length > 128)
                throw new ArgumentOutOfRangeException(nameof(entityId));
            RelativeSpeedMps = relativeSpeedMps.HasValue
                ? DtoGuard.Finite(relativeSpeedMps.Value, nameof(relativeSpeedMps))
                : (double?)null;

            var horizontalRange = Math.Sqrt(LocalPositionM.X * LocalPositionM.X +
                                            LocalPositionM.Y * LocalPositionM.Y);
            if (Math.Abs(RangeM - horizontalRange) > Math.Max(0.05, RangeM * 0.01))
                throw new ArgumentException("Range must match the sensor-local horizontal position.");
            var expectedBearing = Math.Atan2(LocalPositionM.Y, LocalPositionM.X);
            var bearingError = Math.Atan2(Math.Sin(BearingRad - expectedBearing),
                Math.Cos(BearingRad - expectedBearing));
            if (Math.Abs(bearingError) > 0.02)
                throw new ArgumentException("Bearing must match the sensor-local position.");
        }
    }

    public sealed class SensorObservationDto
    {
        public string VehicleId { get; }
        public string SensorId { get; }
        public SensorType SensorType { get; }
        public long ObservedTick { get; }
        public long EgoPoseTick { get; }
        public string MapVersion { get; }
        public bool Valid { get; }
        public ReadOnlyCollection<SensorDetectionDto> Detections { get; }
        public double? ObservedTimeS { get; }
        public MapPositionDto? SensorPositionM { get; }
        public double? SensorHeadingRad { get; }

        public SensorObservationDto(string vehicleId, string sensorId, SensorType sensorType,
            long observedTick, long egoPoseTick, string mapVersion, bool valid,
            IEnumerable<SensorDetectionDto> detections, double? observedTimeS = null,
            MapPositionDto? sensorPositionM = null, double? sensorHeadingRad = null)
        {
            VehicleId = DtoGuard.Text(vehicleId, nameof(vehicleId));
            SensorId = DtoGuard.Text(sensorId, nameof(sensorId));
            SensorType = DtoGuard.EnumValue(sensorType, nameof(sensorType));
            if (observedTick < 0 || egoPoseTick < 0)
                throw new ArgumentOutOfRangeException(nameof(observedTick));
            ObservedTick = observedTick;
            EgoPoseTick = egoPoseTick;
            MapVersion = DtoGuard.Text(mapVersion, nameof(mapVersion));
            Valid = valid;
            Detections = DtoGuard.Copy(detections, nameof(detections));
            if (observedTimeS.HasValue != sensorPositionM.HasValue ||
                observedTimeS.HasValue != sensorHeadingRad.HasValue)
                throw new ArgumentException("Capture time and sensor pose must be supplied together.");
            ObservedTimeS = DtoGuard.OptionalNumber(observedTimeS, nameof(observedTimeS));
            SensorPositionM = sensorPositionM;
            SensorHeadingRad = sensorHeadingRad.HasValue
                ? DtoGuard.Finite(sensorHeadingRad.Value, nameof(sensorHeadingRad)) : (double?)null;
            if (Detections.Count > 64)
                throw new ArgumentOutOfRangeException(nameof(detections), "At most 64 detections are supported.");
            if (SensorType == SensorType.LIDAR_2D)
                foreach (var detection in Detections)
                    if (detection.RelativeSpeedMps.HasValue)
                        throw new ArgumentException("LIDAR detections cannot include relative speed.");
        }
    }

    public sealed class SensorObservationAckDto
    {
        public string VehicleId { get; }
        public string SensorId { get; }
        public string SessionId { get; }
        public long ObservedTick { get; }
        public bool Accepted { get; }
        public string ErrorCode { get; }

        public SensorObservationAckDto(string vehicleId, string sensorId, string sessionId,
            long observedTick, bool accepted, string errorCode)
        {
            VehicleId = DtoGuard.Text(vehicleId, nameof(vehicleId));
            SensorId = DtoGuard.Text(sensorId, nameof(sensorId));
            SessionId = DtoGuard.Text(sessionId, nameof(sessionId));
            if (observedTick < 0) throw new ArgumentOutOfRangeException(nameof(observedTick));
            ObservedTick = observedTick;
            if (accepted == (errorCode != null))
                throw new ArgumentException("Accepted observation has no error; rejection needs one.");
            Accepted = accepted;
            ErrorCode = errorCode;
        }
    }

    public sealed class ServiceCommandAckDto
    {
        public string MessageId { get; }
        public string CommandType { get; }
        public bool Accepted { get; }
        public RequestDto Request { get; }
        public string ErrorCode { get; }
        public string ErrorMessage { get; }

        public ServiceCommandAckDto(string messageId, string commandType, bool accepted,
            RequestDto request, string errorCode, string errorMessage)
        {
            MessageId = DtoGuard.Text(messageId, nameof(messageId));
            CommandType = DtoGuard.Text(commandType, nameof(commandType));
            if (accepted && (request == null || errorCode != null) ||
                !accepted && (request != null || string.IsNullOrWhiteSpace(errorCode)))
                throw new ArgumentException("Inconsistent service command acknowledgement.");
            Accepted = accepted;
            Request = request;
            ErrorCode = errorCode;
            ErrorMessage = errorMessage;
        }
    }
}
