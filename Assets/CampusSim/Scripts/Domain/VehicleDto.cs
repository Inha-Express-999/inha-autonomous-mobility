namespace InhaExpress.Client.Domain
{
    // Display projection only; not an actuator command or a planning input.
    public sealed class VehicleDto
    {
        public string Id { get; }
        public MapPositionDto Position { get; }
        public double HeadingRad { get; }
        public double SpeedMps { get; }
        public double? BatteryWh { get; }
        public VehicleMissionState MissionState { get; }
        public VehicleMotionState MotionState { get; }
        public ReasonCode Reason { get; }
        public string RequestId { get; }
        public string RouteId { get; }

        public VehicleDto(string id, MapPositionDto position, double headingRad, double speedMps,
            double? batteryWh, VehicleMissionState missionState, VehicleMotionState motionState,
            string requestId = null, string routeId = null, ReasonCode reason = ReasonCode.UNKNOWN)
        {
            Id = DtoGuard.Text(id, nameof(id));
            Position = position;
            HeadingRad = DtoGuard.Finite(headingRad, nameof(headingRad));
            SpeedMps = DtoGuard.NonNegative(speedMps, nameof(speedMps));
            BatteryWh = DtoGuard.OptionalNumber(batteryWh, nameof(batteryWh));
            MissionState = DtoGuard.EnumValue(missionState, nameof(missionState));
            MotionState = DtoGuard.EnumValue(motionState, nameof(motionState));
            RequestId = DtoGuard.OptionalId(requestId, nameof(requestId));
            RouteId = DtoGuard.OptionalId(routeId, nameof(routeId));
            Reason = DtoGuard.EnumValue(reason, nameof(reason));
        }
    }
}
