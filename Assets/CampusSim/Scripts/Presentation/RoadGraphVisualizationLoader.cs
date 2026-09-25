using System;
using System.Collections.Generic;
using InhaExpress.Client.Domain;
using UnityEngine;

namespace InhaExpress.Client.Presentation
{
    /// <summary>
    /// Loads a versioned RoadGraph document into visualization-only prefabs.
    /// It does not make Unity scene objects authoritative routing data.
    /// </summary>
    public sealed class RoadGraphVisualizationLoader : MonoBehaviour
    {
        [SerializeField] private GameObject nodePrefab;
        [SerializeField] private GameObject stopPrefab;
        [SerializeField] private GameObject edgePrefab;

        private GameObject loadedGraphRoot;

        public string LoadedMapId { get; private set; }
        public string LoadedMapVersion { get; private set; }

        public RoadGraphVisualizationLoadResult LoadJson(string json)
        {
            RoadGraphJson graph = ParseAndValidate(json);
            ValidatePrefabs();

            GameObject candidateRoot = null;
            try
            {
                candidateRoot = new GameObject("RoadGraph " + graph.map_id + " [SYNTHETIC]");
                candidateRoot.transform.SetParent(transform, false);
                candidateRoot.transform.localPosition = Vector3.zero;
                candidateRoot.transform.localRotation = Quaternion.identity;
                candidateRoot.transform.localScale = Vector3.one;

                var nodesRoot = new GameObject("Nodes");
                nodesRoot.transform.SetParent(candidateRoot.transform, false);
                var stopsRoot = new GameObject("Stops");
                stopsRoot.transform.SetParent(candidateRoot.transform, false);
                var edgesRoot = new GameObject("Directed Edges");
                edgesRoot.transform.SetParent(candidateRoot.transform, false);

                foreach (RoadGraphNodeJson node in graph.nodes)
                {
                    MapPositionDto position = new MapPositionDto(node.position_m.x, node.position_m.y, 0d);
                    var nodeObject = Instantiate(nodePrefab, nodesRoot.transform, false);
                    nodeObject.name = "Node " + node.id;
                    RoadGraphNodeMarkerView nodeView = nodeObject.GetComponent<RoadGraphNodeMarkerView>();
                    if (!nodeView) throw new InvalidOperationException("Node prefab instance lost its marker component.");
                    nodeView.Bind(
                        node.id, node.landmark_id, position, RoadGraphVerificationStatus.Synthetic);

                    var stopObject = Instantiate(stopPrefab, stopsRoot.transform, false);
                    stopObject.name = "Stop " + node.stop_id;
                    RoadGraphStopMarkerView stopView = stopObject.GetComponent<RoadGraphStopMarkerView>();
                    if (!stopView) throw new InvalidOperationException("Stop prefab instance lost its marker component.");
                    stopView.Bind(
                        node.stop_id, node.landmark_id, position, RoadGraphVerificationStatus.Synthetic);
                }

                foreach (RoadGraphEdgeJson edge in graph.edges)
                {
                    var geometry = new List<MapPositionDto>(edge.geometry_m.Length);
                    foreach (RoadGraphPointJson point in edge.geometry_m)
                        geometry.Add(new MapPositionDto(point.x, point.y, 0d));

                    var edgeObject = Instantiate(edgePrefab, edgesRoot.transform, false);
                    edgeObject.name = "Edge " + edge.id;
                    RoadGraphEdgeView edgeView = edgeObject.GetComponent<RoadGraphEdgeView>();
                    if (!edgeView) throw new InvalidOperationException("Edge prefab instance lost its view component.");
                    edgeView.Bind(
                        edge.id, edge.from_node, edge.to_node, geometry,
                        edge.is_open ? RoadGraphVerificationStatus.Synthetic : RoadGraphVerificationStatus.Closed);
                }

                GameObject previousRoot = loadedGraphRoot;
                loadedGraphRoot = candidateRoot;
                candidateRoot = null;
                LoadedMapId = graph.map_id;
                LoadedMapVersion = graph.map_version;
                DestroyOwnedRoot(previousRoot);

                return new RoadGraphVisualizationLoadResult(graph.map_id, graph.map_version,
                    graph.nodes.Length, graph.edges.Length);
            }
            catch
            {
                DestroyOwnedRoot(candidateRoot);
                throw;
            }
        }

