using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using InhaExpress.Client.MapClient;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace InhaExpress.Client.Tests
{
    public sealed class ServerMapConnectionTests
    {
        [UnityTest]
        public IEnumerator ReadsWebMapAndSharedRunOverHttpAndKeepsAuthoredNodesWhenDisconnected()
        {
            string json = File.ReadAllText(Path.Combine(Application.dataPath, "CampusSim/Data/server_map.json"));
            var map = ServerMapDocument.Parse(json);
            using (var server = new ReadOnlyServer(json, map.map_version))
            {
                var root = new GameObject("Read-only map HTTP test");
                root.SetActive(false);
                var authored = new GameObject("Authored node");
                authored.transform.SetParent(root.transform);
                authored.transform.localPosition = new Vector3(12, 3, 42);
                var asset = new TextAsset(json);
                try
                {
                    var client = root.AddComponent<CampusServerMap>();
                    client.authoredMap = asset; client.geometryHash = map.geometry_hash;
                    client.mapVersion = map.map_version; client.serverUrl = server.Url;
                    client.pollSeconds = .5f; client.showStatus = false;
                    root.SetActive(true);
                    double until = Time.realtimeSinceStartupAsDouble + 15;
                    while (client.RunId != "http-fixture-run" && Time.realtimeSinceStartupAsDouble < until)
                        yield return null;
                    Assert.That(client.RunId, Is.EqualTo("http-fixture-run"), client.LastError);
                    Assert.That(client.SimulationTime, Is.EqualTo(42));
                    Assert.That(client.VehicleCount, Is.EqualTo(3));
                    Assert.That(client.ServerPaused, Is.True);
                    Assert.That(client.Completed, Is.EqualTo(2));
                    Assert.That(client.ServerProjectVersion, Is.EqualTo(map.project_version));
                    Assert.That(authored.transform.localPosition, Is.EqualTo(new Vector3(12, 3, 42)));
                    server.Stop();
                    until = Time.realtimeSinceStartupAsDouble + 10;
                    while (client.LastError == null && Time.realtimeSinceStartupAsDouble < until) yield return null;
                    Assert.That(client.LastError, Is.Not.Null);
                    Assert.That(client.ConnectionStatus, Does.StartWith("Offline"));
                    Assert.That(authored.transform.localPosition, Is.EqualTo(new Vector3(12, 3, 42)));
                    lock (server.Requests)
                    {
                        Assert.That(server.Requests, Does.Contain("GET /v1/demo/unity-map HTTP/1.1"));
                        Assert.That(server.Requests, Does.Contain("GET /v1/demo/state HTTP/1.1"));
                        Assert.That(server.Requests.TrueForAll(r => r.StartsWith("GET ")), Is.True);
                    }
                    root.SetActive(false);
                    yield return null;
                }
                finally { UnityEngine.Object.DestroyImmediate(root); UnityEngine.Object.DestroyImmediate(asset); }
            }
        }

        private sealed class ReadOnlyServer : IDisposable
        {
            private readonly TcpListener listener;
            private readonly Thread worker;
            private readonly string map, state;
            private volatile bool stopped;
            public readonly List<string> Requests = new List<string>();
            public string Url { get; }

            public ReadOnlyServer(string json, string version)
            {
                map = json;
                state = "{\"run_id\":\"http-fixture-run\",\"time_s\":42,\"paused\":true,\"completed\":2," +
                    "\"simulation_only\":true,\"map_version\":\"" + version + "\",\"vehicles\":[{\"id\":\"V01\"},{\"id\":\"V02\"},{\"id\":\"V03\"}]}";
                listener = new TcpListener(IPAddress.Loopback, 0); listener.Start();
                Url = "http://127.0.0.1:" + ((IPEndPoint)listener.LocalEndpoint).Port;
                worker = new Thread(Serve) { IsBackground = true }; worker.Start();
            }

            private void Serve()
            {
                try
                {
                    while (!stopped)
                        using (var client = listener.AcceptTcpClient())
                        using (var stream = client.GetStream())
                        using (var reader = new StreamReader(stream, Encoding.ASCII, false, 1024, true))
                        {
                            stream.ReadTimeout = 5000;
                            var request = reader.ReadLine();
                            if (request == null) continue;
                            lock (Requests) Requests.Add(request);
                            string header;
                            do { header = reader.ReadLine(); } while (!string.IsNullOrEmpty(header));
                            byte[] body = Encoding.UTF8.GetBytes(request.Contains("/unity-map ") ? map : state);
                            byte[] response = Encoding.ASCII.GetBytes("HTTP/1.1 200 OK\r\nContent-Type: application/json\r\nConnection: close\r\nContent-Length: " + body.Length + "\r\n\r\n");
                            stream.Write(response, 0, response.Length); stream.Write(body, 0, body.Length);
                        }
                }
                catch (SocketException) { }
                catch (IOException) { }
                catch (ObjectDisposedException) { }
            }

            public void Stop() { stopped = true; listener.Stop(); worker.Join(1000); }
            public void Dispose() => Stop();
        }
    }
}
