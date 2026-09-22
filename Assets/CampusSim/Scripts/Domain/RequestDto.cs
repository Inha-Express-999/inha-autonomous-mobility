using System;

namespace InhaExpress.Client.Domain
{
    public sealed class RequestDto
    {
        public string Id { get; }
        public string OwnerId { get; }
        public ServiceType ServiceType { get; }
        public RequestStatus Status { get; }
        public double CreatedS { get; }
        public string PickupLandmarkId { get; }
        public string DropoffLandmarkId { get; }
        public string PickupStopId { get; }
        public string DropoffStopId { get; }
        public ServiceNeedsDto ServiceNeeds { get; }
        public int PartySize { get; }
        public double CargoKg { get; }
        public double? LatestArrivalS { get; }
        public double? EtaS { get; }
        public string VehicleId { get; }
        public ReasonCode Reason { get; }

        public RequestDto(string id, string ownerId, ServiceType serviceType, RequestStatus status,
            double createdS, string pickupLandmarkId, string dropoffLandmarkId,
            string pickupStopId, string dropoffStopId, ServiceNeedsDto serviceNeeds,
            int partySize, double cargoKg, string vehicleId = null, double? latestArrivalS = null,
            double? etaS = null, ReasonCode reason = ReasonCode.UNKNOWN)
        {
            Id = DtoGuard.Text(id, nameof(id));
            OwnerId = DtoGuard.Text(ownerId, nameof(ownerId));
            ServiceType = DtoGuard.EnumValue(serviceType, nameof(serviceType));
            Status = DtoGuard.EnumValue(status, nameof(status));
            CreatedS = DtoGuard.NonNegative(createdS, nameof(createdS));
            PickupLandmarkId = DtoGuard.OptionalId(pickupLandmarkId, nameof(pickupLandmarkId));
            DropoffLandmarkId = DtoGuard.OptionalId(dropoffLandmarkId, nameof(dropoffLandmarkId));
            PickupStopId = DtoGuard.OptionalId(pickupStopId, nameof(pickupStopId));
            DropoffStopId = DtoGuard.OptionalId(dropoffStopId, nameof(dropoffStopId));
            ServiceNeeds = serviceNeeds ?? throw new ArgumentNullException(nameof(serviceNeeds));
            if (partySize < 0) throw new ArgumentOutOfRangeException(nameof(partySize));
            PartySize = partySize;
            CargoKg = DtoGuard.NonNegative(cargoKg, nameof(cargoKg));
            VehicleId = DtoGuard.OptionalId(vehicleId, nameof(vehicleId));
            LatestArrivalS = DtoGuard.OptionalNumber(latestArrivalS, nameof(latestArrivalS));
            EtaS = DtoGuard.OptionalNumber(etaS, nameof(etaS));
            Reason = DtoGuard.EnumValue(reason, nameof(reason));
            // Rejected input must remain representable for error screens and operator logs.
            if (status != RequestStatus.REJECTED)
            {
                if (serviceType == ServiceType.PASSENGER &&
                    (pickupLandmarkId == null || dropoffLandmarkId == null ||
                     pickupLandmarkId == dropoffLandmarkId || partySize < 1 || cargoKg != 0 ||
                     serviceNeeds.WheelchairSlots > partySize))
                    throw new ArgumentException("Invalid passenger request.");
                if (serviceType == ServiceType.CARGO && (partySize != 0 || cargoKg <= 0))
                    throw new ArgumentException("Invalid cargo request.");
            }
            if (status == RequestStatus.VALIDATED || status == RequestStatus.QUEUED ||
                status == RequestStatus.ASSIGNED || status == RequestStatus.PICKUP_SERVICE ||
                status == RequestStatus.IN_TRANSIT || status == RequestStatus.DROPOFF_SERVICE ||
                status == RequestStatus.COMPLETED)
            {
                if (pickupStopId == null || dropoffStopId == null)
                    throw new ArgumentException("Validated requests need both server-selected stops.");
            }
            if ((status == RequestStatus.ASSIGNED || status == RequestStatus.PICKUP_SERVICE ||
                 status == RequestStatus.IN_TRANSIT || status == RequestStatus.DROPOFF_SERVICE) && vehicleId == null)
                throw new ArgumentException("An active assigned request needs a vehicle.");
        }
    }
}
