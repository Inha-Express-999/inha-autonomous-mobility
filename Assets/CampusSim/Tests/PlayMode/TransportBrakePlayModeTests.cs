using System.Collections;
using System.Collections.Generic;
using InhaExpress.Client.Presentation;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace InhaExpress.Client.Tests
{
    public sealed partial class TransportBrakePlayModeTests
    {
        private readonly List<GameObject> objects = new List<GameObject>();

        [UnityTearDown]
        public IEnumerator Cleanup()
        {
            Time.timeScale = 1;
            foreach (var item in objects) if (item != null) Object.Destroy(item);
            objects.Clear();
            yield return null;
        }

        private TransportVehicleBrakeBridge Create(out Rigidbody body, out WheelCollider[] wheels)
        {
            var ground = GameObject.CreatePrimitive(PrimitiveType.Cube);
            objects.Add(ground);
            ground.transform.position = new Vector3(0, -0.5f, 0);
            ground.transform.localScale = new Vector3(30, 1, 30);
            var car = new GameObject("isolated-brake-test");
            objects.Add(car);
            car.SetActive(false);
            car.transform.position = new Vector3(0, 0.5f, 0);
            body = car.AddComponent<Rigidbody>();
            body.mass = 500;
            var box = car.AddComponent<BoxCollider>();
            box.size = new Vector3(1.5f, 0.3f, 2.5f);
            wheels = new WheelCollider[4];
            for (int i = 0; i < 4; i++)
            {
                var wheel = new GameObject("wheel-" + i);
                wheel.transform.SetParent(car.transform);
                wheel.transform.localPosition = new Vector3(i % 2 == 0 ? -0.7f : 0.7f, 0, i < 2 ? 1 : -1);
                wheels[i] = wheel.AddComponent<WheelCollider>();
                wheels[i].radius = 0.3f;
                wheels[i].suspensionDistance = 0.2f;
            }
            car.AddComponent<VehicleController>();
            var bridge = car.AddComponent<TransportVehicleBrakeBridge>();
            car.SetActive(true);
            return bridge;
        }

        private void Ready(TransportVehicleBrakeBridge b, string session = "s")
        {
            Assert.That(b.BeginSession("r", session, "test-map", 0), Is.True);
            Assert.That(b.ConfirmFullSnapshot("r", session, "test-map"), Is.True);
            Assert.That(b.ConfirmFreshObservations("r", session, "test-map"), Is.True);
        }

        [UnityTest]
        public IEnumerator WorkerDisconnectAppliesWheelBrakeAndStopsMovingBody()
        {
            var bridge = Create(out var body, out var wheels);
            yield return new WaitForSeconds(0.7f);
            Ready(bridge);
            Assert.That(bridge.ApplyValidatedCommand("r", "s", "test-map", 1, 0, 1, 3.6f, 0, false), Is.True);
            body.linearVelocity = Vector3.forward;
            var worker = new System.Threading.Thread(bridge.QueueDisconnected);
            worker.Start(); worker.Join();
            yield return new WaitForFixedUpdate();
            yield return new WaitForFixedUpdate();
            Assert.That(bridge.BrakeRequired, Is.True);
            foreach (var wheel in wheels)
            {
                Assert.That(wheel.motorTorque, Is.Zero);
                Assert.That(wheel.brakeTorque, Is.GreaterThan(0));
            }
            float until = Time.realtimeSinceStartup + 4;
            while (Mathf.Abs(Vector3.Dot(body.linearVelocity, Vector3.forward)) > 0.15f && Time.realtimeSinceStartup < until)
                yield return new WaitForFixedUpdate();
            Assert.That(Mathf.Abs(body.linearVelocity.z), Is.LessThanOrEqualTo(0.15f));
        }

        [UnityTest]
        public IEnumerator PythonProcessLossReachesWheelBrakesThroughRealWebSocket()
        {
            // Test-only socket adapter and synthetic readiness, NOT a runtime-v4 decoder.
            var bridge = Create(out var body, out var wheels);
            yield return new WaitForSeconds(0.7f);
            var start = new System.Diagnostics.ProcessStartInfo
            {
                FileName = System.Environment.GetEnvironmentVariable("CAMPUS_BRAKE_TEST_PYTHON") ?? "python",
                Arguments = "-u \"" + System.IO.Path.GetFullPath(System.IO.Path.Combine(
                    Application.dataPath, "../AgentScripts/brake_socket_fixture.py")) + "\"",
                UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true
            };
            using (var server = System.Diagnostics.Process.Start(start))
            using (var socket = new System.Net.WebSockets.ClientWebSocket())
            using (var timeout = new System.Threading.CancellationTokenSource(15000))
            {
                System.Threading.Tasks.Task receiver = null;
                try
                {
                    var port = server.StandardOutput.ReadLineAsync();
                    float deadline = Time.realtimeSinceStartup + 10;
                    while (!port.IsCompleted && Time.realtimeSinceStartup < deadline) yield return null;
                    Assert.That(port.IsCompleted, Is.True, "Python fixture failed to start");
                    Assert.That(int.TryParse(port.GetAwaiter().GetResult(), out int number), Is.True);
                    var connect = socket.ConnectAsync(new System.Uri("ws://127.0.0.1:" + number), timeout.Token);
                    while (!connect.IsCompleted && !timeout.IsCancellationRequested) yield return null;
                    Assert.That(connect.IsCompleted, Is.True);
                    connect.GetAwaiter().GetResult();
                    var bytes = new byte[128];
                    var hello = socket.ReceiveAsync(new System.ArraySegment<byte>(bytes), timeout.Token);
                    while (!hello.IsCompleted && !timeout.IsCancellationRequested) yield return null;
                    Assert.That(hello.IsCompleted, Is.True);
                    var message = hello.GetAwaiter().GetResult();
                    Assert.That(message.EndOfMessage, Is.True);
                    Assert.That(System.Text.Encoding.UTF8.GetString(bytes, 0, message.Count), Is.EqualTo("TRANSPORT_TEST_READY"));
                    Ready(bridge);
                    Assert.That(bridge.ApplyValidatedCommand("r", "s", "test-map", 1, 0, 10, 3.6f, 0, false), Is.True);
                    body.linearVelocity = Vector3.forward;
                    Assert.That(bridge.BrakeRequired, Is.False);
                    receiver = System.Threading.Tasks.Task.Run(async () =>
                    {
                        try { await socket.ReceiveAsync(new System.ArraySegment<byte>(bytes), timeout.Token); }
                        catch (System.Net.WebSockets.WebSocketException) { }
                        finally { bridge.QueueDisconnected(); }
                    });
                    // Kill ONLY this test-owned Python process; no graceful close frame.
                    server.Kill();
                    deadline = Time.realtimeSinceStartup + 3;
                    while (!receiver.IsCompleted && Time.realtimeSinceStartup < deadline) yield return null;
                    Assert.That(receiver.IsCompleted, Is.True, "Socket receive must observe process loss");
                    receiver.GetAwaiter().GetResult();
                    yield return new WaitForFixedUpdate();
                    yield return new WaitForFixedUpdate();
                    Assert.That(bridge.Reason, Is.EqualTo("SOCKET_DISCONNECTED"));
                    foreach (var wheel in wheels)
                    {
                        Assert.That(wheel.motorTorque, Is.Zero);
                        Assert.That(wheel.brakeTorque, Is.GreaterThan(0));
                    }
                    deadline = Time.realtimeSinceStartup + 4;
                    while (body.linearVelocity.magnitude > 0.15f && Time.realtimeSinceStartup < deadline)
                        yield return new WaitForFixedUpdate();
                    Assert.That(body.linearVelocity.magnitude, Is.LessThanOrEqualTo(0.15f));
                    Assert.That(bridge.ApplyValidatedCommand("r", "s", "test-map", 2, 0, 10, 3.6f, 0, false), Is.False);
                }
                finally
                {
                    timeout.Cancel();
                    socket.Abort();
                    if (!server.HasExited) { server.Kill(); server.WaitForExit(2000); }
                    if (receiver != null) { try { receiver.Wait(2000); } catch (System.AggregateException) { } }
                }
            }
        }

        [UnityTest]
        public IEnumerator SilenceWithPausedSimulationLatchesBrakeAndReconnectRequiresFreshState()
        {
            var bridge = Create(out _, out var wheels);
            yield return null;
            Ready(bridge);
            Assert.That(bridge.ApplyValidatedCommand("r", "s", "test-map", 1, 0, 10, 3.6f, 0, false), Is.True);
            Time.timeScale = 0;
            yield return new WaitForSecondsRealtime(0.3f);
            Assert.That(bridge.ApplyValidatedCommand("r", "s", "test-map", 1, 0, 10, 3.6f, 0, false), Is.False);
            yield return new WaitForSecondsRealtime(0.3f);
            Assert.That(bridge.BrakeRequired, Is.True);
            Assert.That(bridge.Reason, Is.EqualTo("COMMAND_SILENCE"));
            Assert.That(bridge.BeginSession("r", "s2", "test-map", 0), Is.True);
            Assert.That(bridge.ApplyValidatedCommand("r", "s2", "test-map", 1, 0, 10, 3.6f, 0, false), Is.False);
            Assert.That(bridge.ConfirmFullSnapshot("r", "s2", "test-map"), Is.True);
            Assert.That(bridge.ApplyValidatedCommand("r", "s2", "test-map", 1, 0, 10, 3.6f, 0, false), Is.False);
            Assert.That(bridge.ConfirmFreshObservations("r", "s2", "test-map"), Is.True);
            Assert.That(bridge.ApplyValidatedCommand("r", "s", "test-map", 99, 0, 10, 3.6f, 0, false), Is.False);
            Time.timeScale = 1;
            yield return new WaitForFixedUpdate();
            yield return new WaitForFixedUpdate();
            foreach (var wheel in wheels) Assert.That(wheel.brakeTorque, Is.GreaterThan(0));
        }
    }
}
