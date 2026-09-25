using System;
using System.Collections.Generic;
using System.IO;
using InhaExpress.Client.Domain;
using InhaExpress.Client.Presentation;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UObject = UnityEngine.Object;

public static class RoadGraphPrefabAuthoring
{
    private const string Root = "Assets/CampusSim/Prefabs/Navigation";
    private const string FixturePath = "maps/fixtures/campus-synthetic-6.json";
    private const string PreviewScenePath = "Assets/CampusSim/Scenes/RoadGraphSyntheticPreview.unity";

    [MenuItem("InhaExpress/Map/Create RoadGraph Visualization Prefabs")]
    public static void CreatePrefabs()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
            throw new System.InvalidOperationException("Create navigation prefabs in Edit Mode.");

        EnsureFolder(Root);
        Material markerMaterial = CreateMaterial("GraphMarker.mat", new Color(1f, 0.68f, 0.16f));
        Material edgeMaterial = CreateMaterial("GraphEdge.mat", new Color(1f, 0.68f, 0.16f));
        SaveMarker("RoadGraphNode.prefab", "RoadGraphNode", new Vector3(0.55f, 0.55f, 0.55f), PrimitiveType.Sphere, markerMaterial, false);
        SaveMarker("RoadGraphStop.prefab", "RoadGraphStop", new Vector3(0.8f, 0.16f, 0.8f), PrimitiveType.Cylinder, markerMaterial, true);
        SaveEdge(edgeMaterial);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log("Created reusable RoadGraph node, Stop, and edge prefabs under " + Root);
    }

    [MenuItem("InhaExpress/Map/Create Synthetic RoadGraph Preview Scene")]
    public static void CreateSyntheticPreview()
    {
        string fixturePath = Path.GetFullPath(FixturePath);
        if (!File.Exists(fixturePath)) throw new FileNotFoundException("Synthetic graph fixture was not found.", fixturePath);
        if (AssetDatabase.LoadAssetAtPath<SceneAsset>(PreviewScenePath))
            throw new System.InvalidOperationException("Preview scene already exists; preserve it and choose a new path before regenerating.");

        var graph = JsonUtility.FromJson<FixtureGraph>(File.ReadAllText(fixturePath));
        if (graph == null || graph.schema_version != 1 || graph.data_status != "SYNTHETIC_FIXTURE" ||
            graph.nodes == null || graph.edges == null)
            throw new System.InvalidOperationException("Only the supported schema-v1 synthetic fixture can be visualized by this preview command.");

        var nodePrefab = AssetDatabase.LoadAssetAtPath<GameObject>(Root + "/RoadGraphNode.prefab");
        var stopPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(Root + "/RoadGraphStop.prefab");
        var edgePrefab = AssetDatabase.LoadAssetAtPath<GameObject>(Root + "/RoadGraphEdge.prefab");
        if (!nodePrefab || !stopPrefab || !edgePrefab)
            throw new System.InvalidOperationException("Create RoadGraph visualization prefabs first.");

        var nodesById = new Dictionary<string, FixtureNode>();
        foreach (FixtureNode node in graph.nodes)
        {
            if (node == null || string.IsNullOrWhiteSpace(node.id) || node.position_m == null ||
                !nodesById.TryAdd(node.id, node))
                throw new System.InvalidOperationException("Fixture has a null, malformed, or duplicate node.");
        }

        Scene previousScene = SceneManager.GetActiveScene();
        Scene preview = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
        try
        {
            SceneManager.SetActiveScene(preview);
            var graphRoot = new GameObject("SYNTHETIC ONLY - " + graph.map_id);
            SceneManager.MoveGameObjectToScene(graphRoot, preview);
            var nodesRoot = new GameObject("Nodes and Stops");
            nodesRoot.transform.SetParent(graphRoot.transform, false);
            var edgesRoot = new GameObject("Directed Edges");
            edgesRoot.transform.SetParent(graphRoot.transform, false);

            foreach (FixtureNode node in graph.nodes)
            {
                MapPositionDto position = ToMapPosition(node.position_m);
                var nodeObject = (GameObject)PrefabUtility.InstantiatePrefab(nodePrefab, preview);
                nodeObject.transform.SetParent(nodesRoot.transform, true);
                nodeObject.GetComponent<RoadGraphNodeMarkerView>().Bind(node.id, node.landmark_id,
                    position, RoadGraphVerificationStatus.Synthetic);

                if (!string.IsNullOrWhiteSpace(node.stop_id))
                {
                    var stopObject = (GameObject)PrefabUtility.InstantiatePrefab(stopPrefab, preview);
                    stopObject.transform.SetParent(nodesRoot.transform, true);
                    stopObject.GetComponent<RoadGraphStopMarkerView>().Bind(node.stop_id, node.landmark_id,
                        position, RoadGraphVerificationStatus.Synthetic);
                }
            }

            foreach (FixtureEdge edge in graph.edges)
            {
                if (edge == null || string.IsNullOrWhiteSpace(edge.id) ||
                    !nodesById.ContainsKey(edge.from_node) || !nodesById.ContainsKey(edge.to_node) ||
                    edge.geometry_m == null || edge.geometry_m.Length < 2)
                    throw new System.InvalidOperationException("Fixture has a malformed edge or missing endpoint: " + (edge?.id ?? "<null>"));

                var geometry = new List<MapPositionDto>(edge.geometry_m.Length);
                foreach (FixturePosition point in edge.geometry_m) geometry.Add(ToMapPosition(point));
                var edgeObject = (GameObject)PrefabUtility.InstantiatePrefab(edgePrefab, preview);
                edgeObject.transform.SetParent(edgesRoot.transform, true);
                edgeObject.GetComponent<RoadGraphEdgeView>().Bind(edge.id, edge.from_node, edge.to_node,
                    geometry, RoadGraphVerificationStatus.Synthetic);
            }

            CreatePreviewGround(graphRoot.transform);
            CreatePreviewCamera(graphRoot.transform);
            if (!EditorSceneManager.SaveScene(preview, PreviewScenePath))
                throw new System.InvalidOperationException("Unity failed to save the synthetic preview scene.");
            Debug.Log("Created a synthetic-only RoadGraph preview. It is not approved campus routing data.");
        }
        finally
        {
            if (preview.IsValid() && preview.isLoaded) EditorSceneManager.CloseScene(preview, true);
            if (previousScene.IsValid() && previousScene.isLoaded) SceneManager.SetActiveScene(previousScene);
        }
    }

    private static MapPositionDto ToMapPosition(FixturePosition position)
    {
        if (position == null) throw new System.InvalidOperationException("Fixture geometry contains a null coordinate.");
        return new MapPositionDto(position.x, position.y, 0d);
    }

    private static void CreatePreviewGround(Transform parent)
    {
        GameObject ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
        ground.name = "Synthetic Fixture Ground (300m x 150m)";
        ground.transform.SetParent(parent, false);
        ground.transform.localPosition = new Vector3(150f, -0.22f, 70f);
        ground.transform.localScale = new Vector3(30f, 1f, 15f);
        UObject.DestroyImmediate(ground.GetComponent<Collider>());
        ground.GetComponent<Renderer>().sharedMaterial = CreateMaterial("GraphGround.mat", new Color(0.13f, 0.17f, 0.20f));
    }

    private static void CreatePreviewCamera(Transform parent)
    {
        var cameraObject = new GameObject("Preview Camera");
        cameraObject.transform.SetParent(parent, false);
        cameraObject.transform.position = new Vector3(150f, 250f, 70f);
        cameraObject.transform.LookAt(new Vector3(150f, 0f, 70f));
        var camera = cameraObject.AddComponent<Camera>();
        camera.orthographic = true;
        camera.orthographicSize = 90f;
        camera.clearFlags = CameraClearFlags.SolidColor;
        camera.backgroundColor = new Color(0.06f, 0.08f, 0.10f);

        var lightObject = new GameObject("Preview Directional Light");
        lightObject.transform.SetParent(parent, false);
        lightObject.transform.rotation = Quaternion.Euler(50f, -30f, 0f);
        lightObject.AddComponent<Light>().type = LightType.Directional;
    }

    // Unity JsonUtility fills these fixture fields by reflection.
