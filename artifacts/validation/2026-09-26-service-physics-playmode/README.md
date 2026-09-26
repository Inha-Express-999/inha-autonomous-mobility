# Recorded PlayMode service integration

- Unity 6000.3.21f1, isolated PlayMode Physics, current production source copied by hash.
- One combined scenario passed: mobile passenger call, actual Raycast obstacle stop,
  clear hold/recovery, pickup/dropoff completion, PC cargo return completion and mobile
  request projection.
- Accepted pose ACKs: 329; accepted sensor ACKs: 328; rejected sensor ACKs: 0.
- This is a synthetic 3-Stop test shell, not the campus scene or a Windows Player run.
- `results.xml` contains the assertion outcome; `source-manifest.json` records source
  versions; `snapshot-trace.csv` records the PC stream. The CSV requestStatus column
  shows the first request, so it stays COMPLETED during the cargo return; the cargo
  request's own completion is verified by ID in the test.

Reproduce using `AgentScripts/RunUnityPhysicsIntegration.ps1`. See
`AgentScripts/UnityPhysicsIntegration/README.md` for scope and prerequisites.
