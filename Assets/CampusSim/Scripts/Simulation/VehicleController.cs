using UnityEngine;
using UnityEngine.Serialization;

namespace InhaExpress.Client.Presentation
{
    /// <summary>
    /// WheelCollider-based vehicle movement controller.
    /// </summary>
    [RequireComponent(typeof(Rigidbody))]
    public sealed class VehicleController : MonoBehaviour, IOperatorVehicle
    {
        [Header("PC Operator Binding")]
        [SerializeField] private string operatorVehicleId = "V01";
        public string OperatorVehicleId => operatorVehicleId;
        public Transform VehicleTransform => transform;
        public Rigidbody VehicleBody => vehicleBody != null ? vehicleBody : GetComponent<Rigidbody>();
        [Header("Driving")]
        [FormerlySerializedAs("maxForwardSpeed")]
        [SerializeField, Min(0f)] private float maxForwardSpeedKmh = 36f;
        [FormerlySerializedAs("maxReverseSpeed")]
        [SerializeField, Min(0f)] private float maxReverseSpeedKmh = 12.6f;
        [SerializeField, Min(0f)] private float motorTorque = 1500f;
        [SerializeField, Min(0f)] private float maxSteerAngle = 30f;
        [SerializeField, Min(0f)] private float brakeTorque = 3000f;

        [Header("Drive Command")]
        [SerializeField, Min(0.01f)] private float speedControlGain = 0.5f;
        [SerializeField, Min(0.01f)] private float brakingControlGain = 1f;
        [SerializeField, Min(0f)] private float reverseSwitchStopSpeedMps = 0.2f;
        [SerializeField, Min(0.05f)] private float driveCommandTimeoutSeconds = 0.5f;

        [Header("Anti-Roll Bars")]
        [SerializeField, Min(0f)] private float frontAntiRollStiffness = 5000f;
        [SerializeField, Min(0f)] private float rearAntiRollStiffness = 5000f;

        private Rigidbody vehicleBody;
        private WheelCollider[] frontWheels;
        private WheelCollider[] rearWheels;
        private WheelCollider frontLeftWheel;
        private WheelCollider frontRightWheel;
        private WheelCollider rearLeftWheel;
        private WheelCollider rearRightWheel;
        private float driveInput;
        private float steerInput;
        private float brakeInput;
        private bool driveCommandMode;
        private float targetSpeedMps;
        private float targetSteeringAngleRad;
        private bool emergencyStopRequested;
        private float lastDriveCommandTime;

        /// <summary>
        /// Requests a signed target speed (km/h), front-wheel steering angle (radians), and optional
        /// emergency stop. Positive speed is forward; negative speed is reverse. Positive steering
        /// turns right and negative steering turns left. Values are clamped to this vehicle's limits.
        /// Returns false for non-finite input and requests an emergency stop. Call on Unity's main
        /// thread; network callbacks should enqueue commands for the main thread.
        /// </summary>
        public bool SetDriveCommand(float targetSpeedKmh, float steeringAngleRad, bool emergencyStop = false)
        {
            driveCommandMode = true;
            lastDriveCommandTime = Time.time;

            if (!IsFinite(targetSpeedKmh) || !IsFinite(steeringAngleRad))
            {
                this.targetSpeedMps = 0f;
                targetSteeringAngleRad = 0f;
                emergencyStopRequested = true;
                return false;
            }

            float maxSteeringAngleRad = Mathf.Max(0f, maxSteerAngle) * Mathf.Deg2Rad;
            float clampedTargetSpeedKmh = Mathf.Clamp(
                targetSpeedKmh,
                -Mathf.Max(0f, maxReverseSpeedKmh),
                Mathf.Max(0f, maxForwardSpeedKmh));
            this.targetSpeedMps = clampedTargetSpeedKmh / 3.6f;
            targetSteeringAngleRad = Mathf.Clamp(steeringAngleRad, -maxSteeringAngleRad, maxSteeringAngleRad);
            emergencyStopRequested = emergencyStop;
            return true;
        }

        private void Awake()
        {
            vehicleBody = GetComponent<Rigidbody>();
            CacheWheels();
            // Park until a valid service/controller command arrives. No local keyboard driving.
            SetDriveCommand(0f, 0f, true);
        }

