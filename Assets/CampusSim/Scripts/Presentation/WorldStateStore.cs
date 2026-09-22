using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using InhaExpress.Client.Domain;

namespace InhaExpress.Client.Presentation
{
    public enum SnapshotApplyResult { Applied, IgnoredOldSequence, IgnoredRetiredRun }

    // Call on Unity's main thread after transport authentication/decoding.
    // Does not own Physics, dispatch, request transitions, or server authorization.
    public sealed class WorldStateStore
    {
        private readonly ClientRole role;
        private readonly string subscriberId;
        private readonly HashSet<string> retiredRuns = new HashSet<string>(StringComparer.Ordinal);
        private double? receivedAt;
        public WorldSnapshotDto Current { get; private set; }
        public IReadOnlyDictionary<string, VehicleDto> Vehicles { get; private set; } =
            new ReadOnlyDictionary<string, VehicleDto>(new Dictionary<string, VehicleDto>());
        public IReadOnlyDictionary<string, RequestDto> Requests { get; private set; } =
            new ReadOnlyDictionary<string, RequestDto>(new Dictionary<string, RequestDto>());
        public event Action<WorldSnapshotDto> SnapshotChanged;

        public WorldStateStore(ClientRole role, string subscriberId = null)
        {
            if (!Enum.IsDefined(typeof(ClientRole), role)) throw new ArgumentOutOfRangeException(nameof(role));
            if (role == ClientRole.Mobile_Passenger && string.IsNullOrWhiteSpace(subscriberId))
                throw new ArgumentException("Mobile store requires an authenticated subscriber ID.");
            this.role = role;
            this.subscriberId = subscriberId;
        }