        public void Clear()
        {
            DestroyOwnedRoot(loadedGraphRoot);
            loadedGraphRoot = null;
            LoadedMapId = null;
            LoadedMapVersion = null;
        }

        private void ValidatePrefabs()
        {
            if (!nodePrefab || !nodePrefab.GetComponent<RoadGraphNodeMarkerView>())
                throw new InvalidOperationException("Node prefab must have a RoadGraphNodeMarkerView component.");
            if (!stopPrefab || !stopPrefab.GetComponent<RoadGraphStopMarkerView>())
                throw new InvalidOperationException("Stop prefab must have a RoadGraphStopMarkerView component.");
            if (!edgePrefab || !edgePrefab.GetComponent<RoadGraphEdgeView>() || !edgePrefab.GetComponent<LineRenderer>())
                throw new InvalidOperationException("Edge prefab must have RoadGraphEdgeView and LineRenderer components.");
        }

        private static RoadGraphJson ParseAndValidate(string json)
        {
            if (string.IsNullOrWhiteSpace(json)) throw new ArgumentException("RoadGraph JSON is required.", nameof(json));

            RoadGraphJson graph;
            try { graph = JsonUtility.FromJson<RoadGraphJson>(json); }
            catch (Exception exception) { throw new FormatException("RoadGraph JSON could not be parsed.", exception); }

            if (graph == null || graph.schema_version != 1 ||
                graph.data_status != "SYNTHETIC_FIXTURE" ||
                graph.verification_status != "SYNTHETIC_FIXTURE" ||
                graph.coordinate_frame != "SYNTHETIC_LOCAL_METERS")
                throw new FormatException("Only schema-v1 SYNTHETIC_FIXTURE RoadGraph documents are supported.");
            RequireText(graph.map_id, "map_id");
            RequireText(graph.map_version, "map_version");
            RequireText(graph.source_description, "source_description");
            if (graph.nodes == null || graph.nodes.Length < 2)
                throw new FormatException("RoadGraph must contain at least two nodes.");
            if (graph.edges == null || graph.edges.Length < 1)
                throw new FormatException("RoadGraph must contain at least one edge.");

            var nodesById = new Dictionary<string, RoadGraphNodeJson>(StringComparer.Ordinal);
            var stopIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (RoadGraphNodeJson node in graph.nodes)
            {
                if (node == null) throw new FormatException("RoadGraph contains a null node.");
                RequireText(node.id, "node.id");
                RequireText(node.landmark_id, "node.landmark_id");
                RequireText(node.stop_id, "node.stop_id");
                if (node.position_m == null || !Finite(node.position_m.x) || !Finite(node.position_m.y))
                    throw new FormatException("Node " + node.id + " has a missing or non-finite position.");
                if (!nodesById.TryAdd(node.id, node)) throw new FormatException("Duplicate node id: " + node.id);
                if (!stopIds.Add(node.stop_id)) throw new FormatException("Duplicate stop id: " + node.stop_id);
            }

            var edgeIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (RoadGraphEdgeJson edge in graph.edges)
            {
                if (edge == null) throw new FormatException("RoadGraph contains a null edge.");
                RequireText(edge.id, "edge.id");
                RequireText(edge.from_node, "edge.from_node");
                RequireText(edge.to_node, "edge.to_node");
                RequireText(edge.source, "edge.source");
                if (!edgeIds.Add(edge.id)) throw new FormatException("Duplicate edge id: " + edge.id);
                if (!nodesById.TryGetValue(edge.from_node, out RoadGraphNodeJson from) ||
                    !nodesById.TryGetValue(edge.to_node, out RoadGraphNodeJson to))
                    throw new FormatException("Edge " + edge.id + " references an unknown endpoint.");
                if (edge.verification_status != "SYNTHETIC")
                    throw new FormatException("Edge " + edge.id + " is not marked SYNTHETIC.");
                if (!Finite(edge.length_m) || edge.length_m <= 0f ||
                    !Finite(edge.width_m) || edge.width_m <= 0f ||
                    !Finite(edge.allowed_speed_mps) || edge.allowed_speed_mps <= 0f)
                    throw new FormatException("Edge " + edge.id + " has invalid length, width, or speed.");
                if (edge.geometry_m == null || edge.geometry_m.Length < 2)
                    throw new FormatException("Edge " + edge.id + " needs at least two geometry points.");

                double geometryLength = 0d;
                for (int i = 0; i < edge.geometry_m.Length; i++)
                {
                    RoadGraphPointJson point = edge.geometry_m[i];
                    if (point == null || !Finite(point.x) || !Finite(point.y))
                        throw new FormatException("Edge " + edge.id + " has a missing or non-finite geometry point.");
                    if (i > 0)
                    {
                        RoadGraphPointJson previous = edge.geometry_m[i - 1];
                        geometryLength += Math.Sqrt(Square(point.x - previous.x) + Square(point.y - previous.y));
                    }
                }
                if (!SamePoint(edge.geometry_m[0], from.position_m) ||
                    !SamePoint(edge.geometry_m[edge.geometry_m.Length - 1], to.position_m))
                    throw new FormatException("Edge " + edge.id + " geometry does not meet its endpoint nodes.");
                if (edge.length_m + 0.01d < geometryLength)
                    throw new FormatException("Edge " + edge.id + " length is shorter than its geometry.");
            }
            return graph;
        }