#pragma warning disable 0649
    [Serializable]
    private sealed class FixtureGraph
    {
        public int schema_version;
        public string map_id;
        public string data_status;
        public FixtureNode[] nodes;
        public FixtureEdge[] edges;
    }

    [Serializable]
    private sealed class FixtureNode
    {
        public string id;
        public string landmark_id;
        public string stop_id;
        public FixturePosition position_m;
    }

    [Serializable]
    private sealed class FixtureEdge
    {
        public string id;
        public string from_node;
        public string to_node;
        public FixturePosition[] geometry_m;
    }

    [Serializable]
    private sealed class FixturePosition
    {
        public double x;
        public double y;
    }
#pragma warning restore 0649

    private static void SaveMarker(string fileName, string objectName, Vector3 scale, PrimitiveType shape, Material material, bool addTop)
    {
        string path = Root + "/" + fileName;
        GameObject root = new GameObject(objectName);
        try
        {
            GameObject body = GameObject.CreatePrimitive(shape);
            body.name = "StatusVisual";
            body.transform.SetParent(root.transform, false);
            body.transform.localScale = scale;
            if (shape == PrimitiveType.Sphere) body.transform.localPosition = new Vector3(0f, 0.55f, 0f);
            UObject.DestroyImmediate(body.GetComponent<Collider>());
            body.GetComponent<Renderer>().sharedMaterial = material;
            if (addTop)
            {
                GameObject center = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                center.name = "CenterVisual";
                center.transform.SetParent(root.transform, false);
                center.transform.localPosition = new Vector3(0f, 0.35f, 0f);
                center.transform.localScale = new Vector3(0.3f, 0.3f, 0.3f);
                UObject.DestroyImmediate(center.GetComponent<Collider>());
                center.GetComponent<Renderer>().sharedMaterial = material;
            }
            if (shape == PrimitiveType.Sphere) root.AddComponent<RoadGraphNodeMarkerView>();
            else root.AddComponent<RoadGraphStopMarkerView>();
            PrefabUtility.SaveAsPrefabAsset(root, path);
        }
        finally { UObject.DestroyImmediate(root); }
    }

    private static void SaveEdge(Material material)
    {
        GameObject root = new GameObject("RoadGraphEdge");
        try
        {
            var line = root.AddComponent<LineRenderer>();
            line.useWorldSpace = true;
            line.positionCount = 2;
            line.SetPosition(0, Vector3.zero);
            line.SetPosition(1, Vector3.forward);
            line.startWidth = 0.18f;
            line.endWidth = 0.18f;
            line.sharedMaterial = material;
            line.numCapVertices = 2;
            root.AddComponent<RoadGraphEdgeView>();
            PrefabUtility.SaveAsPrefabAsset(root, Root + "/RoadGraphEdge.prefab");
        }
        finally { UObject.DestroyImmediate(root); }
    }

    private static Material CreateMaterial(string fileName, Color color)
    {
        string path = Root + "/" + fileName;
        Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (!material)
        {
            Shader shader = Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Unlit/Color");
            if (!shader) throw new System.InvalidOperationException("Could not find a supported unlit shader.");
            material = new Material(shader);
            AssetDatabase.CreateAsset(material, path);
        }
        if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", color);
        if (material.HasProperty("_Color")) material.SetColor("_Color", color);
        EditorUtility.SetDirty(material);
        return material;
    }

    private static void EnsureFolder(string path)
    {
        string current = "Assets";
        foreach (string part in path.Substring("Assets/".Length).Split('/'))
        {
            string next = current + "/" + part;
            if (!AssetDatabase.IsValidFolder(next)) AssetDatabase.CreateFolder(current, part);
            current = next;
        }
    }
}
