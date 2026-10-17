using UnityEngine;
#if UNITY_EDITOR
using UnityEngine.InputSystem;
#endif
using UnityEngine.Serialization;

namespace InhaExpress.Client.Presentation
{
    /// <summary>
    /// WheelCollider-based vehicle movement controller.
    /// </summary>
    [RequireComponent(typeof(Rigidbody))]
    public sealed class VehicleController : MonoBehaviour
    {
        [Header("Driving")]
        [FormerlySerializedAs("maxForwardSpeed")]
        [SerializeField, Min(0f)] private float maxForwardSpeedKmh = 36f;
        [FormerlySerializedAs("maxReverseSpeed")]
        [SerializeField, Min(0f)] private float maxReverseSpeedKmh = 12.6f;
        [SerializeField, Min(0f)] private float motorTorque = 1500f;
        [SerializeField, Min(0f)] private float maxSteerAngle = 30f;
        [SerializeField, Min(0f)] private float brakeTorque = 3000f;

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
        private bool brakeInput;

        /// <summary>
        /// Supplies normalized driving inputs from a runtime controller.
        /// Throttle and steering are clamped to the range -1..1.
        /// </summary>
        public void SetControlInput(float throttle, float steering, bool brake)
        {
            driveInput = Mathf.Clamp(throttle, -1f, 1f);
            steerInput = Mathf.Clamp(steering, -1f, 1f);
            brakeInput = brake;
        }

        private void Awake()
        {
            vehicleBody = GetComponent<Rigidbody>();
            CacheWheels();
        }

#if UNITY_EDITOR
        private void Update()
        {
            Keyboard keyboard = Keyboard.current;
            if (keyboard == null)
            {
                SetControlInput(0f, 0f, false);
                return;
            }

            bool accelerate = keyboard.upArrowKey.isPressed;
            bool reverse = keyboard.downArrowKey.isPressed;
            float throttle = accelerate == reverse ? 0f : accelerate ? 1f : -1f;

            bool steerLeft = keyboard.leftArrowKey.isPressed;
            bool steerRight = keyboard.rightArrowKey.isPressed;
            float steering = steerLeft == steerRight ? 0f : steerLeft ? -1f : 1f;
            SetControlInput(throttle, steering, keyboard.spaceKey.isPressed);
        }
#endif

        private void FixedUpdate()
        {
            ApplySteering();
            ApplyDriveAndBrakes();
            ApplyAntiRollBars();
        }

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
            float steeringAngle = steerInput * maxSteerAngle;
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
            float appliedMotorTorque = brakeInput || atSpeedLimit ? 0f : driveInput * motorTorque;
            float appliedBrakeTorque = brakeInput ? brakeTorque : 0f;

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
