using System;
using System.Collections.Generic;
using InhaExpress.Client.Domain;
using UnityEngine;
using UnityEngine.Rendering;

namespace InhaExpress.Client.Presentation
{
    /// <summary>
    /// Creates visualization/physics-shell actors from the authoritative server snapshot.
    /// Actors report their own Rigidbody pose; this component never applies server poses after spawn.
    /// </summary>
    public sealed class VehicleActorSpawner : MonoBehaviour, IClientView
    {
        private const string SyntheticMapPrefix = "synthetic-";

        [Serializable]
        private sealed class VehiclePrefabBinding
        {
            public string vehicleId;
            public GameObject prefab;
        }

        [SerializeField] private string requiredMapVersion;
        [SerializeField, Min(0f)] private float spawnHeightOffsetM;
        [SerializeField] private GameObject routeOverlayPrefab;
        [SerializeField] private Material routeOverlayMaterial;
        [SerializeField] private VehiclePrefabBinding[] vehiclePrefabs = Array.Empty<VehiclePrefabBinding>();

        private readonly Dictionary<string, GameObject> actors = new Dictionary<string, GameObject>(StringComparer.Ordinal);
        private readonly Dictionary<string, LineRenderer> routeLines = new Dictionary<string, LineRenderer>(StringComparer.Ordinal);
        private ClientRuntimeHost host;
        private string currentRunId;
        private Material runtimeRouteOverlayMaterial;

        public void Bind(ClientRuntimeHost runtime)
        {
            if (runtime == null || runtime.Role != ClientRole.PC_Operator)
                throw new ArgumentException("Vehicle actors can only be bound to the PC operator runtime.", nameof(runtime));

            if (host != null) host.Store.SnapshotChanged -= OnSnapshot;
            host = runtime;
            host.Store.SnapshotChanged += OnSnapshot;
            if (host.Store.Current != null) OnSnapshot(host.Store.Current);
        }

        private void OnDestroy()
        {
            if (host != null) host.Store.SnapshotChanged -= OnSnapshot;
            ClearActors();
            if (runtimeRouteOverlayMaterial != null)
            {
                if (Application.isPlaying) Destroy(runtimeRouteOverlayMaterial);
                else DestroyImmediate(runtimeRouteOverlayMaterial);
                runtimeRouteOverlayMaterial = null;
            }
        }

        private void OnSnapshot(WorldSnapshotDto snapshot)
        {
            if (host == null || host.Fixture != null) return;
            if (string.IsNullOrWhiteSpace(requiredMapVersion) ||
                !requiredMapVersion.StartsWith(SyntheticMapPrefix, StringComparison.Ordinal) ||
                !string.Equals(snapshot.MapVersion, requiredMapVersion, StringComparison.Ordinal))
            {
                ClearActors();
                currentRunId = null;
                return;
            }

            if (!string.Equals(currentRunId, snapshot.RunId, StringComparison.Ordinal))
            {
                ClearActors();
                currentRunId = snapshot.RunId;
            }

            foreach (var vehicle in snapshot.Vehicles)
            {
                if (!actors.TryGetValue(vehicle.Id, out var actor))
                {
                    if (!TryFindPrefab(vehicle.Id, out var prefab)) continue;
                    actor = Spawn(vehicle, prefab);
                    if (actor == null) continue;
                }

                var follower = actor.GetComponent<VehicleRouteFollower>();
                if (follower == null) follower = actor.AddComponent<VehicleRouteFollower>();
                RouteDto matchingRoute = null;
                if (vehicle.RouteId != null)
                    foreach (var route in snapshot.Routes)
                        if (string.Equals(route.Id, vehicle.RouteId, StringComparison.Ordinal))
                        {
                            matchingRoute = route;
                            break;
                        }
                if (matchingRoute == null || !follower.ApplyRoute(matchingRoute, snapshot.MapVersion,
                        Time.realtimeSinceStartupAsDouble))
                {
                    follower.ClearRoute();
                    ClearRouteLine(vehicle.Id);
                    continue;
                }

                // The alpha follower has no local obstacle/safety controller. It moves only
                // while the authoritative snapshot explicitly grants the DRIVING state.
                bool serverAllowsMotion = vehicle.MotionState == VehicleMotionState.DRIVING &&
                    vehicle.Reason != ReasonCode.STALE_LOCALIZATION;
                follower.SetMotionAuthorized(serverAllowsMotion);
                UpdateRouteLine(vehicle.Id, actor, follower, serverAllowsMotion);
            }
        }

