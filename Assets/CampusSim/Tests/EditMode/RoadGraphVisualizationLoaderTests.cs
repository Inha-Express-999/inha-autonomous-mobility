using System;
using System.Linq;
using System.IO;
using System.Reflection;
using InhaExpress.Client.Presentation;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace InhaExpress.Client.Tests
{
    public sealed class RoadGraphVisualizationLoaderTests
    {
        private const string FixturePath = "maps/fixtures/campus-synthetic-6.json";
        private GameObject loaderObject;
        private RoadGraphVisualizationLoader loader;
        private string fixtureJson;

        [SetUp]
        public void SetUp()
        {
            loaderObject = new GameObject("RoadGraphVisualizationLoaderTests");
            loader = loaderObject.AddComponent<RoadGraphVisualizationLoader>();
            SetPrefab("nodePrefab", "Assets/CampusSim/Prefabs/Navigation/RoadGraphNode.prefab");
            SetPrefab("stopPrefab", "Assets/CampusSim/Prefabs/Navigation/RoadGraphStop.prefab");
            SetPrefab("edgePrefab", "Assets/CampusSim/Prefabs/Navigation/RoadGraphEdge.prefab");
            string projectRoot = Directory.GetParent(Application.dataPath).FullName;
            fixtureJson = File.ReadAllText(Path.Combine(projectRoot, FixturePath));
        }

        [TearDown]
        public void TearDown()
        {
            if (loaderObject) UnityEngine.Object.DestroyImmediate(loaderObject);
        }

        [Test]
        public void SyntheticFixtureLoadsPrefabViewsAndLocalCoordinates()
        {
            RoadGraphVisualizationLoadResult result = loader.LoadJson(fixtureJson);

            Assert.That(result.MapId, Is.EqualTo("campus-synthetic-6"));
            Assert.That(result.MapVersion, Is.EqualTo("synthetic-campus-6stop-v1"));
            Assert.That(result.NodeCount, Is.EqualTo(6));
            Assert.That(result.EdgeCount, Is.EqualTo(12));
            Assert.That(loaderObject.GetComponentsInChildren<RoadGraphNodeMarkerView>(true), Has.Length.EqualTo(6));
            Assert.That(loaderObject.GetComponentsInChildren<RoadGraphStopMarkerView>(true), Has.Length.EqualTo(6));
            Assert.That(loaderObject.GetComponentsInChildren<RoadGraphEdgeView>(true), Has.Length.EqualTo(12));

            RoadGraphNodeMarkerView eastNode = loaderObject.GetComponentsInChildren<RoadGraphNodeMarkerView>(true)
                .Single(item => item.NodeId == "n2");
            Assert.That(eastNode.transform.localPosition, Is.EqualTo(new Vector3(100f, 0f, 0f)));
            RoadGraphEdgeView curvedEdge = loaderObject.GetComponentsInChildren<RoadGraphEdgeView>(true)
                .Single(item => item.EdgeId == "e56");
            LineRenderer curvedLine = curvedEdge.GetComponent<LineRenderer>();
            Assert.That(curvedLine, Is.Not.Null);
            Assert.That(curvedLine.useWorldSpace, Is.False);
            Assert.That(curvedLine.GetPosition(1), Is.EqualTo(new Vector3(170f, 0f, 140f)));
            Assert.That(curvedEdge.VerificationStatus, Is.EqualTo(RoadGraphVerificationStatus.Synthetic));
        }

        [TestCase("\"id\": \"n2\"", "\"id\": \"n1\"")]
        [TestCase("\"from_node\": \"n1\"", "\"from_node\": \"missing_node\"")]
        [TestCase("\"schema_version\": 1", "\"schema_version\": 2")]
        public void InvalidGraphIsRejectedWithoutReplacingCurrentDisplay(string source, string replacement)
        {
            loader.LoadJson(fixtureJson);
            string loadedVersion = loader.LoadedMapVersion;
            string invalidJson = fixtureJson.Replace(source, replacement);

            Assert.Throws<FormatException>(() => loader.LoadJson(invalidJson));

            Assert.That(loader.LoadedMapVersion, Is.EqualTo(loadedVersion));
            Assert.That(loaderObject.GetComponentsInChildren<RoadGraphNodeMarkerView>(true), Has.Length.EqualTo(6));
            Assert.That(loaderObject.GetComponentsInChildren<RoadGraphEdgeView>(true), Has.Length.EqualTo(12));
        }

        [Test]
        public void ClearRemovesOnlyOwnedGraphAndResetsVersion()
        {
            loader.LoadJson(fixtureJson);
            GameObject unrelated = new GameObject("unrelated child");
            unrelated.transform.SetParent(loaderObject.transform, false);

            loader.Clear();

            Assert.That(loader.LoadedMapId, Is.Null);
            Assert.That(loader.LoadedMapVersion, Is.Null);
            Assert.That(loaderObject.transform.Find("unrelated child"), Is.Not.Null);
            Assert.That(loaderObject.GetComponentsInChildren<RoadGraphNodeMarkerView>(true), Is.Empty);
            UnityEngine.Object.DestroyImmediate(unrelated);
        }

        private void SetPrefab(string fieldName, string assetPath)
        {
            var field = typeof(RoadGraphVisualizationLoader).GetField(fieldName,
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(field, Is.Not.Null, "Expected serialized loader field " + fieldName);
            field.SetValue(loader, AssetDatabase.LoadAssetAtPath<GameObject>(assetPath));
            Assert.That(field.GetValue(loader), Is.Not.Null, "Could not load prefab " + assetPath);
        }
    }
}
