using System.Collections;
using InhaExpress.Client.Domain;
using InhaExpress.Client.Networking;
using InhaExpress.Client.Presentation;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace InhaExpress.Client.Tests
{
    public sealed partial class TransportBrakePlayModeTests
    {
        private ClientRuntimeHost LiveHost(string url = "ws://127.0.0.1:1/v1/client/ws")
        {
            var go = new GameObject("live-pc-host");
            objects.Add(go);
            var host = go.AddComponent<ClientRuntimeHost>();
            host.Initialize(ClientRole.PC_Operator,
                new WebSocketClientDataSource(url, ClientRole.PC_Operator, "0.4.2.2"));
            return host;
        }

        [UnityTest]
        public IEnumerator ActualV3HostServerLossReachesWheelBrakes()
        {
            var bridge = Create(out var body, out var wheels);
            yield return new WaitForSeconds(0.7f);
            var start = new System.Diagnostics.ProcessStartInfo
            {
                FileName = System.Environment.GetEnvironmentVariable("CAMPUS_BRAKE_TEST_PYTHON") ?? "python",
                Arguments = "-u \"" + System.IO.Path.GetFullPath(System.IO.Path.Combine(
                    Application.dataPath, "../AgentScripts/brake_runtime_fixture.py")) + "\"",
                UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true
            };
            using (var server = System.Diagnostics.Process.Start(start))
            {
                ClientRuntimeHost host = null;
                try
                {
                    var port = server.StandardOutput.ReadLineAsync();
                    float deadline = Time.realtimeSinceStartup + 8;
                    while (!port.IsCompleted && Time.realtimeSinceStartup < deadline) yield return null;
                    Assert.That(port.IsCompleted, Is.True);
                    Assert.That(int.TryParse(port.GetAwaiter().GetResult(), out int number), Is.True);
                    host = LiveHost("ws://127.0.0.1:" + number + "/v1/client/ws");
                    Assert.That(ClientVehicleSafetyBinding.BindScene(host, body.gameObject.scene), Is.EqualTo(1));
                    var relay = body.GetComponent<ClientConnectionBrakeRelay>();
                    Assert.That(relay.Runtime, Is.SameAs(host));
                    host.StartSource();
                    deadline = Time.realtimeSinceStartup + 10;
                    while ((host.ConnectionState != ConnectionState.Connected || host.Store.Current == null) &&
                        Time.realtimeSinceStartup < deadline) yield return null;
                    Assert.That(host.ConnectionState, Is.EqualTo(ConnectionState.Connected));
                    Assert.That(host.Store.Current, Is.Not.Null);
                    Assert.That(host.Store.IsStale(Time.realtimeSinceStartupAsDouble), Is.False);
                    yield return null;
                    // Synthetic drive/readiness only. The production source supplies connection/snapshot state.
                    Ready(bridge);
                    Assert.That(bridge.ApplyValidatedCommand("r", "s", "test-map", 1, 0, 10, 3.6f, 0, false), Is.True);
                    body.linearVelocity = Vector3.forward;
                    server.Kill();
                    long seq = 2;
                    deadline = Time.realtimeSinceStartup + 4;
                    while (!bridge.BrakeRequired && Time.realtimeSinceStartup < deadline)
                    {
                        // Keep watchdog fresh: only the real host/relay may revoke this test authority.
                        bridge.ApplyValidatedCommand("r", "s", "test-map", seq++, 0, 10, 3.6f, 0, false);
                        yield return null;
                    }
                    Assert.That(bridge.BrakeRequired, Is.True);
                    Assert.That(bridge.Reason, Is.EqualTo("SOCKET_DISCONNECTED"));
                    CollectionAssert.Contains(new[] { "TRANSPORT_DISCONNECTED", "SNAPSHOT_STALE", "CONNECTION_REPLACED" }, relay.LastStopReason);
                    yield return new WaitForFixedUpdate();
                    yield return new WaitForFixedUpdate();
                    foreach (var wheel in wheels)
                    {
                        Assert.That(wheel.motorTorque, Is.Zero);
                        Assert.That(wheel.brakeTorque, Is.GreaterThan(0));
                    }
                    deadline = Time.realtimeSinceStartup + 4;
                    while (body.linearVelocity.magnitude > 0.15f && Time.realtimeSinceStartup < deadline)
                        yield return new WaitForFixedUpdate();
                    Assert.That(body.linearVelocity.magnitude, Is.LessThanOrEqualTo(0.15f));
                    Assert.That(bridge.ApplyValidatedCommand("r", "s", "test-map", seq, 0, 10, 3.6f, 0, false), Is.False);
                }
                finally
                {
                    if (host != null) Object.Destroy(host.gameObject);
                    if (!server.HasExited) { server.Kill(); server.WaitForExit(2000); }
                }
            }
        }

        [UnityTest]
        public IEnumerator ServerRestartRestoresNewSnapshotButKeepsVehicleBraked()
        {
            var bridge = Create(out _, out var wheels);
            yield return new WaitForSeconds(0.7f);
            var start = new System.Diagnostics.ProcessStartInfo
            {
                FileName = System.Environment.GetEnvironmentVariable("CAMPUS_BRAKE_TEST_PYTHON") ?? "python",
                Arguments = "-u \"" + System.IO.Path.GetFullPath(System.IO.Path.Combine(
                    Application.dataPath, "../AgentScripts/brake_runtime_fixture.py")) + "\"",
                UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true
            };
            System.Diagnostics.Process server = null;
            ClientRuntimeHost host = null;
            try
            {
                server = System.Diagnostics.Process.Start(start);
                var portLine = server.StandardOutput.ReadLineAsync();
                float deadline = Time.realtimeSinceStartup + 8;
                while (!portLine.IsCompleted && Time.realtimeSinceStartup < deadline) yield return null;
                Assert.That(portLine.IsCompleted, Is.True);
                Assert.That(int.TryParse(portLine.GetAwaiter().GetResult(), out int port), Is.True);
                host = LiveHost("ws://127.0.0.1:" + port + "/v1/client/ws");
                ClientVehicleSafetyBinding.BindVehicle(host, bridge.GetComponent<VehicleController>());
                host.StartSource();
                deadline = Time.realtimeSinceStartup + 10;
                while (!host.HasCurrentConnectionSnapshot && Time.realtimeSinceStartup < deadline) yield return null;
                Assert.That(host.HasCurrentConnectionSnapshot, Is.True);
                yield return null;
                var oldSnapshot = host.Store.Current;
                int oldGeneration = host.ConnectionGeneration;
                Ready(bridge);
                Assert.That(bridge.ApplyValidatedCommand("r", "s", "test-map", 1, 0, 10, 3.6f, 0, false), Is.True);
                server.Kill();
                Assert.That(server.WaitForExit(2000), Is.True);
                server.Dispose(); server = null;
                deadline = Time.realtimeSinceStartup + 4;
                while (host.ConnectionGeneration == oldGeneration && Time.realtimeSinceStartup < deadline) yield return null;
                Assert.That(host.ConnectionGeneration, Is.GreaterThan(oldGeneration));
                Assert.That(host.HasCurrentConnectionSnapshot, Is.False, "Old full snapshot cannot satisfy a new connection");
                yield return null;
                Assert.That(bridge.BrakeRequired, Is.True);
                start.Arguments += " " + port;
                server = System.Diagnostics.Process.Start(start);
                var restartLine = server.StandardOutput.ReadLineAsync();
                deadline = Time.realtimeSinceStartup + 8;
                while (!restartLine.IsCompleted && Time.realtimeSinceStartup < deadline) yield return null;
                Assert.That(restartLine.IsCompleted, Is.True);
                Assert.That(restartLine.GetAwaiter().GetResult(), Is.EqualTo(port.ToString()));
                deadline = Time.realtimeSinceStartup + 12;
                while ((!host.HasCurrentConnectionSnapshot || host.Store.Current.RunId == oldSnapshot.RunId) &&
                    Time.realtimeSinceStartup < deadline) yield return null;
                Assert.That(host.HasCurrentConnectionSnapshot, Is.True);
                Assert.That(host.Store.Current.RunId, Is.Not.EqualTo(oldSnapshot.RunId));
                Assert.That(host.Store.IsStale(Time.realtimeSinceStartupAsDouble), Is.False);
                yield return null;
                yield return new WaitForFixedUpdate();
                yield return new WaitForFixedUpdate();
                Assert.That(bridge.BrakeRequired, Is.True, "Reconnect/full snapshot alone cannot resume motion");
                Assert.That(bridge.ApplyValidatedCommand("r", "s", "test-map", 999, 0, 10, 3.6f, 0, false), Is.False);
                Assert.That(host.Store.ApplySnapshot(oldSnapshot, Time.realtimeSinceStartupAsDouble),
                    Is.EqualTo(SnapshotApplyResult.IgnoredRetiredRun));
                foreach (var wheel in wheels)
                {
                    Assert.That(wheel.motorTorque, Is.Zero);
                    Assert.That(wheel.brakeTorque, Is.GreaterThan(0));
                }
            }
            finally
            {
                if (host != null) Object.Destroy(host.gameObject);
                if (server != null)
                {
                    if (!server.HasExited) { server.Kill(); server.WaitForExit(2000); }
                    server.Dispose();
                }
            }
        }

        [UnityTest]
        public IEnumerator InactiveVehicleBindsAndDisabledHostKeepsBrake()
        {
            var bridge = Create(out _, out _);
            var host = LiveHost();
            bridge.gameObject.SetActive(false);
            var relay = ClientVehicleSafetyBinding.BindVehicle(host, bridge.GetComponent<VehicleController>());
            Assert.That(relay.Runtime, Is.SameAs(host));
            bridge.gameObject.SetActive(true);
            host.enabled = false;
            yield return null;
            Assert.That(bridge.BrakeRequired, Is.True);
            Assert.That(relay.LastStopReason, Is.EqualTo("NO_LIVE_PC_TRANSPORT"));
            Assert.That(ClientVehicleSafetyBinding.BindVehicle(host, bridge.GetComponent<VehicleController>()), Is.SameAs(relay));
        }

        [UnityTest]
        public IEnumerator FixtureCannotBindPhysicalVehicle()
        {
            var bridge = Create(out _, out _);
            var go = new GameObject("fixture-host");
            objects.Add(go);
            var host = go.AddComponent<ClientRuntimeHost>();
            host.Initialize(ClientRole.PC_Operator, new FixtureClientDataSource(ClientRole.PC_Operator, "0.4.2.2"));
            Assert.Throws<System.InvalidOperationException>(() =>
                ClientVehicleSafetyBinding.BindVehicle(host, bridge.GetComponent<VehicleController>()));
            Assert.That(bridge.GetComponent<ClientConnectionBrakeRelay>(), Is.Null);
            yield return null;
        }

        [UnityTest]
        public IEnumerator CompetingSyntheticDriverIsRejected()
        {
            var bridge = Create(out _, out _);
            var host = LiveHost();
            bridge.gameObject.AddComponent<VehicleRouteFollower>();
            Assert.Throws<System.InvalidOperationException>(() =>
                ClientVehicleSafetyBinding.BindVehicle(host, bridge.GetComponent<VehicleController>()));
            Assert.That(bridge.GetComponent<ClientConnectionBrakeRelay>(), Is.Null);
            yield return null;
        }
    }
}