        private GameObject Spawn(VehicleDto vehicle, GameObject prefab)
        {
            var position = MapCoordinateConverter.ToUnity(vehicle.Position) + Vector3.up * spawnHeightOffsetM;
            var rotation = Quaternion.Euler(0f, MapCoordinateConverter.ToUnityYaw(vehicle.HeadingRad), 0f);
            var actor = Instantiate(prefab, position, rotation);
            actor.name = "Vehicle " + vehicle.Id;
            int vehicleLayer = LayerMask.NameToLayer("Vehicle");
            if (vehicleLayer >= 0) SetLayerRecursively(actor.transform, vehicleLayer);

            var reporter = actor.GetComponent<VehicleEgoLocalizationReporter>();
            if (reporter == null)
            {
                DestroyActor(actor);
                Debug.LogError("Vehicle actor prefab has no ego localization reporter: " + prefab.name, this);
                return null;
            }

            reporter.Configure(vehicle.Id);
            if (actor.GetComponent<VehicleRaycastSensorRig>() == null)
                actor.AddComponent<VehicleRaycastSensorRig>();
            CreateRouteLine(vehicle.Id, actor);
            actors.Add(vehicle.Id, actor);
            Debug.Log("Spawned Unity physics actor for server vehicle " + vehicle.Id, actor);
            return actor;
        }

        private static void SetLayerRecursively(Transform root, int layer)
        {
            root.gameObject.layer = layer;
            for (int index = 0; index < root.childCount; index++)
                SetLayerRecursively(root.GetChild(index), layer);
        }

        private bool TryFindPrefab(string vehicleId, out GameObject prefab)
        {
            if (vehiclePrefabs != null)
                foreach (var binding in vehiclePrefabs)
                    if (binding != null && string.Equals(binding.vehicleId, vehicleId, StringComparison.Ordinal) && binding.prefab != null)
                    {
                        prefab = binding.prefab;
                        return true;
                    }
            prefab = null;
            return false;
        }

        private void CreateRouteLine(string vehicleId, GameObject actor)
        {
            var routeObject = routeOverlayPrefab != null
                ? Instantiate(routeOverlayPrefab, actor.transform, false)
                : new GameObject("Assigned Route");
            routeObject.name = "Assigned Route";
            routeObject.layer = actor.layer;
            if (routeObject.transform.parent != actor.transform)
                routeObject.transform.SetParent(actor.transform, false);
            var line = routeObject.GetComponent<LineRenderer>();
            if (line == null) line = routeObject.AddComponent<LineRenderer>();
            line.useWorldSpace = true;
            line.loop = false;
            line.widthMultiplier = 0.10f;
            line.numCornerVertices = 2;
            line.numCapVertices = 2;
            line.shadowCastingMode = ShadowCastingMode.Off;
            line.receiveShadows = false;
            if (line.sharedMaterial == null) line.sharedMaterial = GetRouteLineMaterial();
            line.positionCount = 0;
            routeLines[vehicleId] = line;
        }

        private Material GetRouteLineMaterial()
        {
            if (routeOverlayMaterial != null) return routeOverlayMaterial;
            if (runtimeRouteOverlayMaterial != null) return runtimeRouteOverlayMaterial;
            var shader = Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Sprites/Default");
            if (shader == null)
            {
                Debug.LogWarning("Route overlay is hidden because no supported unlit shader was found.", this);
                return null;
            }
            runtimeRouteOverlayMaterial = new Material(shader) { name = "Runtime Vehicle Route Overlay" };
            return runtimeRouteOverlayMaterial;
        }

        private void UpdateRouteLine(string vehicleId, GameObject actor, VehicleRouteFollower follower,
            bool serverAllowsMotion)
        {
            if (!routeLines.TryGetValue(vehicleId, out var line) || line == null) return;
            int pointCount = follower.RoutePointCount;
            if (line.sharedMaterial == null || pointCount < 2)
            {
                line.positionCount = 0;
                return;
            }

            const float lineLiftM = 0.12f;
            line.positionCount = pointCount;
            // The route line represents immutable server geometry. Actor motion and
            // resume progress must not replace its first point or truncate its prefix.
            for (int index = 0; index < pointCount; index++)
                line.SetPosition(index, follower.GetRoutePoint(index) + Vector3.up * lineLiftM);

            Color color = serverAllowsMotion
                ? new Color(0.10f, 0.72f, 0.91f, 0.95f)
                : new Color(1.00f, 0.60f, 0.10f, 0.95f);
            line.startColor = color;
            line.endColor = color;
        }

        private void ClearRouteLine(string vehicleId)
        {
            if (routeLines.TryGetValue(vehicleId, out var line) && line != null)
                line.positionCount = 0;
        }

        private void ClearActors()
        {
            foreach (var actor in actors.Values)
                if (actor != null)
                {
                    var follower = actor.GetComponent<VehicleRouteFollower>();
                    if (follower != null) follower.ClearRoute();
                    DestroyActor(actor);
                }
            actors.Clear();
            routeLines.Clear();
        }

        private static void DestroyActor(GameObject actor)
        {
            if (actor == null) return;
            if (Application.isPlaying) Destroy(actor);
            else DestroyImmediate(actor);
        }
    }
}
