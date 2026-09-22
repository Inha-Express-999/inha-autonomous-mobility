using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace InhaExpress.Client.Domain
{
    public sealed class LandmarkDto
    {
        public string Id { get; }
        public string Name { get; }
        public string CampusId { get; }
        public VerificationStatus Verification { get; }
        public ReadOnlyCollection<string> StopIds { get; }
        public LandmarkDto(string id, string name, string campusId, VerificationStatus verification,
            IEnumerable<string> stopIds)
        {
            Id = DtoGuard.Text(id, nameof(id));
            Name = DtoGuard.Text(name, nameof(name));
            CampusId = DtoGuard.Text(campusId, nameof(campusId));
            Verification = DtoGuard.EnumValue(verification, nameof(verification));
            StopIds = DtoGuard.Copy(stopIds, nameof(stopIds));
            foreach (var stop in StopIds) DtoGuard.Text(stop, nameof(stopIds));
        }
    }

    public sealed class StopDto
    {
        public string Id { get; }
        public string LandmarkId { get; }
        public string EntranceId { get; }
        public MapPositionDto Position { get; }
        public bool? StepFreeAccess { get; }
        public VerificationStatus Verification { get; }
        public StopDto(string id, string landmarkId, string entranceId, MapPositionDto position,
            bool? stepFreeAccess, VerificationStatus verification)
        {
            Id = DtoGuard.Text(id, nameof(id));
            LandmarkId = DtoGuard.Text(landmarkId, nameof(landmarkId));
            EntranceId = DtoGuard.OptionalId(entranceId, nameof(entranceId));
            Position = position;
            StepFreeAccess = stepFreeAccess;
            Verification = DtoGuard.EnumValue(verification, nameof(verification));
        }
    }

    public sealed class RouteDto
    {
        public string Id { get; }
        public string MapVersion { get; }
        public ReadOnlyCollection<MapPositionDto> Polyline { get; }
        public ReasonCode Reason { get; }
        public RouteDto(string id, string mapVersion, IEnumerable<MapPositionDto> polyline,
            ReasonCode reason = ReasonCode.UNKNOWN)
        {
            Id = DtoGuard.Text(id, nameof(id));
            MapVersion = DtoGuard.Text(mapVersion, nameof(mapVersion));
            Polyline = DtoGuard.Copy(polyline, nameof(polyline));
            Reason = DtoGuard.EnumValue(reason, nameof(reason));
            if (Polyline.Count == 0) throw new System.ArgumentException("Route geometry is empty.");
        }
    }

    public sealed class ZoneDto
    {
        public string Id { get; }
        public ZoneStatus Status { get; }
        public double? ObservedDensity { get; }
        public double? EmaDensity { get; }
        public double? PriorDensity { get; }
        public ReasonCode Reason { get; }
        public ZoneDto(string id, ZoneStatus status, double? observedDensity, double? emaDensity,
            double? priorDensity, ReasonCode reason = ReasonCode.UNKNOWN)
        {
            Id = DtoGuard.Text(id, nameof(id));
            Status = DtoGuard.EnumValue(status, nameof(status));
            ObservedDensity = DtoGuard.OptionalNumber(observedDensity, nameof(observedDensity));
            EmaDensity = DtoGuard.OptionalNumber(emaDensity, nameof(emaDensity));
            PriorDensity = DtoGuard.OptionalNumber(priorDensity, nameof(priorDensity));
            Reason = DtoGuard.EnumValue(reason, nameof(reason));
        }
    }
}
