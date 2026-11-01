#if UNITY_EDITOR

using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;
using System.Linq;
using System;
using System.Collections;
using System.Diagnostics;
using System.IO;
using InhaExpress.Client.MapClient;
using InhaExpress.Client.Presentation;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;
namespace InhaExpress.Client.Tests
{
    public sealed class CampusSceneReconnectTests
    {
        [Serializable] private class Info { public string run_id, geometry_hash; }
        [Serializable] private class Pose { public int connection, tick; public float x, z; }
        [Serializable] private class Poses { public Pose[] samples; }
        [UnityTest]
        public IEnumerator CampusV01BrakesAndSendsFreshPose()
        {
            const string terrain = "Assets/CampusSim/Scenes/CampusTerrain.unity";
            Assert.That(SceneManager.GetSceneByPath(terrain).isLoaded, Is.False);
            yield return EditorSceneManager.LoadSceneAsyncInPlayMode(terrain, new LoadSceneParameters(LoadSceneMode.Additive));
            var scene = SceneManager.GetSceneByPath(terrain);
            var start = PhysicalPoseReconnectTests.FixtureStartInfo();
            GameObject car = null, mapObject = null;
            using (var process = Process.Start(start))
            try
            {
                var errors = process.StandardError.ReadToEndAsync();
                var line = process.StandardOutput.ReadLineAsync();
                float until = Time.realtimeSinceStartup + 8;
                while (!line.IsCompleted && Time.realtimeSinceStartup < until) yield return null;
                Assert.That(line.IsCompleted, Is.True);
                Assert.That(int.TryParse(line.GetAwaiter().GetResult(), out int port), Is.True, errors.IsCompleted ? errors.GetAwaiter().GetResult() : "Python fixture did not report a port");
                string url = "http://127.0.0.1:" + port;
                yield return new WaitForSecondsRealtime(1);
                Info info;
                using (var http = UnityWebRequest.Get(url + "/v1/desktop/info"))
                {
                    http.timeout = 5; yield return http.SendWebRequest();
                    Assert.That(http.result, Is.EqualTo(UnityWebRequest.Result.Success));
                    info = JsonUtility.FromJson<Info>(http.downloadHandler.text);
                }
                mapObject = new GameObject("test-map"); mapObject.SetActive(false);
                var map = mapObject.AddComponent<CampusServerMap>(); map.enabled = false;
                map.serverUrl = url; map.geometryHash = info.geometry_hash;
                typeof(CampusServerMap).GetProperty("RunId").SetValue(map, info.run_id);
                var vehicle = scene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<VehicleController>())
                    .Single(v => v.OperatorVehicleId == "V01");
                car = vehicle.gameObject;
                Assert.That(car.scene.path, Is.EqualTo(terrain));
                var body = vehicle.VehicleBody;
                Assert.That(body.isKinematic, Is.False);
                var wheels = car.GetComponentsInChildren<WheelCollider>();
                Assert.That(wheels.Length, Is.EqualTo(4));
                Assert.That(car.GetComponent<UnityAutonomousClient>(), Is.Null);
                var client = car.AddComponent<UnityAutonomousClient>(); client.Initialize(map, vehicle);
                until = Time.realtimeSinceStartup + 10;
                while ((client.ConnectionGeneration < 1 || client.Connected) && Time.realtimeSinceStartup < until) yield return null;
                Assert.That(client.ConnectionGeneration, Is.EqualTo(1));
                Assert.That(client.Connected, Is.False);
                yield return new WaitForFixedUpdate();
                Assert.That(client.Connected, Is.False);
                AssertBraked(wheels);
                // Hold the test displacement during reconnect so a stale sample is distinguishable.
                var fresh = body.position + Vector3.right * .5f;
                body.constraints |= RigidbodyConstraints.FreezePositionX | RigidbodyConstraints.FreezePositionZ;
                body.position = fresh;
                until = Time.realtimeSinceStartup + 10;
                while ((client.ConnectionGeneration < 2 || !client.Connected) && Time.realtimeSinceStartup < until) yield return null;
                Assert.That(client.ConnectionGeneration, Is.GreaterThanOrEqualTo(2));
                yield return new WaitForSecondsRealtime(.5f);
                using (var http = UnityWebRequest.Get(url + "/__test/poses"))
                {
                    http.timeout = 5; yield return http.SendWebRequest();
                    Assert.That(http.result, Is.EqualTo(UnityWebRequest.Result.Success));
                    var frames = JsonUtility.FromJson<Poses>(http.downloadHandler.text).samples;
                    var first = Array.Find(frames, p => p.connection == 2);
                    Assert.That(first, Is.Not.Null);
                    var before = frames.First(p => p.connection == 1);
                    Assert.That(first.tick, Is.GreaterThan(before.tick));
                    Assert.That(Mathf.Abs(first.x - before.x), Is.GreaterThan(.25f));
                    Assert.That(first.x, Is.EqualTo(fresh.x).Within(.001f));
                    Assert.That(first.z, Is.EqualTo(fresh.z).Within(.001f));
                }
                AssertBraked(wheels);
            }
            finally
            {
                if (scene.isLoaded) SceneManager.UnloadSceneAsync(scene);
                if (mapObject != null) Object.Destroy(mapObject);
                if (!process.HasExited) { process.Kill(); process.WaitForExit(2000); }
            }
        }
        private static void AssertBraked(WheelCollider[] wheels)
        {
            foreach (var wheel in wheels)
            {
                Assert.That(wheel.brakeTorque, Is.GreaterThan(0f), wheel.name);
                Assert.That(wheel.motorTorque, Is.EqualTo(0f), wheel.name);
            }
        }
    }
}

#endif
