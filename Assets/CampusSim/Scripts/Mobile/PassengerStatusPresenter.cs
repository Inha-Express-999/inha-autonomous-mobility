using System.Text;
using InhaExpress.Client.Domain;
using InhaExpress.Client.Presentation;

namespace InhaExpress.Client.Mobile
{
    public sealed class PassengerStatusPresenter : FixtureStatusPresenter
    {
        public override ClientRole Role => ClientRole.Mobile_Passenger;
        protected override string Format(WorldSnapshotDto snapshot) => Describe(snapshot);

        public static string Describe(WorldSnapshotDto snapshot)
        {
            var text = new StringBuilder();
            text.AppendLine($"Project {snapshot.ProjectVersion}\nMap {snapshot.MapVersion}");
            if (snapshot.Requests.Count == 0) text.AppendLine("No request for this fixture subscriber.");
            foreach (var request in snapshot.Requests)
            {
                text.AppendLine($"\n{request.Id}: {StatusText(request.Status)}");
                text.AppendLine($"{request.PickupLandmarkId} -> {request.DropoffLandmarkId}");
                text.AppendLine($"Pickup: {request.PickupStopId ?? "not decided"}\nDropoff: {request.DropoffStopId ?? "not decided"}");
                text.AppendLine($"Vehicle: {request.VehicleId ?? "not assigned"}");
                text.AppendLine($"Recorded ETA: {request.EtaS?.ToString("F1") ?? "unknown"} s");
            }
            foreach (var vehicle in snapshot.Vehicles)
            {
                if (vehicle.MotionState == VehicleMotionState.YIELDING && vehicle.Reason == ReasonCode.PEDESTRIAN)
                    text.AppendLine("Yielding to a pedestrian (recorded example).");
                else if (vehicle.MissionState == VehicleMissionState.TO_DROPOFF && vehicle.Reason == ReasonCode.CROWD_AVOIDANCE)
                    text.AppendLine("Avoiding congestion (recorded example).");
            }
            text.AppendLine("\nSynthetic locations and accessibility. This is not a real ride request.");
            return text.ToString();
        }

        public static string StatusText(RequestStatus status)
        {
            switch (status)
            {
                case RequestStatus.CREATED: return "Request created";
                case RequestStatus.VALIDATED: return "Request validated";
                case RequestStatus.QUEUED: return "Waiting for assignment";
                case RequestStatus.ASSIGNED: return "Vehicle approaching";
                case RequestStatus.PICKUP_SERVICE: return "Boarding";
                case RequestStatus.IN_TRANSIT: return "In transit";
                case RequestStatus.DROPOFF_SERVICE: return "Alighting - not completed yet";
                case RequestStatus.COMPLETED: return "Alighting completed (final walk not verified)";
                default: return status.ToString();
            }
        }
    }
}
