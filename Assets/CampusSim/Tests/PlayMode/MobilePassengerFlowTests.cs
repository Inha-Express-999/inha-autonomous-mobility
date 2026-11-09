using System;
using System.Collections;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Text;
using System.Threading;
using InhaExpress.Client.MapClient;
using InhaExpress.Client.Presentation;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace InhaExpress.Client.Tests
{
    public sealed class MobilePassengerFlowTests
    {
        private static FieldInfo Field(string name) => typeof(WebClientHud).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic);
        private static void Call(WebClientHud hud, string method, params object[] args) =>
            typeof(WebClientHud).GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic).Invoke(hud, args);

        [UnityTest]
        public IEnumerator MobileSelectsLandmarksSendsRealHttpOrderAndShowsItsOwnStatus()
        {
            var root = new GameObject("Mobile HTTP flow test"); root.SetActive(false);
            var asset = new TextAsset(File.ReadAllText(Path.Combine(Application.dataPath, "CampusSim/Data/server_map.json")));
            var document = ServerMapDocument.Parse(asset.text);
            string savedPending = PlayerPrefs.GetString("InhaMobil.Passenger.Pending", "");
            PlayerPrefs.DeleteKey("InhaMobil.Passenger.Pending");
            using (var server = new PassengerServer(asset.text, document.map_version))
            try
            {
                var map = root.AddComponent<CampusServerMap>();
                map.authoredMap = asset; map.geometryHash = document.geometry_hash; map.mapVersion = document.map_version;
                map.serverUrl = server.Url; map.pollSeconds = .5f; map.showStatus = false;
                var hud = root.AddComponent<WebClientHud>();
                Field("mobile").SetValue(hud, true); Field("passengerId").SetValue(hud, "mobile-test"); Field("map").SetValue(hud, map);
                var canvas = new GameObject("Mobile Canvas", typeof(RectTransform), typeof(Canvas)); canvas.transform.SetParent(root.transform);
                var rect = (RectTransform)canvas.transform;
                Call(hud, "BuildMobile", rect);
                var vegetation = Array.Find(root.GetComponentsInChildren<Toggle>(true), t => t.name == "Mobile Vegetation");
                Assert.That(vegetation, Is.Not.Null);
                bool savedVegetation = VegetationDisplaySettings.MobileVisible;
                try
                {
                    vegetation.isOn = !savedVegetation;
                    Assert.That(VegetationDisplaySettings.MobileVisible, Is.EqualTo(!savedVegetation));
                    vegetation.isOn = savedVegetation;
                    Assert.That(VegetationDisplaySettings.MobileVisible, Is.EqualTo(savedVegetation));
                }
                finally { VegetationDisplaySettings.SetMobileVisible(savedVegetation); }

                Field("connection").SetValue(hud, FixtureUiFactory.Text(rect, "Connection", "", 12, Color.black));
                Field("session").SetValue(hud, FixtureUiFactory.Text(rect, "Session", "", 12, Color.black));
                root.SetActive(true);
                double until = Time.realtimeSinceStartupAsDouble + 10;
                while (map.RunId == null && Time.realtimeSinceStartupAsDouble < until) yield return null;
                Assert.That(map.RunId, Is.EqualTo("mobile-fixture"), map.LastError);
                Call(hud, "RefreshStopDropdowns");
                var dropdowns = root.GetComponentsInChildren<Dropdown>();
                Assert.That(dropdowns.Length, Is.EqualTo(2));
                dropdowns[0].value = 1;
                int destination = Array.FindIndex(document.stops, s => s.landmark_id != document.stops[0].landmark_id) + 1;
                dropdowns[1].value = destination;
                Assert.That(Field("pickup").GetValue(hud), Is.Not.Null);
                Assert.That(Field("dropoff").GetValue(hud), Is.Not.Null);
                var submit = (Button)Field("requestSubmit").GetValue(hud);
                until = Time.realtimeSinceStartupAsDouble + 3;
                while (!submit.interactable && Time.realtimeSinceStartupAsDouble < until) yield return null;
                Assert.That(submit.interactable, Is.True);
                submit.onClick.Invoke();
                until = Time.realtimeSinceStartupAsDouble + 10;
                while (server.PostCount == 0 && Time.realtimeSinceStartupAsDouble < until) yield return null;
                Assert.That(server.PostCount, Is.EqualTo(1));
                Assert.That(server.Order.owner_id, Is.EqualTo("mobile-test"));
                Assert.That(server.Order.pickup_landmark_id, Is.EqualTo(document.stops[0].landmark_id));
                Assert.That(server.Order.dropoff_landmark_id, Is.EqualTo(document.stops[destination - 1].landmark_id));
                until = Time.realtimeSinceStartupAsDouble + 5;
                var status = (Text)Field("requestStatus").GetValue(hud);
                while ((!status.text.Contains("가용 차량 대기") || submit.GetComponentInChildren<Text>().text != "요청 취소") && Time.realtimeSinceStartupAsDouble < until) yield return null;
                Assert.That(status.text, Does.Contain("가용 차량 대기"));
                Assert.That(submit.interactable, Is.True);
                Assert.That(submit.GetComponentInChildren<Text>().text, Is.EqualTo("요청 취소"));
                var scroll = root.GetComponentInChildren<ScrollRect>();
                Assert.That(scroll, Is.Not.Null, "Compact screens can scroll to connection settings.");
                Assert.That(scroll.horizontal, Is.False);
                Assert.That(submit.transform.parent.GetComponent<RectTransform>().rect.height, Is.GreaterThanOrEqualTo(48));
                map.enabled = false;
                yield return new WaitForSecondsRealtime(.4f);
                Assert.That(map.RunId, Is.Null, "Disabled connections must not appear online.");
                Assert.That(submit.interactable, Is.False, "Do not cancel or submit against stale state.");
                map.enabled = true;
                until = Time.realtimeSinceStartupAsDouble + 10;
                while ((!submit.interactable || map.RunId == null) && Time.realtimeSinceStartupAsDouble < until) yield return null;
                Assert.That(map.RunId, Is.EqualTo("mobile-fixture"));
                Assert.That(submit.GetComponentInChildren<Text>().text, Is.EqualTo("요청 취소"));
                Assert.That(server.PostCount, Is.EqualTo(1), "Reconnect must recover the request without resubmitting.");
                submit.onClick.Invoke();
                until = Time.realtimeSinceStartupAsDouble + 5;
                while (!status.text.Contains("요청 취소") && Time.realtimeSinceStartupAsDouble < until) yield return null;
                Assert.That(server.PostCount, Is.EqualTo(1), "The second action cancels instead of submitting another order.");
                Assert.That(server.CancelCount, Is.EqualTo(1));
                Assert.That(status.text, Does.Contain("요청 취소"));
            }
            finally
            {
                PlayerPrefs.SetString("InhaMobil.Passenger.Pending", savedPending); PlayerPrefs.Save();
                UnityEngine.Object.DestroyImmediate(root); UnityEngine.Object.DestroyImmediate(asset);
            }
        }

        [Serializable] private sealed class OrderData { public string command_id, owner_id, pickup_landmark_id, dropoff_landmark_id; }
        private sealed class PassengerServer : IDisposable
        {
            private readonly TcpListener listener = new TcpListener(IPAddress.Loopback, 0);
            private readonly Thread worker;
            private readonly string map, version;
            private volatile bool stopped;
            public volatile int PostCount;
            public volatile int CancelCount;
            public OrderData Order;
            public string Url { get; }
            public PassengerServer(string map, string version)
            {
                this.map = map; this.version = version; listener.Start();
                Url = "http://127.0.0.1:" + ((IPEndPoint)listener.LocalEndpoint).Port;
                worker = new Thread(Serve) { IsBackground = true }; worker.Start();
            }
            private string Request => Order == null ? "" : "{\"id\":\"mobile-request\",\"owner_id\":\"mobile-test\",\"status\":\"" + (CancelCount > 0 ? "CANCELLED" : "QUEUED") + "\"}";
            private void Serve()
            {
                try
                {
                    while (!stopped)
                    using (var client = listener.AcceptTcpClient())
                    using (var stream = client.GetStream())
                    using (var reader = new StreamReader(stream, Encoding.UTF8, false, 1024, true))
                    {
                        stream.ReadTimeout = 5000;
                        string line = reader.ReadLine(); if (line == null) continue;
                        int length = 0; string header;
                        while (!string.IsNullOrEmpty(header = reader.ReadLine()))
                            if (header.StartsWith("Content-Length:", StringComparison.OrdinalIgnoreCase)) length = int.Parse(header.Substring(15).Trim());
                        string response;
                        if (line.StartsWith("POST "))
                        {
                            var body = new char[length]; int read = 0;
                            while (read < length) { int count = reader.Read(body, read, length - read); if (count == 0) break; read += count; }
                            if (line.Contains("/cancel ")) Interlocked.Increment(ref CancelCount);
                            else { Order = JsonUtility.FromJson<OrderData>(new string(body)); Interlocked.Increment(ref PostCount); }
                            response = "{\"accepted\":true,\"request\":" + Request + "}";
                        }
                        else if (line.Contains("/unity-map ")) response = map;
                        else response = "{\"run_id\":\"mobile-fixture\",\"map_version\":\"" + version + "\",\"simulation_only\":true,\"vehicles\":[],\"queue\":[],\"requests\":[" + Request + "]}";
                        byte[] bytes = Encoding.UTF8.GetBytes(response);
                        byte[] head = Encoding.ASCII.GetBytes("HTTP/1.1 200 OK\r\nContent-Type: application/json\r\nConnection: close\r\nContent-Length: " + bytes.Length + "\r\n\r\n");
                        stream.Write(head, 0, head.Length); stream.Write(bytes, 0, bytes.Length);
                    }
                }
                catch (SocketException) { } catch (IOException) { } catch (ObjectDisposedException) { }
            }
            public void Dispose() { stopped = true; listener.Stop(); worker.Join(1000); }
        }
    }
}
