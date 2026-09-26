# Shared command sequence lifecycle

Unity 6000.3.21f1 · 2026-09-26 · isolated PlayMode.

- `RunUnityFollowerTests.ps1`: 12/12 passed, including physical command expiry and
  replay rejection after destroying/recreating an actuator with shared history.
- `RunUnityPhysicsIntegration.ps1 -PythonPath <repo>/tmp/backend-venv/Scripts/python.exe -Crossing -PythonControl`:
  1/1 passed. Actual server steering, crossing stop/recovery/passenger completion,
  followed by production actor recreation and command sequence continuation.

XML includes accepted sensor counts and old/new sequence values. Source manifests
bind evidence to each execution. Full socket disconnect/server restart behavior,
moving-actor recreation, original scenes, Player/Android and load are not verified
by these tests. Existing Unity allocation diagnostics remain.
