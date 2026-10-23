using UnityEngine;

namespace InhaExpress.Client.Presentation
{
    // Lets the PC UI use the existing Assembly-CSharp controller without moving its assembly/GUID.
    public interface IOperatorVehicle
    {
        string OperatorVehicleId { get; }
        Transform VehicleTransform { get; }
        Rigidbody VehicleBody { get; }
    }
}
