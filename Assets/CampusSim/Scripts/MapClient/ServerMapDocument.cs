using System;
using System.Collections.Generic;
using UnityEngine;

namespace InhaExpress.Client.MapClient
{
    [Serializable] public sealed class ServerMapNode
    {
        public string id;
        public Vector3 position;
    }

    [Serializable] public sealed class ServerMapEdge
    {
        public string u, v, key;
    }

    [Serializable] public sealed class ServerMapAnchor
    {
        public string id, name, node_id, landmark_id;
        public Vector3 position;
        public string[] vehicle_ids;
    }

    [Serializable] public sealed class ServerMapDocument
    {
        public int schema_version;
        public string project_version, map_version, geometry_hash, coordinate_system, height_mode, attribution;
        public bool simulation_only, operationally_verified, vehicle_actuation;
        public ServerMapNode[] nodes;
        public ServerMapEdge[] edges;
        public ServerMapAnchor[] hubs, stops;

        public static ServerMapDocument Parse(string json)
        {
            var document = JsonUtility.FromJson<ServerMapDocument>(json);
            if (document == null || document.schema_version != 1 || !document.simulation_only ||
                document.operationally_verified || document.vehicle_actuation ||
                document.coordinate_system != "unity_legacy_x_east_z_north_m" ||
                document.height_mode != "project_to_unity_surface_visual_only" ||
                string.IsNullOrEmpty(document.project_version) || string.IsNullOrEmpty(document.map_version) ||
                string.IsNullOrEmpty(document.geometry_hash) || document.geometry_hash.Length != 64 ||
                document.nodes == null || document.nodes.Length == 0 || document.edges == null ||
                document.hubs == null || document.stops == null)
                throw new ArgumentException("Unsupported or incomplete read-only server map.");

            var nodesById = new Dictionary<string, Vector3>();
            foreach (var node in document.nodes)
            {
                if (node == null || string.IsNullOrEmpty(node.id) || !Finite(node.position) ||
                    node.position.y != 0 || nodesById.ContainsKey(node.id))
                    throw new ArgumentException("Invalid or duplicate map node.");
                nodesById.Add(node.id, node.position);
            }
            var edges = new HashSet<string>();
            foreach (var edge in document.edges)
                if (edge == null || !nodesById.ContainsKey(edge.u ?? "") ||
                    !nodesById.ContainsKey(edge.v ?? "") || string.IsNullOrEmpty(edge.key) ||
                    !edges.Add(edge.u + "\n" + edge.v + "\n" + edge.key))
                    throw new ArgumentException("Invalid or duplicate directed map edge.");
            ValidateAnchors(document.hubs, nodesById);
            ValidateAnchors(document.stops, nodesById);
            return document;
        }

        private static void ValidateAnchors(ServerMapAnchor[] anchors, Dictionary<string, Vector3> nodes)
        {
            var ids = new HashSet<string>();
            foreach (var anchor in anchors)
                if (anchor == null || string.IsNullOrEmpty(anchor.id) || !ids.Add(anchor.id) ||
                    !nodes.TryGetValue(anchor.node_id ?? "", out var expected) ||
                    !Finite(anchor.position) || Vector3.Distance(expected, anchor.position) > 0.001f)
                    throw new ArgumentException("Hub/Stop is not bound to its server road node.");
        }

        private static bool Finite(Vector3 value) =>
            !float.IsNaN(value.x) && !float.IsInfinity(value.x) &&
            !float.IsNaN(value.y) && !float.IsInfinity(value.y) &&
            !float.IsNaN(value.z) && !float.IsInfinity(value.z);
    }
}
