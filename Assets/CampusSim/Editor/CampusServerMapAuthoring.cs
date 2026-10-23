using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using InhaExpress.Client.MapClient;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

public static class CampusServerMapAuthoring
{
    public const string ScenePath = "Assets/CampusSim/Scenes/CampusTerrain.unity";
    public const string SnapshotPath = "Assets/CampusSim/Data/server_map.json";
    private const string RootName = "Server Map | Web-aligned Nodes and Hubs";
    private const string Generated = "Assets/CampusSim/Generated/ServerMap";

    [MenuItem("Campus/Server Map/Import Web Nodes Into CampusTerrain")]
    public static void Import()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
            throw new InvalidOperationException("Import nodes in Edit Mode.");
        var map = ServerMapDocument.Parse(File.ReadAllText(SnapshotPath));
        AssetDatabase.Refresh();
        var scene = SceneManager.GetSceneByPath(ScenePath);
        if (scene.IsValid() && scene.isDirty)
            throw new InvalidOperationException("Save CampusTerrain before importing server nodes.");
        if (!scene.IsValid() || !scene.isLoaded) scene = EditorSceneManager.OpenScene(ScenePath);
        var existing = scene.GetRootGameObjects().Where(g => g.name == RootName).ToArray();
        if (existing.Length > 1 || (existing.Length == 1 && !existing[0].GetComponent<CampusServerMap>()))
            throw new InvalidOperationException("Ambiguous server map root; refusing to replace objects.");
        var root = existing.Length == 1 ? existing[0] : new GameObject(RootName);
        SceneManager.MoveGameObjectToScene(root, scene);
        var overlay = root.GetComponent<CampusServerMap>() ?? root.AddComponent<CampusServerMap>();

