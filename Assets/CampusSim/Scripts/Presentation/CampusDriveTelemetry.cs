using System.Globalization;
using System.IO;
using InhaExpress.Client.Presentation;
using UnityEngine;
using Unity.Cinemachine;

/// <summary>Opt-in Play-mode measurements; never changes vehicle or camera motion.</summary>
[DefaultExecutionOrder(10000)]
public sealed class CampusDriveTelemetry : MonoBehaviour
{
    private IAutonomousVehicle vehicle;
    private UnityAutonomousClient client;
    private Camera observedCamera;
    private CinemachineBrain observedBrain;
    private PcOperatorControls operatorControls;
    private WheelCollider[] wheels;
    private StreamWriter writer;
    private double started, expires;
    private float nextSample;
    private static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;

    public void Begin(IAutonomousVehicle target, UnityAutonomousClient control, Camera camera,
        string path, float durationSeconds = 600)
    {
        Close();
        vehicle = target;
        client = control;
        observedCamera = camera;
        observedBrain = camera != null ? camera.GetComponent<CinemachineBrain>() : null;
        operatorControls = Object.FindFirstObjectByType<PcOperatorControls>();
        wheels = target.VehicleTransform.GetComponentsInChildren<WheelCollider>();
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path)));
        writer = new StreamWriter(path, false);
        writer.WriteLine("elapsed_s,simulation_s,x,y,z,speed_kmh,vertical_mps,pitch_deg,roll_deg,grounded_wheels,target_kmh,lane_error_m,camera_x,camera_y,camera_z,camera_pitch_deg,camera_roll_deg,camera_follow_requested,camera_blending,camera_live_mode,signed_speed_kmh,heading_deg,wheel_steer_deg,yaw_rate_deg_s,lateral_speed_mps,status");
        started = Time.realtimeSinceStartupAsDouble;
        expires = started + durationSeconds;
        nextSample = 0;
    }

    private void LateUpdate()
    {
        if (writer == null) return;
        if (vehicle == null || vehicle.VehicleTransform == null || Time.realtimeSinceStartupAsDouble >= expires) { Close(); return; }
        if (Time.unscaledTime < nextSample) return;
        nextSample = Time.unscaledTime + .02f;
        var body = vehicle.VehicleBody;
        var p = body.position;
        var angles = body.rotation.eulerAngles;
        int grounded = 0;
        float steer = 0;
        foreach (var wheel in wheels) if (wheel.isGrounded) grounded++;
        foreach (var wheel in wheels) if (Mathf.Abs(wheel.steerAngle) > Mathf.Abs(steer)) steer = wheel.steerAngle;
        var cp = observedCamera != null ? observedCamera.transform.position : Vector3.zero;
        var ca = observedCamera != null ? observedCamera.transform.eulerAngles : Vector3.zero;
        writer.WriteLine(string.Join(",", new[] {
            N(Time.realtimeSinceStartupAsDouble - started), N(Time.time), N(p.x), N(p.y), N(p.z),
            N(body.linearVelocity.magnitude * 3.6), N(body.linearVelocity.y),
            N(Mathf.DeltaAngle(0, angles.x)), N(Mathf.DeltaAngle(0, angles.z)), N(grounded),
            N(client != null ? client.LastTargetSpeedKmh : 0), N(client != null ? client.LaneErrorM : 0),
            N(cp.x), N(cp.y), N(cp.z), N(Mathf.DeltaAngle(0, ca.x)), N(Mathf.DeltaAngle(0, ca.z)),
            N(operatorControls != null && operatorControls.IsFollowing ? 1 : 0),
            N(observedBrain != null && observedBrain.IsBlending ? 1 : 0),
            // 0 unknown, 1 selected-vehicle rig, 2 map rig. Record the actual
            // Cinemachine output separately from the requested UI state.
            N(observedBrain != null && observedBrain.ActiveVirtualCamera != null
                ? (observedBrain.ActiveVirtualCamera.Name == "PC selected vehicle Cinemachine" ? 1
                    : observedBrain.ActiveVirtualCamera.Name == "Campus map Cinemachine" ? 2 : 0) : 0),
            N(Vector3.Dot(body.linearVelocity, vehicle.VehicleTransform.forward) * 3.6), N(angles.y),
            N(steer), N(body.angularVelocity.y * Mathf.Rad2Deg), N(Vector3.Dot(body.linearVelocity, vehicle.VehicleTransform.right)),
            "\"" + (client != null ? client.Status : "no-client").Replace("\"", "\"\"") + "\""
        }));
        if (Time.frameCount % 100 == 0) writer.Flush();
    }

    private static string N(double value) => value.ToString("R", Invariant);
    public void Close() { writer?.Dispose(); writer = null; }
    private void OnDisable() => Close();
    private void OnDestroy() => Close();
}
