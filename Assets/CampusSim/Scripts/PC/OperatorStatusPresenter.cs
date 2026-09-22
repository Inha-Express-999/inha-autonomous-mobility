using System.Globalization;
using System.Text;
using InhaExpress.Client.Domain;
using InhaExpress.Client.Presentation;

namespace InhaExpress.Client.PC
{
    public sealed class OperatorStatusPresenter : FixtureStatusPresenter
    {
        public override ClientRole Role => ClientRole.PC_Operator;
        protected override string Format(WorldSnapshotDto snapshot) => Describe(snapshot);

        public static string Describe(WorldSnapshotDto snapshot)
        {
            var text = new StringBuilder();
            text.AppendLine($"Project {snapshot.ProjectVersion} | schema {snapshot.SchemaVersion}");
            text.AppendLine($"Map {snapshot.MapVersion}\nRun {snapshot.RunId}");
            text.AppendLine($"Tick {snapshot.SimulationTick} | seq {snapshot.Sequence} | t={snapshot.SimulationTimeS.ToString("F1", CultureInfo.InvariantCulture)} s");
            text.AppendLine($"Vehicles {snapshot.Vehicles.Count} | Requests {snapshot.Requests.Count} | Landmarks {snapshot.Landmarks.Count}");
            foreach (var vehicle in snapshot.Vehicles)
                text.AppendLine($"\n{vehicle.Id}: {vehicle.MissionState} / {vehicle.MotionState}\n" +
                    $"{vehicle.SpeedMps:F1} m/s | battery {vehicle.BatteryWh?.ToString("F0") ?? "unknown"} Wh | reason {vehicle.Reason}");
            foreach (var request in snapshot.Requests)
                text.AppendLine($"\n{request.Id}: {request.Status}\n{request.PickupLandmarkId} -> {request.DropoffLandmarkId}");
            foreach (var zone in snapshot.Zones)
                text.AppendLine($"\n{zone.Id}: {zone.Status}\nObserved {zone.ObservedDensity?.ToString("F2") ?? "unknown"} | " +
                    $"EMA {zone.EmaDensity?.ToString("F2") ?? "unknown"} | prior {zone.PriorDensity?.ToString("F2") ?? "unknown"} persons/m2");
            text.AppendLine("\nSynthetic UI recording only. No Physics, sensors, dispatch or campus coordinate alignment.");
            return text.ToString();
        }
    }
}
