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
    public sealed class PoseProbeVehicle : MonoBehaviour, IAutonomousVehicle
    {
        public string OperatorVehicleId => "V01";
        public Transform VehicleTransform => transform;
        public Rigidbody VehicleBody => GetComponent<Rigidbody>();
        public float WheelbaseM => 1.3f;
        public float MaximumSteeringRad => .52f;
        public bool StopRequested = true;
        public bool SetDriveCommand(float speed, float angle, bool emergencyStop = false)
        { StopRequested = emergencyStop; return true; }
    }
    public sealed class PhysicalPoseReconnectTests
    {
        [Serializable] private class Info { public string run_id, geometry_hash; }
        [Serializable] private class Pose { public int connection, tick; public float x, z; }
        [Serializable] private class Poses { public Pose[] samples; }
        [UnityTest]
        public IEnumerator ReconnectFirstPacketUsesNewRigidbodyPose()
        {
            var start = new ProcessStartInfo {
                FileName = Environment.GetEnvironmentVariable("CAMPUS_BRAKE_TEST_PYTHON") ?? "python",
                Arguments = "-u \"" + Path.GetFullPath(Path.Combine(Application.dataPath, "../AgentScripts/pose_reconnect_fixture.py")) + "\"",
                UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true
            };
            GameObject car = null, mapObject = null;
            using (var process = Process.Start(start))
            try
            {
                var line = process.StandardOutput.ReadLineAsync();
                float until = Time.realtimeSinceStartup + 8;
                while (!line.IsCompleted && Time.realtimeSinceStartup < until) yield return null;
                Assert.That(line.IsCompleted, Is.True);
                Assert.That(int.TryParse(line.GetAwaiter().GetResult(), out int port), Is.True);
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
                car = new GameObject("pose-probe");
                var body = car.AddComponent<Rigidbody>(); body.isKinematic = true;
                body.position = new Vector3(70.07f, 0, -159.69f);
                car.AddComponent<BoxCollider>();
                for (int i=0;i<4;i++)
                {
                    var wheel = new GameObject("wheel"); wheel.transform.SetParent(car.transform, false);
                    wheel.transform.localPosition = new Vector3(i%2==0 ? -.4f : .4f, 0, i<2 ? -.65f : .65f);
                    wheel.AddComponent<WheelCollider>();
                }
                var probe = car.AddComponent<PoseProbeVehicle>();
                var client = car.AddComponent<UnityAutonomousClient>(); client.Initialize(map, probe);
                until = Time.realtimeSinceStartup + 10;
                while ((client.ConnectionGeneration < 1 || client.Connected) && Time.realtimeSinceStartup < until) yield return null;
                Assert.That(client.ConnectionGeneration, Is.EqualTo(1));
                Assert.That(client.Connected, Is.False);
                var fresh = new Vector3(70.57f, 0, -159.19f); body.position = fresh;
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
                    Assert.That(first.x, Is.EqualTo(fresh.x).Within(.001f));
                    Assert.That(first.z, Is.EqualTo(fresh.z).Within(.001f));
                }
                Assert.That(body.position, Is.EqualTo(fresh));
                Assert.That(probe.StopRequested, Is.True);
            }
            finally
            {
                if (car != null) Object.Destroy(car);
                if (mapObject != null) Object.Destroy(mapObject);
                if (!process.HasExited) { process.Kill(); process.WaitForExit(2000); }
            }
        }
    }
}
