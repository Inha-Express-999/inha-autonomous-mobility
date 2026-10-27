#if UNITY_EDITOR
using System.Collections;
using InhaExpress.Client.Domain;
using InhaExpress.Client.Networking;
using InhaExpress.Client.Presentation;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;

namespace InhaExpress.Client.Tests
{
    public sealed class TeamVehicleBrakePlayModeTests
    {
        [UnityTest]
        public IEnumerator TeamPrefabStartsBrakedAndBindsToLivePcHost()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                "Assets/CampusSim/Prefabs/Vehicles/TransportVehicle_Annyoung.prefab");
            Assert.That(prefab, Is.Not.Null);
            GameObject car = null, hostObject = null;
            try
            {
                car = Object.Instantiate(prefab, new Vector3(0, 10, 0), Quaternion.identity);
                var controller = car.GetComponent<VehicleController>();
                Assert.That(controller, Is.Not.Null);
                Assert.That(controller.enabled, Is.True);
                var body = car.GetComponent<Rigidbody>();
                Assert.That(body.isKinematic, Is.False);
                // This checks torque routing without claiming tire/road stopping distance.
                body.useGravity = false;
                var wheels = car.GetComponentsInChildren<WheelCollider>(true);
                Assert.That(wheels.Length, Is.EqualTo(4));
                var bridge = car.GetComponent<TransportVehicleBrakeBridge>();
                Assert.That(bridge, Is.Not.Null);
                yield return new WaitForFixedUpdate();
                yield return new WaitForFixedUpdate();
                AssertBrakes(bridge, wheels);
                hostObject = new GameObject("team-prefab-host");
                var host = hostObject.AddComponent<ClientRuntimeHost>();
                host.Initialize(ClientRole.PC_Operator,
                    new WebSocketClientDataSource("ws://127.0.0.1:1/v1/client/ws", ClientRole.PC_Operator, "0.4.3.1"));
                var relay = ClientVehicleSafetyBinding.BindVehicle(host, controller);
                Assert.That(relay.Runtime, Is.SameAs(host));
                yield return null;
                yield return new WaitForFixedUpdate();
                AssertBrakes(bridge, wheels);
                Assert.That(relay.LastStopReason, Is.EqualTo("TRANSPORT_DISCONNECTED"));
                Assert.That(bridge.ApplyValidatedCommand("old", "old", "old", 1, 0, 1, 3.6f, 0, false), Is.False);
            }
            finally
            {
                if (hostObject != null) Object.Destroy(hostObject);
                if (car != null) Object.Destroy(car);
            }
            yield return null;
        }
        private static void AssertBrakes(TransportVehicleBrakeBridge bridge, WheelCollider[] wheels)
        {
            Assert.That(bridge.BrakeRequired, Is.True);
            foreach (var wheel in wheels)
            {
                Assert.That(wheel.motorTorque, Is.Zero);
                Assert.That(wheel.brakeTorque, Is.GreaterThan(0));
            }
        }
    }
}
#endif
