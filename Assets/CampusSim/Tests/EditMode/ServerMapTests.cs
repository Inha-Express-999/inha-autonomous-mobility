using System;
using System.IO;
using System.Linq;
using InhaExpress.Client.MapClient;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace InhaExpress.Client.Tests
{
    public sealed class ServerMapTests
    {
        private const string MapPath = "Assets/CampusSim/Data/server_map.json";
        private const string ScenePath = "Assets/CampusSim/Scenes/CampusTerrain.unity";

        [Test]
        public void ExportHasExactServerTopologyAndRejectsInvalidBindings()
        {
            var json = File.ReadAllText(MapPath);
            var map = ServerMapDocument.Parse(json);
            Assert.That(map.nodes.Length, Is.EqualTo(187));
            Assert.That(map.edges.Length, Is.EqualTo(402));
            Assert.That(map.hubs.Length, Is.EqualTo(3));
            Assert.That(map.stops.Length, Is.EqualTo(14));
            var hub = map.hubs.Single(h => h.id == "west_field_parking");
            Assert.That(hub.node_id, Is.EqualTo("osm:10058484941"));
            Assert.That(hub.vehicle_ids, Is.EqualTo(new[] { "V03" }));
            map.hubs[0].position += Vector3.right;
            Assert.Throws<ArgumentException>(() => ServerMapDocument.Parse(JsonUtility.ToJson(map)));
            map = ServerMapDocument.Parse(json);
            map.nodes[1].id = map.nodes[0].id;
            Assert.Throws<ArgumentException>(() => ServerMapDocument.Parse(JsonUtility.ToJson(map)));
            map = ServerMapDocument.Parse(json);
            map.vehicle_actuation = true;
            Assert.Throws<ArgumentException>(() => ServerMapDocument.Parse(JsonUtility.ToJson(map)));
            map = ServerMapDocument.Parse(json);
            map.schema_version++;
            Assert.Throws<ArgumentException>(() => ServerMapDocument.Parse(JsonUtility.ToJson(map)));
        }

        [Test]
        public void RemoteMapMismatchIsRejectedEvenIfItReportsTheSameHash()
        {
            var root = new GameObject("Read-only map validation test");
            var asset = new TextAsset(File.ReadAllText(MapPath));
            try
            {
                var map = ServerMapDocument.Parse(asset.text);
                var client = root.AddComponent<CampusServerMap>();
                client.authoredMap = asset;
                client.geometryHash = map.geometry_hash;
                client.mapVersion = map.map_version;
                Assert.DoesNotThrow(() => client.ValidateRemote(asset.text));
                map.nodes[0].position += Vector3.right;
                Assert.Throws<ArgumentException>(() => client.ValidateRemote(JsonUtility.ToJson(map)));
                map = ServerMapDocument.Parse(asset.text);
                map.geometry_hash = new string('0', 64);
                Assert.Throws<ArgumentException>(() => client.ValidateRemote(JsonUtility.ToJson(map)));
            }
            finally { UnityEngine.Object.DestroyImmediate(root); UnityEngine.Object.DestroyImmediate(asset); }
        }

        [Test]
        public void ExistingCampusTerrainContainsAllAnchorsAtServerXZWithoutPhysicsComponents()
        {
            var scene = SceneManager.GetSceneByPath(ScenePath);
            bool opened = !scene.IsValid() || !scene.isLoaded;
            if (opened) scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Additive);
            try
            {
                var vehicle = scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<Transform>(true))
                    .Single(t => t.name == "Annyoung Car");
                Assert.That(GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(vehicle.gameObject), Is.Zero,
                    "The scene instance must not retain the removed VehicleWebSocketClient reference.");
                Assert.That(vehicle.GetComponents<MonoBehaviour>().Any(c => c != null && c.GetType().Name == "VehicleController"), Is.True);
                var roots = scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<CampusServerMap>(true)).ToArray();
                Assert.That(roots.Length, Is.EqualTo(1));
                var overlay = roots[0];
                var map = ServerMapDocument.Parse(AssetDatabase.LoadAssetAtPath<TextAsset>(MapPath).text);
                Assert.That(overlay.geometryHash, Is.EqualTo(map.geometry_hash));
                Assert.That(overlay.markers.Length, Is.EqualTo(204));
                Assert.That(overlay.markers.Count(m => m.kind == ServerMapMarker.MarkerKind.RoadNode), Is.EqualTo(187));
                Assert.That(overlay.markers.Count(m => m.kind == ServerMapMarker.MarkerKind.Hub), Is.EqualTo(3));
                Assert.That(overlay.markers.Count(m => m.kind == ServerMapMarker.MarkerKind.Stop), Is.EqualTo(14));
                foreach (var marker in overlay.markers)
                {
                    var server = map.nodes.Single(n => n.id == marker.nodeId).position;
                    Assert.That(marker.transform.position.x, Is.EqualTo(server.x).Within(.01));
                    Assert.That(marker.transform.position.z, Is.EqualTo(server.z).Within(.01));
                    Assert.That(marker.operationallyVerified, Is.False);
                    Assert.That(marker.surfaceSource, Is.Not.Empty);
                }
                Assert.That(overlay.GetComponentsInChildren<Collider>(true), Is.Empty);
                Assert.That(overlay.GetComponentsInChildren<Rigidbody>(true), Is.Empty);
                // Display models now live below service points. Only the
                // importer-owned debug marker meshes remain combined in three draws.
                var views=overlay.transform.Cast<Transform>().Where(t=>t.name.EndsWith(" visual markers")).ToArray();
                var renderers = views.Select(t=>t.GetComponent<MeshRenderer>()).ToArray();
                Assert.That(renderers.Length, Is.EqualTo(3));
                Assert.That(renderers.All(r => r.sharedMaterial != null), Is.True);
                Assert.That(views.All(t=>t.GetComponent<MeshFilter>().sharedMesh.vertexCount > 0), Is.True);
                var models=overlay.GetComponentsInChildren<ServicePointVisual>(true);
                Assert.That(models.Length,Is.EqualTo(17));
                foreach(var model in models)
                {
                    var point=model.transform.parent.GetComponent<ServerMapMarker>();
                    Assert.That(point,Is.Not.Null);
                    Assert.That(point.serverId,Is.EqualTo(model.pointId));
                    Assert.That(point.kind,Is.EqualTo(model.pointKind));
                    Assert.That(point.kind,Is.Not.EqualTo(ServerMapMarker.MarkerKind.RoadNode));
                }
                var vehicles=scene.GetRootGameObjects().SelectMany(g=>g.GetComponentsInChildren<Rigidbody>(true));
                Assert.That(vehicles.All(v=>v.GetComponentInParent<ServerMapMarker>()==null),Is.True);
            }
            finally { if (opened) EditorSceneManager.CloseScene(scene, true); }
        }
    }
}