        private static void DestroyOwnedRoot(GameObject root)
        {
            if (!root) return;
            if (Application.isPlaying) Destroy(root);
            else DestroyImmediate(root);
        }

        private static void RequireText(string value, string field)
        {
            if (string.IsNullOrWhiteSpace(value)) throw new FormatException("RoadGraph field " + field + " is required.");
        }

        private static bool Finite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);
        private static double Square(double value) => value * value;
        private static bool SamePoint(RoadGraphPointJson left, RoadGraphPointJson right) =>
            Math.Sqrt(Square(left.x - right.x) + Square(left.y - right.y)) <= 0.01d;

        [Serializable]
        private sealed class RoadGraphJson
        {
            public int schema_version;
            public string map_id;
            public string map_version;
            public string data_status;
            public string coordinate_frame;
            public string verification_status;
            public string source_description;
            public RoadGraphNodeJson[] nodes;
            public RoadGraphEdgeJson[] edges;
        }

        [Serializable]
        private sealed class RoadGraphNodeJson
        {
            public string id;
            public string landmark_id;
            public string stop_id;
            public RoadGraphPointJson position_m;
        }

        [Serializable]
        private sealed class RoadGraphEdgeJson
        {
            public string id;
            public string from_node;
            public string to_node;
            public RoadGraphPointJson[] geometry_m;
            public float length_m;
            public float width_m;
            public float allowed_speed_mps;
            public string source;
            public string verification_status;
            public bool is_open = true;
        }

        [Serializable]
        private sealed class RoadGraphPointJson
        {
            public double x;
            public double y;
        }
    }

    public sealed class RoadGraphVisualizationLoadResult
    {
        public string MapId { get; }
        public string MapVersion { get; }
        public int NodeCount { get; }
        public int EdgeCount { get; }

        public RoadGraphVisualizationLoadResult(string mapId, string mapVersion, int nodeCount, int edgeCount)
        {
            MapId = mapId;
            MapVersion = mapVersion;
            NodeCount = nodeCount;
            EdgeCount = edgeCount;
        }
    }
}
