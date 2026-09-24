using System;

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
