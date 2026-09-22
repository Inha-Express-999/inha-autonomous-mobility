using System;

namespace InhaExpress.Client.Domain
{
    public sealed class ServiceNeedsDto
    {
        public bool RequiresStepFree { get; }
        public int WheelchairSlots { get; }
        public bool BoardingAssistance { get; }

        public ServiceNeedsDto(bool requiresStepFree, int wheelchairSlots, bool boardingAssistance)
        {
            if (wheelchairSlots < 0) throw new ArgumentOutOfRangeException(nameof(wheelchairSlots));
            if (wheelchairSlots > 0 && !requiresStepFree)
                throw new ArgumentException("Wheelchair slots require step-free access.");
            RequiresStepFree = requiresStepFree;
            WheelchairSlots = wheelchairSlots;
            BoardingAssistance = boardingAssistance;
        }
    }
}