        private void FixedUpdate()
        {
            if (driveCommandMode)
                UpdateDriveCommandInputs();

            ApplySteering();
            ApplyDriveAndBrakes();
            ApplyAntiRollBars();
        }

        private void StoreControlInput(float throttle, float steering, float brake)
        {
            if (!IsFinite(throttle) || !IsFinite(steering) || !IsFinite(brake))
            {
                driveInput = 0f;
                steerInput = 0f;
                brakeInput = 1f;
                return;
            }

            driveInput = Mathf.Clamp(throttle, -1f, 1f);
            steerInput = Mathf.Clamp(steering, -1f, 1f);
            brakeInput = Mathf.Clamp01(brake);
        }

        private void UpdateDriveCommandInputs()
        {
            float commandTimeout = Mathf.Max(driveCommandTimeoutSeconds, Time.fixedDeltaTime);
            bool commandExpired = Time.time - lastDriveCommandTime > commandTimeout;
            if (commandExpired || emergencyStopRequested)
            {
                driveInput = 0f;
                brakeInput = 1f;
                return;
            }

            float forwardSpeed = Vector3.Dot(vehicleBody.linearVelocity, transform.forward);
            if (Mathf.Abs(targetSpeedMps) <= 0.01f)
            {
                driveInput = 0f;
                brakeInput = Mathf.Clamp01(Mathf.Abs(forwardSpeed) * brakingControlGain);
                return;
            }

            if (forwardSpeed * targetSpeedMps < 0f && Mathf.Abs(forwardSpeed) > reverseSwitchStopSpeedMps)
            {
                driveInput = 0f;
                brakeInput = Mathf.Clamp01(Mathf.Abs(forwardSpeed) * brakingControlGain);
                return;
            }

            float targetDirection = Mathf.Sign(targetSpeedMps);
            float speedError = Mathf.Abs(targetSpeedMps) - forwardSpeed * targetDirection;
            if (speedError >= 0f)
            {
                driveInput = targetDirection * Mathf.Clamp01(speedError * speedControlGain);
                brakeInput = 0f;
            }
            else
            {
                driveInput = 0f;
                brakeInput = Mathf.Clamp01(-speedError * brakingControlGain);
            }
        }

        private static bool IsFinite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);

        private void CacheWheels()
        {
            WheelCollider[] wheels = GetComponentsInChildren<WheelCollider>(true);
            if (wheels.Length < 4)
            {
                Debug.LogError($"{nameof(VehicleController)} requires four WheelColliders; found {wheels.Length}.", this);
                enabled = false;
                return;
            }

            float centerZ = 0f;
            for (int i = 0; i < wheels.Length; i++)
            {
                Vector3 localWheelCenter = transform.InverseTransformPoint(wheels[i].transform.TransformPoint(wheels[i].center));
                centerZ += localWheelCenter.z;
            }
            centerZ /= wheels.Length;

            int frontCount = 0;
            int rearCount = 0;
            for (int i = 0; i < wheels.Length; i++)
            {
                Vector3 localWheelCenter = transform.InverseTransformPoint(wheels[i].transform.TransformPoint(wheels[i].center));
                if (localWheelCenter.z >= centerZ)
                    frontCount++;
                else
                    rearCount++;
            }

            if (frontCount == 0 || rearCount == 0)
            {
                Debug.LogError($"{nameof(VehicleController)} could not identify front and rear wheels from their positions.", this);
                enabled = false;
                return;
            }

            frontWheels = new WheelCollider[frontCount];
            rearWheels = new WheelCollider[rearCount];
            int frontIndex = 0;
            int rearIndex = 0;
            for (int i = 0; i < wheels.Length; i++)
            {
                Vector3 localWheelCenter = transform.InverseTransformPoint(wheels[i].transform.TransformPoint(wheels[i].center));
                if (localWheelCenter.z >= centerZ)
                    frontWheels[frontIndex++] = wheels[i];
                else
                    rearWheels[rearIndex++] = wheels[i];
            }

            AssignWheelPair(frontWheels, out frontLeftWheel, out frontRightWheel);
            AssignWheelPair(rearWheels, out rearLeftWheel, out rearRightWheel);
        }

