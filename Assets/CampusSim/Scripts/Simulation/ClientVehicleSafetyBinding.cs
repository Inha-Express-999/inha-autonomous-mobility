using System;
using InhaExpress.Client.Domain;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace InhaExpress.Client.Presentation
{
    // Stop-only integration for the existing v3 PC transport; never grants driving authority.
    public static class ClientVehicleSafetyBinding
    {
        public static ClientConnectionBrakeRelay BindVehicle(ClientRuntimeHost host, VehicleController vehicle)
        {
            if (host == null || !host.IsLivePcTransport)
                throw new InvalidOperationException("Vehicle safety requires a live PC WebSocket runtime.");
            if (vehicle == null) throw new ArgumentNullException(nameof(vehicle));
            var body = vehicle.GetComponent<Rigidbody>();
            if (body == null || body.isKinematic || vehicle.GetComponentsInChildren<WheelCollider>(true).Length != 4)
                throw new InvalidOperationException("Use a dynamic Rigidbody with four WheelColliders.");
            if (vehicle.GetComponent<VehicleRouteFollower>() != null ||
                vehicle.GetComponent<VehicleCommandActuator>() != null)
                throw new InvalidOperationException("Synthetic movement and WheelCollider control cannot share a vehicle.");
            var relay = vehicle.GetComponent<ClientConnectionBrakeRelay>();
            if (relay != null && relay.Runtime != null && relay.Runtime != host)
                throw new InvalidOperationException("A different PC runtime already owns this vehicle safety binding.");
            if (relay == null) relay = vehicle.gameObject.AddComponent<ClientConnectionBrakeRelay>();
            vehicle.SetDriveCommand(0, 0, true);
            relay.enabled = true;
            vehicle.GetComponent<TransportVehicleBrakeBridge>().enabled = true;
            relay.Bind(host);
            return relay;
        }

        public static int BindScene(ClientRuntimeHost host, Scene scene)
        {
            if (host == null || !host.IsLivePcTransport || !scene.IsValid() || !scene.isLoaded) return 0;
            int count = 0;
            foreach (var root in scene.GetRootGameObjects())
                foreach (var vehicle in root.GetComponentsInChildren<VehicleController>(true))
                {
                    BindVehicle(host, vehicle);
                    count++;
                }
            return count;
        }
    }
}
