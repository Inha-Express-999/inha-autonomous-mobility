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
}
