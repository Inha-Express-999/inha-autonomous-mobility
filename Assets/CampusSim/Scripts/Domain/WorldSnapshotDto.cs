using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace InhaExpress.Client.Domain
{
    public sealed class WorldSnapshotDto
    {
        public const int SupportedSchemaVersion = 3;
        public int SchemaVersion { get; }
        public string ProjectVersion { get; }
        public string MapVersion { get; }
        public string RunId { get; }
        public long Sequence { get; }
        public long SimulationTick { get; }
        public double SimulationTimeS { get; }
        public ClientRole Role { get; }
        public string SubscriberId { get; }
        public ReadOnlyCollection<VehicleDto> Vehicles { get; }
        public ReadOnlyCollection<RequestDto> Requests { get; }
        public ReadOnlyCollection<LandmarkDto> Landmarks { get; }
        public ReadOnlyCollection<StopDto> Stops { get; }
        public ReadOnlyCollection<RouteDto> Routes { get; }
        public ReadOnlyCollection<ZoneDto> Zones { get; }
        public ReadOnlyCollection<VehicleControlDto> ControlCommands { get; }

        public WorldSnapshotDto(int schemaVersion, string projectVersion, string mapVersion,
            string runId, long sequence, long simulationTick, double simulationTimeS,
            ClientRole role, string subscriberId, IEnumerable<VehicleDto> vehicles,
            IEnumerable<RequestDto> requests, IEnumerable<LandmarkDto> landmarks,
            IEnumerable<StopDto> stops, IEnumerable<RouteDto> routes, IEnumerable<ZoneDto> zones,
            IEnumerable<VehicleControlDto> controlCommands = null)
        {
            if (schemaVersion != SupportedSchemaVersion) throw new ArgumentException("Unsupported schema version.");
            SchemaVersion = schemaVersion;
            ProjectVersion = DtoGuard.Text(projectVersion, nameof(projectVersion));
            var parts = projectVersion.Split('.');
            if (parts.Length != 4) throw new ArgumentException("Project version needs four numeric parts.");
            foreach (var part in parts)
            {
                if (part.Length == 0) throw new ArgumentException("Empty project version part.");
                foreach (var digit in part)
                    if (digit < '0' || digit > '9') throw new ArgumentException("Non-numeric project version.");
            }
            MapVersion = DtoGuard.Text(mapVersion, nameof(mapVersion));
            RunId = DtoGuard.Text(runId, nameof(runId));
            if (sequence < 0 || simulationTick < 0) throw new ArgumentOutOfRangeException(nameof(sequence));
            Sequence = sequence;
            SimulationTick = simulationTick;
            SimulationTimeS = DtoGuard.NonNegative(simulationTimeS, nameof(simulationTimeS));
            Role = DtoGuard.EnumValue(role, nameof(role));
            SubscriberId = DtoGuard.OptionalId(subscriberId, nameof(subscriberId));
            if (role == ClientRole.Mobile_Passenger && subscriberId == null)
                throw new ArgumentException("Mobile snapshot needs a subscriber.");
            Vehicles = DtoGuard.Copy(vehicles, nameof(vehicles));
            Requests = DtoGuard.Copy(requests, nameof(requests));
            Landmarks = DtoGuard.Copy(landmarks, nameof(landmarks));
            Stops = DtoGuard.Copy(stops, nameof(stops));
            Routes = DtoGuard.Copy(routes, nameof(routes));
            Zones = DtoGuard.Copy(zones, nameof(zones));
            ControlCommands = DtoGuard.Copy(controlCommands ?? Array.Empty<VehicleControlDto>(), nameof(controlCommands));
            if (role != ClientRole.PC_Operator && ControlCommands.Count > 0)
                throw new ArgumentException("Only PC operator snapshots may contain actuator commands.");
            var controlled = new HashSet<string>(StringComparer.Ordinal);
            foreach (var command in ControlCommands)
            {
                if (command.MapVersion != mapVersion || !controlled.Add(command.VehicleId))
                    throw new ArgumentException("Control map mismatch or duplicate vehicle command.");
                bool found = false;
                foreach (var vehicle in Vehicles) if (vehicle.Id == command.VehicleId) found = true;
                if (!found) throw new ArgumentException("Command references an absent vehicle.");
            }
        }
    }
}