        private void AssignWheelPair(WheelCollider[] axle, out WheelCollider leftWheel, out WheelCollider rightWheel)
        {
            leftWheel = null;
            rightWheel = null;
            float leftX = float.PositiveInfinity;
            float rightX = float.NegativeInfinity;

            for (int i = 0; i < axle.Length; i++)
            {
                Vector3 localWheelCenter = transform.InverseTransformPoint(axle[i].transform.TransformPoint(axle[i].center));
                if (localWheelCenter.x < leftX)
                {
                    leftX = localWheelCenter.x;
                    leftWheel = axle[i];
                }

                if (localWheelCenter.x > rightX)
                {
                    rightX = localWheelCenter.x;
                    rightWheel = axle[i];
                }
            }

            if (axle.Length < 2 || leftWheel == rightWheel)
            {
                Debug.LogError($"{nameof(VehicleController)} requires a left and right WheelCollider on each axle.", this);
                enabled = false;
            }
        }

        private void ApplySteering()
        {
            float steeringAngle = driveCommandMode
                ? targetSteeringAngleRad * Mathf.Rad2Deg
                : steerInput * maxSteerAngle;
            for (int i = 0; i < frontWheels.Length; i++)
                frontWheels[i].steerAngle = steeringAngle;
        }

        private void ApplyDriveAndBrakes()
        {
            float forwardSpeed = Vector3.Dot(vehicleBody.linearVelocity, transform.forward);
            float maxForwardSpeedMps = maxForwardSpeedKmh / 3.6f;
            float maxReverseSpeedMps = maxReverseSpeedKmh / 3.6f;
            bool atSpeedLimit = driveInput > 0f && forwardSpeed >= maxForwardSpeedMps
                                || driveInput < 0f && forwardSpeed <= -maxReverseSpeedMps;
            float appliedMotorTorque = brakeInput > 0f || atSpeedLimit ? 0f : driveInput * motorTorque;
            float appliedBrakeTorque = brakeInput * brakeTorque;

            for (int i = 0; i < rearWheels.Length; i++)
                rearWheels[i].motorTorque = appliedMotorTorque;

            for (int i = 0; i < frontWheels.Length; i++)
                frontWheels[i].motorTorque = 0f;

            for (int i = 0; i < frontWheels.Length; i++)
                frontWheels[i].brakeTorque = appliedBrakeTorque;
            for (int i = 0; i < rearWheels.Length; i++)
                rearWheels[i].brakeTorque = appliedBrakeTorque;
        }

        private void ApplyAntiRollBars()
        {
            ApplyAntiRollBar(frontLeftWheel, frontRightWheel, frontAntiRollStiffness);
            ApplyAntiRollBar(rearLeftWheel, rearRightWheel, rearAntiRollStiffness);
        }

        private void ApplyAntiRollBar(WheelCollider leftWheel, WheelCollider rightWheel, float stiffness)
        {
            if (stiffness <= 0f)
                return;

            float leftTravel = GetSuspensionTravel(leftWheel);
            float rightTravel = GetSuspensionTravel(rightWheel);
            float antiRollForce = (leftTravel - rightTravel) * stiffness;

            Vector3 leftApplicationPoint = leftWheel.transform.TransformPoint(leftWheel.center);
            Vector3 rightApplicationPoint = rightWheel.transform.TransformPoint(rightWheel.center);
            vehicleBody.AddForceAtPosition(-leftWheel.transform.up * antiRollForce, leftApplicationPoint);
            vehicleBody.AddForceAtPosition(rightWheel.transform.up * antiRollForce, rightApplicationPoint);
        }

        private static float GetSuspensionTravel(WheelCollider wheel)
        {
            if (!wheel.GetGroundHit(out WheelHit hit))
                return 1f;

            float suspensionDistance = Mathf.Max(wheel.suspensionDistance, 0.001f);
            float localHitY = wheel.transform.InverseTransformPoint(hit.point).y;
            float suspensionTravel = (wheel.center.y - localHitY - wheel.radius) / suspensionDistance;
            return Mathf.Clamp01(suspensionTravel);
        }
    }
}
