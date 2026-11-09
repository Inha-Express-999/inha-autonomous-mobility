using System;
using System.IO;
using System.Linq;
using InhaExpress.Client.MapClient;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

public static class CampusStopSignAuthoring
{
    private const string AssetsFolder = "Assets/CampusSim/Generated/StopShelters";

    [MenuItem("Campus/Server Map/Build Stop Shelters")]
    public static void Build()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Use Edit Mode.");
        var scene = SceneManager.GetSceneByPath(CampusServerMapAuthoring.ScenePath);
        if (!scene.IsValid() || !scene.isLoaded) scene = EditorSceneManager.OpenScene(CampusServerMapAuthoring.ScenePath);
        var map = scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<CampusServerMap>(true)).Single();
        var document = ServerMapDocument.Parse(File.ReadAllText(CampusServerMapAuthoring.SnapshotPath));
        var nodes = document.nodes.ToDictionary(n => n.id, n => n.position);
        CampusServicePointVisuals.Migrate(map);
        Directory.CreateDirectory(AssetsFolder);
        if(!AssetDatabase.IsValidFolder(AssetsFolder))AssetDatabase.Refresh();
        var blue = Material("Mobil blue", new Color(.086f,.467f,1));
        var dark = Material("Graphite", new Color(.09f,.14f,.23f));
        var white = Material("Warm white", new Color(.91f,.94f,.98f));
        var terrains = scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<Terrain>(true)).ToArray();
        var roads = scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<MeshCollider>(true))
            .Where(c => IsRoad(c.transform)).ToArray();
        int count = 0;
        foreach (var marker in map.markers.Where(m => m.kind == ServerMapMarker.MarkerKind.Stop))
        {
            var attached=marker.GetComponentsInChildren<ServicePointVisual>(true)
                .Where(v=>v.pointKind==marker.kind && v.pointId==marker.serverId).ToArray();
            if(attached.Length>1)throw new InvalidOperationException("Duplicate Stop model: "+marker.serverId);
            // Presentation offsets only: never move or change the authoritative Stop marker.
            var neighbour = document.edges.Where(e => e.u == marker.nodeId || e.v == marker.nodeId)
                .Select(e => nodes[e.u == marker.nodeId ? e.v : e.u] - nodes[marker.nodeId])
                .Where(v => v.sqrMagnitude > .01f).OrderByDescending(v => v.sqrMagnitude).FirstOrDefault();
            neighbour.y = 0;
            var forward = neighbour.sqrMagnitude > .01f ? neighbour.normalized : Vector3.forward;
            var outward = Vector3.Cross(Vector3.up, forward);
            var position = marker.transform.position + outward * 4.5f;
            position.y = marker.transform.position.y - .8f;
            var stop = attached.Length == 1 ? attached[0].transform : new GameObject(marker.serverId + " | " + marker.displayName).transform;
            if (attached.Length == 0)
            {
                Undo.RegisterCreatedObjectUndo(stop.gameObject,"Create Stop display model");
                stop.SetPositionAndRotation(position, Quaternion.LookRotation(-outward, Vector3.up));
                CampusServicePointVisuals.Bind(stop,marker);
            }
            // Preserve hand-authored X/Z and heading. Reproject the whole platform, not just its centre.
            Undo.RecordObject(stop, "Ground Stop shelter");
            position = stop.position;
            position.y = GroundHeight(stop, terrains, roads, marker.transform.position.y - .8f) + .025f;
            stop.position = position;
            Box(stop, "Platform", new Vector3(0,.06f,0), new Vector3(2.7f,.12f,1.3f), white);
            Box(stop, "Blue edge", new Vector3(0,.13f,.58f), new Vector3(2.7f,.025f,.1f), blue);
            for (int side = -1; side <= 1; side += 2)
                Box(stop, "Canopy post", new Vector3(side*1.15f,1.3f,-.4f), new Vector3(.08f,2.5f,.08f), dark);
            Box(stop, "Canopy", new Vector3(0,2.6f,0), new Vector3(2.9f,.12f,1.5f), blue);
            Box(stop, "Bench", new Vector3(0,.52f,-.25f), new Vector3(1.8f,.1f,.42f), dark);
            Box(stop, "Bench back", new Vector3(0,.82f,-.45f), new Vector3(1.8f,.38f,.06f), dark);
            Box(stop, "Bench support left", new Vector3(-.65f,.29f,-.25f), new Vector3(.08f,.46f,.32f), dark);
            Box(stop, "Bench support right", new Vector3(.65f,.29f,-.25f), new Vector3(.08f,.46f,.32f), dark);
            Box(stop, "Stop sign", new Vector3(0,2.23f,.65f), new Vector3(2.4f,.5f,.08f), white);
            if (!stop.Find("Stop label")) Label(stop, marker.displayName, new Vector3(0,2.24f,.695f), 180, .115f, dark.color);
            count++;
        }
        if (map.GetComponentsInChildren<ServicePointVisual>(true).Any(v=>v.GetComponentsInChildren<Collider>(true).Length!=0))
            throw new InvalidOperationException("Visual shelters must not affect Physics.");
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        AssetDatabase.SaveAssets();
        Debug.Log("CAMPUS_STOP_SHELTERS count="+count+" colliders=0 markers_unchanged=true existing_horizontal_placement_preserved=true");
    }

    private static bool IsRoad(Transform transform)
    {
        for (var t = transform; t; t = t.parent)
            if (t.name == "Continuous asphalt network" || t.name == "Continuous pedestrian network" ||
                t.name == "Junction aware kerbs" || t.name.StartsWith("Road ", StringComparison.Ordinal) || t.name == "Road extensions") return true;
        return false;
    }

    public static float GroundHeight(Transform model, Terrain[] terrains, MeshCollider[] roads, float fallback)
    {
        float highest = float.NegativeInfinity;
        for (int x = -1; x <= 1; x++) for (int z = -1; z <= 1; z++)
        {
            var point = model.TransformPoint(new Vector3(x * 1.35f, 0, z * .65f));
            foreach (var terrain in terrains)
            {
                var local = point - terrain.transform.position;
                var size = terrain.terrainData.size;
                if (local.x >= 0 && local.z >= 0 && local.x <= size.x && local.z <= size.z)
                    highest = Mathf.Max(highest, terrain.SampleHeight(point) + terrain.transform.position.y);
            }
            foreach (var road in roads)
            {
                var origin = new Vector3(point.x, road.bounds.max.y + 1, point.z);
                if (road.Raycast(new Ray(origin, Vector3.down), out var hit, road.bounds.size.y + 2))
                    highest = Mathf.Max(highest, hit.point.y);
            }
        }
        return float.IsNegativeInfinity(highest) ? fallback : highest;
    }

    private static Material Material(string name, Color color)
    {
        string path = AssetsFolder + "/" + name + ".mat";
        var material = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (!material) { material = new Material(Shader.Find("Universal Render Pipeline/Unlit")); AssetDatabase.CreateAsset(material, path); }
        material.SetColor("_BaseColor", color);
        material.enableInstancing = false; // Shared meshes/materials and static batching for these fixed props.
        EditorUtility.SetDirty(material);
        return material;
    }

    private static void Box(Transform parent, string name, Vector3 position, Vector3 size, Material material)
    {
        // Repair known generated parts; posts intentionally share a name, one on each side.
        var matches = parent.Cast<Transform>().Where(t => t.name == name &&
            (name != "Canopy post" || Mathf.Sign(t.localPosition.x) == Mathf.Sign(position.x))).ToArray();
        if (matches.Length > 0)
        {
            Undo.RecordObject(matches[0], "Repair shelter part");
            matches[0].localPosition = position; matches[0].localScale = size;
            foreach (var duplicate in matches.Skip(1)) Undo.DestroyObjectImmediate(duplicate.gameObject);
            return;
        }
        var obj = GameObject.CreatePrimitive(PrimitiveType.Cube);
        Undo.RegisterCreatedObjectUndo(obj, "Restore shelter part");
        obj.name = name; obj.transform.SetParent(parent, false);
        obj.transform.localPosition = position; obj.transform.localScale = size;
        UnityEngine.Object.DestroyImmediate(obj.GetComponent<Collider>());
        var renderer = obj.GetComponent<MeshRenderer>();
        renderer.sharedMaterial = material; renderer.shadowCastingMode = ShadowCastingMode.Off;
        renderer.receiveShadows = false;
        GameObjectUtility.SetStaticEditorFlags(obj, StaticEditorFlags.BatchingStatic);
    }

    private static void Label(Transform parent, string value, Vector3 position, float yaw, float scale, Color color)
    {
        var obj = new GameObject("Stop label"); obj.transform.SetParent(parent, false);
        obj.transform.localPosition = position; obj.transform.localRotation = Quaternion.Euler(0,yaw,0);
        var text = obj.AddComponent<TextMesh>(); text.text = value;
        text.font = Resources.Load<Font>("Freesentation-Bold"); text.fontSize = 64;
        text.characterSize = scale; text.anchor = TextAnchor.MiddleCenter; text.alignment = TextAlignment.Center; text.color = color;
        if (text.font)
        {
            text.font.RequestCharactersInTexture(value, text.fontSize);
            float advance = 0;
            foreach (char character in value)
                if (text.font.GetCharacterInfo(character, out var glyph, text.fontSize)) advance += glyph.advance;
            // TextMesh uses ten font pixels per characterSize world unit.
            text.characterSize = Mathf.Min(scale, 2.1f * 10 / Mathf.Max(1, advance), .3f * 10 / text.fontSize);
            obj.GetComponent<MeshRenderer>().sharedMaterial = text.font.material;
        }
    }
}
