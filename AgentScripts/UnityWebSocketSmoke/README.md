# Unity WebSocket source smoke test

This Windows helper compiles the repository's client/domain C# files with the Mono compiler bundled with the project's pinned Unity Editor, then connects that client to a temporary local Python ASGI server over a real WebSocket.

The helper connects a PC_Operator client and a Mobile_Passenger client concurrently. The PC client sends ego-localization and one synthetic LiDAR observation, verifies both ACKs, creates passenger/cargo requests and cancels its cargo request. The server applies a mobile passenger request, deliberately drops that request's first ACK, and closes that socket. The mobile client must reconnect, resend the original message ID and payload, receive one accepted ACK, and observe exactly one request in its role-scoped snapshot. This checks source-level WebSocket serialization for both roles, sensor-observation serialization/ACK, operator cargo/cancel handling, reconnect backoff, pending-command replay and server idempotency together.

## Run

Install the backend development dependencies in a Python environment, then run PowerShell from the repository root:

```powershell
.\AgentScripts\UnityWebSocketSmoke\run.ps1 -PythonPath "C:\path\to\backend-venv\Scripts\python.exe"
```

The C# sensor DTO edge cases can be compiled and run without Python or a server:

```powershell
.\AgentScripts\UnityWebSocketSmoke\run.ps1 -DtoOnly
```

The helper uses a loopback-only port (`18766` by default); pass `-Port` to choose another unused port. It refuses to start if the selected port already has a listener. It creates a uniquely named temporary run directory under `%TEMP%` and prints its location for logs. The local server process is stopped when the script exits.

This smoke test compiles and runs the domain/network C# source with Unity's bundled Mono runtime, substituting only `UnityEngine.Debug.LogWarning`. It does not compile or run `VehicleRaycastSensorRig` inside Unity, and its LiDAR frame is a constructed fixture, not a Physics raycast. It does not verify that localization is collected from a Unity Transform/Physics actor, applied to server route/runtime state, Unity synchronization-context behavior, scenes, UI, Android, LAN access, or performance.
