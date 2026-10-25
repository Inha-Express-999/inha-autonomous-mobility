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
                    CollectionAssert.Contains(new[] { "TRANSPORT_DISCONNECTED", "SNAPSHOT_STALE" }, relay.LastStopReason);
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