        if (overlay.geometryHash != map.geometry_hash || !HasExpectedMarkers(overlay, map))
        {
            // Only this importer-owned subtree is regenerated. Existing hubs/cars/roads are untouched.
            while (root.transform.childCount > 0)
                UnityEngine.Object.DestroyImmediate(root.transform.GetChild(0).gameObject);
            root.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            root.transform.localScale = Vector3.one;
            var terrain = scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<Terrain>(true))
                .FirstOrDefault();
            if (!terrain) throw new InvalidOperationException("CampusTerrain has no Terrain for visual height projection.");
            var roadColliders = scene.GetRootGameObjects()
                .Where(g => g != root).SelectMany(g => g.GetComponentsInChildren<MeshCollider>(true))
                .Where(c => IsRoad(c.transform)).ToArray();
            Physics.SyncTransforms();
            var nodes = Child(root.transform, "Road Nodes");
            var stops = Child(root.transform, "Stop Candidates (access unverified)");
            var hubs = Child(root.transform, "Home Hubs (web road anchors)");
            var markers = new List<ServerMapMarker>();
            foreach (var node in map.nodes)
                markers.Add(Create(nodes, ServerMapMarker.MarkerKind.RoadNode, node.id, node.id,
                    node.id, node.position, Array.Empty<string>(), terrain, roadColliders));
            foreach (var stop in map.stops)
                markers.Add(Create(stops, ServerMapMarker.MarkerKind.Stop, stop.id, stop.node_id,
                    stop.name, stop.position, Array.Empty<string>(), terrain, roadColliders));
            foreach (var hub in map.hubs)
                markers.Add(Create(hubs, ServerMapMarker.MarkerKind.Hub, hub.id, hub.node_id,
                    hub.name, hub.position, hub.vehicle_ids, terrain, roadColliders));
            overlay.markers = markers.ToArray();
            CreateVisuals(root.transform, markers);
        }
        overlay.authoredMap = AssetDatabase.LoadAssetAtPath<TextAsset>(SnapshotPath);
        overlay.geometryHash = map.geometry_hash;
        overlay.mapVersion = map.map_version;
        EditorUtility.SetDirty(overlay);
        EditorSceneManager.MarkSceneDirty(scene);
        if (!EditorSceneManager.SaveScene(scene)) throw new IOException("Could not save CampusTerrain.");
        AssetDatabase.SaveAssets();
        ValidateScene(overlay, map);
        Directory.CreateDirectory("artifacts");
        File.WriteAllText("artifacts/campus_server_nodes_validation.json", JsonUtility.ToJson(new Report
        {
            project_version = map.project_version, map_version = map.map_version, geometry_hash = map.geometry_hash,
            road_nodes = map.nodes.Length, directed_edges = map.edges.Length, hubs = map.hubs.Length,
            stop_candidates = map.stops.Length,
            road_mesh_projected = overlay.markers.Count(m => m.surfaceSource == "road_mesh_visual_only"),
            terrain_projected = overlay.markers.Count(m => m.surfaceSource == "terrain_visual_only"),
            outside_terrain = overlay.markers.Count(m => m.surfaceSource == "outside_terrain_flat_unverified"),
            marker_renderers = overlay.GetComponentsInChildren<MeshRenderer>(true).Length
        }, true) + "\n");
        Debug.Log($"Server map imported into existing CampusTerrain: {map.nodes.Length} nodes, {map.hubs.Length} hubs, {map.stops.Length} Stop candidates. No vehicles spawned or driven.");
    }

    private static bool HasExpectedMarkers(CampusServerMap overlay, ServerMapDocument map) =>
        overlay.markers != null && overlay.markers.Length == map.nodes.Length + map.stops.Length + map.hubs.Length &&
        overlay.markers.All(m => m != null);

    private static Transform Child(Transform parent, string name)
    {
        var child = new GameObject(name).transform;
        child.SetParent(parent, false);
        return child;
    }

    private static bool IsRoad(Transform transform)
    {
        for (var t = transform; t != null; t = t.parent)
            if (t.name == "Continuous asphalt network" || t.name.StartsWith("Road ", StringComparison.Ordinal) ||
                t.name == "Road extensions") return true;
        return false;
    }

    private static ServerMapMarker Create(Transform parent, ServerMapMarker.MarkerKind kind, string id,
        string nodeId, string name, Vector3 position, string[] vehicles, Terrain terrain, MeshCollider[] roads)
    {
        var marker = Child(parent, id + " | " + name).gameObject.AddComponent<ServerMapMarker>();
        marker.kind = kind; marker.serverId = id; marker.nodeId = nodeId; marker.displayName = name;
        marker.vehicleIds = vehicles; marker.serverPosition = position; marker.operationallyVerified = false;
        var terrainOrigin = terrain.transform.position;
        var size = terrain.terrainData.size;
        bool inside = position.x >= terrainOrigin.x && position.z >= terrainOrigin.z &&
            position.x <= terrainOrigin.x + size.x && position.z <= terrainOrigin.z + size.z;
        float height = inside ? terrain.SampleHeight(position) + terrainOrigin.y : 0;
        marker.surfaceSource = inside ? "terrain_visual_only" : "outside_terrain_flat_unverified";
        float nearest = float.PositiveInfinity;
        foreach (var road in roads)
            if (road.Raycast(new Ray(new Vector3(position.x, height + 100, position.z), Vector3.down), out var hit, 200) &&
                Mathf.Abs(hit.point.y - height) < nearest)
            {
                nearest = Mathf.Abs(hit.point.y - height);
                marker.transform.position = hit.point;
                marker.surfaceSource = "road_mesh_visual_only";
            }
        if (float.IsPositiveInfinity(nearest)) marker.transform.position = new Vector3(position.x, height, position.z);
        // Height is visualization only; the server supplies horizontal coordinates, not measured elevation.
        marker.transform.position += Vector3.up * (kind == ServerMapMarker.MarkerKind.Hub ? 1.2f :
            kind == ServerMapMarker.MarkerKind.Stop ? 0.8f : 0.3f);
        return marker;
    }

    private static void CreateVisuals(Transform root, List<ServerMapMarker> markers)
    {
        Directory.CreateDirectory(Generated);
        AssetDatabase.Refresh();
        var shader = Shader.Find("Universal Render Pipeline/Unlit");
        if (!shader) throw new InvalidOperationException("URP Unlit shader is unavailable.");
        foreach (ServerMapMarker.MarkerKind kind in Enum.GetValues(typeof(ServerMapMarker.MarkerKind)))
        {
            var materialPath = $"{Generated}/{kind}.mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(materialPath);
            if (!material) { material = new Material(shader); AssetDatabase.CreateAsset(material, materialPath); }
            material.SetColor("_BaseColor", kind == ServerMapMarker.MarkerKind.Hub ? new Color(1, .5f, .1f) :
                kind == ServerMapMarker.MarkerKind.Stop ? new Color(.25f, 1, .35f) : new Color(.1f, .65f, 1));
            material.enableInstancing = false; // One combined mesh per category; no per-node draw calls.
            var vertices = new List<Vector3>(); var triangles = new List<int>();
            foreach (var marker in markers.Where(m => m.kind == kind))
            {
                float size = kind == ServerMapMarker.MarkerKind.Hub ? 2.8f : kind == ServerMapMarker.MarkerKind.Stop ? 1.6f : .8f;
                int index = vertices.Count;
                for (int z = -1; z <= 1; z += 2) for (int y = -1; y <= 1; y += 2) for (int x = -1; x <= 1; x += 2)
                    vertices.Add(marker.transform.position + new Vector3(x, y, z) * (size / 2));
                foreach (int t in new[] { 0, 2, 1, 1, 2, 3, 4, 5, 6, 5, 7, 6, 0, 1, 4, 1, 5, 4, 2, 6, 3, 3, 6, 7, 0, 4, 2, 2, 4, 6, 1, 3, 5, 3, 7, 5 })
                    triangles.Add(index + t);
            }
            var meshPath = $"{Generated}/{kind}.asset";
            var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(meshPath);
            if (!mesh) { mesh = new Mesh { name = "Server " + kind + " markers" }; AssetDatabase.CreateAsset(mesh, meshPath); }
            mesh.Clear(); mesh.SetVertices(vertices); mesh.SetTriangles(triangles, 0); mesh.RecalculateNormals(); mesh.RecalculateBounds();
            var view = Child(root, kind + " visual markers").gameObject;
            view.AddComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = view.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = material; renderer.shadowCastingMode = ShadowCastingMode.Off; renderer.receiveShadows = false;
            EditorUtility.SetDirty(material); EditorUtility.SetDirty(mesh);
        }
    }

    public static void ValidateScene(CampusServerMap overlay, ServerMapDocument map)
    {
        if (!HasExpectedMarkers(overlay, map)) throw new InvalidOperationException("Marker count mismatch.");
        var byNode = map.nodes.ToDictionary(n => n.id, n => n.position);
        foreach (var marker in overlay.markers)
            if (!byNode.TryGetValue(marker.nodeId, out var expected) ||
                Mathf.Abs(marker.transform.position.x - expected.x) > .01f ||
                Mathf.Abs(marker.transform.position.z - expected.z) > .01f || marker.operationallyVerified)
                throw new InvalidOperationException("Marker coordinate/provenance mismatch: " + marker.serverId);
        if (overlay.GetComponentsInChildren<Collider>(true).Length != 0 ||
            overlay.GetComponentsInChildren<Rigidbody>(true).Length != 0)
            throw new InvalidOperationException("Server map markers must not alter simulation Physics.");
    }

    [Serializable] private sealed class Report
    {
        public string project_version, map_version, geometry_hash;
        public int road_nodes, directed_edges, hubs, stop_candidates, road_mesh_projected, terrain_projected,
            outside_terrain, marker_renderers;
        public bool operationally_verified = false, vehicle_actuation = false;
    }
}
