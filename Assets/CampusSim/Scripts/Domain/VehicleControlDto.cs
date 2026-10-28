using System;

namespace InhaExpress.Client.Domain
{
    // Actuator intent is distinct from VehicleDto's measured/display projection.
    public sealed class VehicleControlDto
    {
        public string VehicleId { get; }
        public string MapVersion { get; }
        public string SessionId { get; }
        public string RouteId { get; }
        public long Sequence { get; }
        public long EgoPoseTick { get; }
        public double ValidForS { get; }
        public double TargetSpeedMps { get; }
        public double YawRateRadps { get; }
        public string Reason { get; }

        public VehicleControlDto(string vehicleId, string mapVersion, string sessionId,
            string routeId, long sequence, long egoPoseTick, double validForS,
            double targetSpeedMps, double yawRateRadps, string reason)
        {
            VehicleId = DtoGuard.Text(vehicleId, nameof(vehicleId));
            MapVersion = DtoGuard.Text(mapVersion, nameof(mapVersion));
            SessionId = DtoGuard.Text(sessionId, nameof(sessionId));
            RouteId = DtoGuard.OptionalId(routeId, nameof(routeId));
            if (sequence < 0 || egoPoseTick < 0) throw new ArgumentOutOfRangeException(nameof(sequence));
            Sequence = sequence;
            EgoPoseTick = egoPoseTick;
            ValidForS = DtoGuard.Finite(validForS, nameof(validForS));
            if (validForS <= 0 || validForS > 0.3) throw new ArgumentOutOfRangeException(nameof(validForS));
            TargetSpeedMps = DtoGuard.NonNegative(targetSpeedMps, nameof(targetSpeedMps));
            YawRateRadps = DtoGuard.Finite(yawRateRadps, nameof(yawRateRadps));
            Reason = DtoGuard.Text(reason, nameof(reason));
        }
    }
}
