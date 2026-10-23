namespace InhaExpress.Client.Presentation
{
    // Service command adapter. No keyboard, throttle UI, or user steering input.
    public interface IAutonomousVehicle : IOperatorVehicle
    {
        float WheelbaseM { get; }
        float MaximumSteeringRad { get; }
        bool SetDriveCommand(float targetSpeedKmh, float steeringAngleRad, bool emergencyStop = false);
    }
}
