namespace InhaExpress.Client.Domain
{
    public enum ClientRole { PC_Operator, Mobile_Passenger }
    public enum ServiceType { PASSENGER, CARGO }
    public enum RequestStatus
    {
        CREATED, VALIDATED, QUEUED, ASSIGNED, PICKUP_SERVICE, IN_TRANSIT,
        DROPOFF_SERVICE, COMPLETED, REJECTED, CANCELLED, EXPIRED, FAILED
    }
    public enum VehicleMissionState
    {
        IDLE, TO_PICKUP, PICKUP_SERVICE, TO_DROPOFF, DROPOFF_SERVICE,
        TO_CHARGER, CHARGING, OUT_OF_SERVICE
    }
    public enum VehicleMotionState { DRIVING, YIELDING, REPLANNING, EMERGENCY_STOP, WAITING_RESOURCE }
    public enum ZoneStatus { NORMAL, CAUTION, AVOID, CLOSED }
    public enum VerificationStatus { UNKNOWN, SYNTHETIC, VERIFIED }
    public enum ReasonCode
    {
        UNKNOWN, CROWD_AVOIDANCE, ZONE_CLOSED, NO_ACCESSIBLE_ALTERNATIVE,
        PEDESTRIAN, ROAD_CLOSED, VEHICLE_FAILURE, STALE_LOCALIZATION,
        SENSOR_DATA_STALE, SENSOR_INVALID, OBSTACLE_STOP, SAFETY_RESUME_HOLD,
        RESOURCE_WAIT, RESOURCE_STATE_UNAVAILABLE
    }
    public enum ConnectionState { Disconnected, Connecting, Connected, Reconnecting }
    public enum ServerEventType { RequestUpdated, RouteUpdated, ZoneUpdated, Error }
    public enum SensorType { LIDAR_2D, RADAR }
    public enum SensorEntityClass { PEDESTRIAN, VEHICLE, STATIC_OBSTACLE, UNKNOWN }
}
