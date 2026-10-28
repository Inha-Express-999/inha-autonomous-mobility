using System;
using InhaExpress.Client.Domain;
using UnityEngine;

namespace InhaExpress.Client.Presentation
{
    [DisallowMultipleComponent, RequireComponent(typeof(Rigidbody))]
    public sealed class VehicleCommandActuator : MonoBehaviour
    {
        [SerializeField] private float maxSpeedMps = 1f;
        [SerializeField] private float accelerationMps2 = 0.5f;
        [SerializeField] private float brakingMps2 = 2f;
        [SerializeField] private float maxYawRateRadps = Mathf.PI / 2f;
        private Rigidbody body;
        private string vehicleId, mapVersion, sessionId;
        private ControlSequenceGuard sequenceGuard;
        private string runId;
        private long sequence = -1;
        private double expiresAt = double.NegativeInfinity;
        private float targetSpeed, targetYaw, speed;
        public float SpeedMps => speed;
        public bool CommandFresh => Time.realtimeSinceStartupAsDouble < expiresAt;
        public long LastSequence => sequence;

        private void Awake() => body = GetComponent<Rigidbody>();

        public void Configure(string vehicle, string map, string session,
            ControlSequenceGuard sharedGuard = null, string serverRunId = "standalone-test")
        {
            if (string.IsNullOrWhiteSpace(vehicle) || string.IsNullOrWhiteSpace(session) ||
                string.IsNullOrWhiteSpace(map) || !map.StartsWith("synthetic-", StringComparison.Ordinal))
                throw new ArgumentException("Synthetic vehicle/map/session binding required.");
            if (string.IsNullOrWhiteSpace(serverRunId)) throw new ArgumentException("Server run required.");
            if (vehicleId != null && (vehicle != vehicleId || map != mapVersion || session != sessionId || serverRunId != runId))
                throw new InvalidOperationException("Recreate actuator for a different binding.");
            if (sequenceGuard == null) sequenceGuard = sharedGuard ?? new ControlSequenceGuard();
            else if (sharedGuard != null && !ReferenceEquals(sequenceGuard, sharedGuard))
                throw new InvalidOperationException("Cannot replace the actuator sequence history.");
            runId = serverRunId;
            vehicleId = vehicle; mapVersion = map; sessionId = session;
            if (body == null) body = GetComponent<Rigidbody>();
            var follower = GetComponent<VehicleRouteFollower>();
            if (follower != null) follower.enabled = false;
        }

        public bool ApplyControl(VehicleControlDto command, string activeRouteId, long latestPoseTick,
            double basisPoseIssuedAt, double receivedAt)
        {
            if (command == null || vehicleId == null || command.VehicleId != vehicleId ||
                command.MapVersion != mapVersion || command.SessionId != sessionId ||
                command.RouteId != activeRouteId || command.Sequence <= sequence ||
                (activeRouteId == null && (command.TargetSpeedMps > 0 || command.YawRateRadps != 0)) ||
                command.EgoPoseTick > latestPoseTick || latestPoseTick - command.EgoPoseTick > 2 ||
                double.IsNaN(basisPoseIssuedAt) || double.IsInfinity(basisPoseIssuedAt) ||
                double.IsNaN(receivedAt) || double.IsInfinity(receivedAt) ||
                basisPoseIssuedAt < 0 || receivedAt < basisPoseIssuedAt ||
                receivedAt >= basisPoseIssuedAt + command.ValidForS)
            {
                Revoke();
                return false;
            }
            if (!sequenceGuard.TryAccept(runId, command)) { Revoke(); return false; }
            sequence = command.Sequence;
            // Bind expiry to locally issued pose time; delayed delivery/replay cannot renew it.
            expiresAt = basisPoseIssuedAt + command.ValidForS;
            targetSpeed = Mathf.Min(maxSpeedMps, (float)command.TargetSpeedMps);
            targetYaw = Mathf.Clamp((float)command.YawRateRadps, -maxYawRateRadps, maxYawRateRadps);
            return true;
        }

        public void Revoke()
        {
            expiresAt = double.NegativeInfinity;
            targetSpeed = targetYaw = 0;
        }

        private void FixedUpdate()
        {
            if (body == null || vehicleId == null) return;
            var follower = GetComponent<VehicleRouteFollower>();
            if (follower != null && follower.enabled) { follower.enabled = false; Revoke(); }
            float dt = Time.fixedDeltaTime;
            if (dt <= 0) return;
            bool fresh = CommandFresh;
            float desired = fresh ? targetSpeed : 0;
            float previous = speed;
            speed = Mathf.MoveTowards(speed, desired, (desired < speed ? brakingMps2 : accelerationMps2) * dt);
            float yaw = fresh ? targetYaw : 0;
            if (desired == 0 && speed > 0.01f) yaw = 0;
            float angle = yaw * dt;
            float distance = (previous + speed) * 0.5f * dt;
            float chordScale = Mathf.Abs(angle) < 1e-5f ? 1f : Mathf.Sin(angle / 2) / (angle / 2);
            Vector3 direction = body.rotation * Quaternion.Euler(0, angle * Mathf.Rad2Deg / 2, 0) * Vector3.forward;
            body.MovePosition(body.position + direction * (distance * chordScale));
            body.MoveRotation(body.rotation * Quaternion.Euler(0, angle * Mathf.Rad2Deg, 0));
        }
    }
}
