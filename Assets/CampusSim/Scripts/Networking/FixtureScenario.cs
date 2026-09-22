using System;
using System.Collections.Generic;
using InhaExpress.Client.Domain;

namespace InhaExpress.Client.Networking
{
    public static class FixtureScenario
    {
        public const string MapVersion = "synthetic-ui-v1";
        public const string PassengerId = "fixture-passenger";
        public const double DurationS = 45;

        public static WorldSnapshotDto Create(ClientRole role, string subscriberId, string projectVersion,
            string runId, long sequence, long tick)
        {
            if (tick < 0 || tick > 900) throw new ArgumentOutOfRangeException(nameof(tick));
            double time = tick * FixtureClientDataSource.TickIntervalS;
            bool assigned = time >= 3;
            bool validated = time >= 1;
            bool detour = time >= 20;
            bool yielding = time >= 24 && time < 27;
            var status = time < 1 ? RequestStatus.CREATED : time < 2 ? RequestStatus.VALIDATED :
                time < 3 ? RequestStatus.QUEUED : time < 10 ? RequestStatus.ASSIGNED :
                time < 13 ? RequestStatus.PICKUP_SERVICE : time < 40 ? RequestStatus.IN_TRANSIT :
                time < 44 ? RequestStatus.DROPOFF_SERVICE : RequestStatus.COMPLETED;
            var mission = !assigned || time >= 44 ? VehicleMissionState.IDLE : time < 10 ?
                VehicleMissionState.TO_PICKUP : time < 13 ? VehicleMissionState.PICKUP_SERVICE :
                time < 40 ? VehicleMissionState.TO_DROPOFF : VehicleMissionState.DROPOFF_SERVICE;
            var reason = yielding ? ReasonCode.PEDESTRIAN : detour && time < 40 ?
                ReasonCode.CROWD_AVOIDANCE : ReasonCode.UNKNOWN;
            var request = new RequestDto("request-demo", PassengerId, ServiceType.PASSENGER, status, 0,
                "landmark-0", "landmark-1", validated ? "stop-0" : null, validated ? "stop-1" : null,
                new ServiceNeedsDto(true, 1, true), 1, 0, assigned ? "vehicle-1" : null,
                etaS: assigned && time < 40 ? 40 - time : (double?)null);

            var requests = new List<RequestDto>();
            var vehicles = new List<VehicleDto>();
            var routes = new List<RouteDto>();
            var stops = new List<StopDto>();
            var landmarks = new List<LandmarkDto>();
            var zones = new List<ZoneDto>();
            bool pc = role == ClientRole.PC_Operator;
            bool ownsDemo = pc || subscriberId == PassengerId;
            if (ownsDemo) requests.Add(request);
            if (pc || (ownsDemo && assigned))
            {
                bool driving = (time >= 3 && time < 10) || (time >= 13 && time < 40 && !yielding);
                // Synthetic poses intentionally have no correspondence to CampusTerrain.
                double north = time < 3 ? -7 : time < 10 ? time - 10 : time < 13 ? 0 :
                    time < 24 ? time - 13 : time < 27 ? 11 : Math.Min(24, time - 16);
                vehicles.Add(new VehicleDto("vehicle-1", new MapPositionDto(0, north, 0), 0,
                    driving ? 1 : 0, 800, mission, yielding ? VehicleMotionState.YIELDING :
                    driving ? VehicleMotionState.DRIVING : VehicleMotionState.WAITING_RESOURCE,
                    assigned && time < 44 ? request.Id : null, assigned && time < 44 ? "route-demo" : null, reason));
                if (assigned && time < 44)
                    routes.Add(new RouteDto("route-demo", MapVersion,
                        new[] { new MapPositionDto(0, -7, 0), new MapPositionDto(0, 0, 0),
                            new MapPositionDto(0, 24, 0) }, detour ? ReasonCode.CROWD_AVOIDANCE : ReasonCode.UNKNOWN));
            }
            if (pc)
            {
                vehicles.Add(new VehicleDto("vehicle-2", new MapPositionDto(20, 0, 0), 0, 0, null,
                    VehicleMissionState.IDLE, VehicleMotionState.WAITING_RESOURCE));
                vehicles.Add(new VehicleDto("vehicle-3", new MapPositionDto(40, 0, 0), 0, 0, 500,
                    VehicleMissionState.IDLE, VehicleMotionState.WAITING_RESOURCE));
                requests.Add(new RequestDto("request-other", "fixture-other", ServiceType.PASSENGER,
                    RequestStatus.QUEUED, 0, "landmark-2", "landmark-3", "stop-2", "stop-3",
                    new ServiceNeedsDto(false, 0, false), 1, 0));
                zones.Add(new ZoneDto("synthetic-zone", detour ? ZoneStatus.AVOID : ZoneStatus.NORMAL,
                    null, null, detour ? 0.6 : 0.1, detour ? ReasonCode.CROWD_AVOIDANCE : ReasonCode.UNKNOWN));
            }
            for (int i = 0; i < 6; i++)
            {
                bool includeStop = pc || (ownsDemo && validated && i < 2);
                landmarks.Add(new LandmarkDto("landmark-" + i, "Fixture " + (char)('A' + i), "synthetic",
                    VerificationStatus.SYNTHETIC, includeStop ? new[] { "stop-" + i } : Array.Empty<string>()));
                if (includeStop)
                    stops.Add(new StopDto("stop-" + i, "landmark-" + i, "synthetic-entrance-" + i,
                        new MapPositionDto(i < 2 ? 0 : i * 20, i == 1 ? 24 : 0, 0), true, VerificationStatus.SYNTHETIC));
            }
            return new WorldSnapshotDto(WorldSnapshotDto.SupportedSchemaVersion, projectVersion, MapVersion,
                runId, sequence, tick, time, role, subscriberId, vehicles, requests, landmarks, stops, routes, zones);
        }
    }
}
