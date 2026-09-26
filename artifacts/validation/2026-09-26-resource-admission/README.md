# Resource admission checkpoint — 2026-09-26

Project v0.3.2.0. Python/MapData: 349 passed, one existing Starlette/httpx
warning. Ruff and git diff whitespace checks passed.

Command: `AgentScripts/RunUnityPhysicsIntegration.ps1 -PythonPath tmp/backend-venv/Scripts/python.exe -Crossing -PythonControl`.
Unity 6000.3.21f1 isolated PlayMode: **3 passed, 1 failed**. The main service
scenario held the actual 0.4 m vehicle body outside the corridor at x=0.588 m,
but timed out waiting for renewed movement after the explicit exit release.
The trace ends near x=0.592 m. The cause of the remaining hold has not been
established. Do not interpret this as successful admission/restart integration.
The three standalone ray checks passed. Source hashes bind these exact results.

An earlier attempt also failed: the gate prevented at-rest heading alignment.
That issue was corrected and a Python regression test added before this run.

Only V01 is an actual Physics vehicle; the occupied exit is an abstract fixture.
No Player, Android, original scene or multi-vehicle Physics verification was run
for this checkpoint. Earlier successful occupancy-only results remain historical.