        public SnapshotApplyResult ApplySnapshot(WorldSnapshotDto snapshot, double monotonicNowS)
        {
            if (snapshot == null) throw new ArgumentNullException(nameof(snapshot));
            ValidateClock(monotonicNowS);
            if (snapshot.Role != role || (role == ClientRole.Mobile_Passenger && snapshot.SubscriberId != subscriberId))
                throw new ArgumentException("Snapshot subscription does not match this store.");
            if (retiredRuns.Contains(snapshot.RunId)) return SnapshotApplyResult.IgnoredRetiredRun;
            if (Current != null && Current.RunId == snapshot.RunId)
            {
                if (snapshot.Sequence <= Current.Sequence) return SnapshotApplyResult.IgnoredOldSequence;
                if (snapshot.SimulationTick < Current.SimulationTick || snapshot.SimulationTimeS < Current.SimulationTimeS)
                    throw new ArgumentException("Simulation time regressed within a run.");
                if (snapshot.MapVersion != Current.MapVersion)
                    throw new ArgumentException("Map changes require a new run.");
            }

            // Prepare and validate everything before publishing; invalid data leaves state untouched.
            var vehicles = Index(snapshot.Vehicles, x => x.Id);
            var requests = Index(snapshot.Requests, x => x.Id);
            var landmarks = Index(snapshot.Landmarks, x => x.Id);
            var stops = Index(snapshot.Stops, x => x.Id);
            var routes = Index(snapshot.Routes, x => x.Id);
            Index(snapshot.Zones, x => x.Id);
            foreach (var route in routes.Values)
                if (route.MapVersion != snapshot.MapVersion) throw new ArgumentException("Route map mismatch.");
            foreach (var stop in stops.Values) Require(landmarks, stop.LandmarkId);
            foreach (var landmark in landmarks.Values)
                foreach (var stopId in landmark.StopIds)
                {
                    Require(stops, stopId);
                    if (stops[stopId].LandmarkId != landmark.Id) throw new ArgumentException("Stop landmark mismatch.");
                }
            foreach (var request in requests.Values)
            {
                // A rejected input may refer to a landmark that does not exist.
                if (request.Status != RequestStatus.REJECTED)
                {
                    Require(landmarks, request.PickupLandmarkId);
                    Require(landmarks, request.DropoffLandmarkId);
                }
                Require(stops, request.PickupStopId);
                Require(stops, request.DropoffStopId);
                Require(vehicles, request.VehicleId);
                if (request.PickupStopId != null && request.PickupLandmarkId != null &&
                    stops[request.PickupStopId].LandmarkId != request.PickupLandmarkId)
                    throw new ArgumentException("Pickup stop belongs to a different landmark.");
                if (request.DropoffStopId != null && request.DropoffLandmarkId != null &&
                    stops[request.DropoffStopId].LandmarkId != request.DropoffLandmarkId)
                    throw new ArgumentException("Dropoff stop belongs to a different landmark.");
            }
            foreach (var vehicle in vehicles.Values)
            {
                Require(requests, vehicle.RequestId);
                Require(routes, vehicle.RouteId);
            }
            if (role == ClientRole.Mobile_Passenger)
            {
                if (snapshot.Zones.Count != 0) throw new ArgumentException("Mobile may not receive zone telemetry.");
                foreach (var request in requests.Values)
                    if (request.OwnerId != subscriberId || request.ServiceType != ServiceType.PASSENGER)
                        throw new ArgumentException("Mobile received another subscriber's request or cargo.");
                var allowedVehicles = new HashSet<string>();
                var allowedRoutes = new HashSet<string>();
                var allowedStops = new HashSet<string>();
                foreach (var request in requests.Values)
                {
                    if (request.VehicleId != null) allowedVehicles.Add(request.VehicleId);
                    if (request.PickupStopId != null) allowedStops.Add(request.PickupStopId);
                    if (request.DropoffStopId != null) allowedStops.Add(request.DropoffStopId);
                }
                foreach (var vehicle in vehicles.Values)
                {
                    if (!allowedVehicles.Contains(vehicle.Id)) throw new ArgumentException("Unassigned mobile vehicle.");
                    if (vehicle.RouteId != null) allowedRoutes.Add(vehicle.RouteId);
                }
                foreach (var route in routes.Values)
                    if (!allowedRoutes.Contains(route.Id)) throw new ArgumentException("Unrelated mobile route.");
                foreach (var stop in stops.Values)
                    if (!allowedStops.Contains(stop.Id)) throw new ArgumentException("Unrelated mobile stop.");
            }
            if (Current != null && Current.RunId != snapshot.RunId) retiredRuns.Add(Current.RunId);
            Current = snapshot;
            Vehicles = new ReadOnlyDictionary<string, VehicleDto>(vehicles);
            Requests = new ReadOnlyDictionary<string, RequestDto>(requests);
            receivedAt = monotonicNowS;
            SnapshotChanged?.Invoke(snapshot);
            return SnapshotApplyResult.Applied;
        }

        public bool IsStale(double monotonicNowS)
        {
            ValidateClock(monotonicNowS);
            return !receivedAt.HasValue || monotonicNowS - receivedAt.Value >= 1.0;
        }

        private void ValidateClock(double now)
        {
            if (double.IsNaN(now) || double.IsInfinity(now) || now < 0 || (receivedAt.HasValue && now < receivedAt.Value))
                throw new ArgumentOutOfRangeException(nameof(now), "Use a monotonic real-time clock.");
        }

        private static Dictionary<string, T> Index<T>(IEnumerable<T> values, Func<T, string> id)
        {
            var result = new Dictionary<string, T>(StringComparer.Ordinal);
            foreach (var value in values)
            {
                var key = id(value);
                if (result.ContainsKey(key)) throw new ArgumentException("Duplicate ID: " + key);
                result.Add(key, value);
            }
            return result;
        }

        private static void Require<T>(Dictionary<string, T> values, string id)
        {
            if (id != null && !values.ContainsKey(id)) throw new ArgumentException("Missing referenced ID: " + id);
        }
    }
}
