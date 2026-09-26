# Raycast Radar abstraction verification

2026-09-26, Unity 6000.3.21f1. EditMode 15/15; Radar Physics/Python PlayMode 1/1.
Run with AgentScripts/RunUnityPhysicsIntegration.ps1 -Radar -PythonPath <python>.
XML records RADAR mode and accepted pose/sensor ACK counts. The combined test
checks passenger/cargo completion, range-gate obstacle stop/clear recovery, mobile
store continuity and actor recreation. EditMode additionally checks range-rate
estimation from actual moving collider hits, merged returns and lost/stale history.
No hidden actor velocities or transforms are sent to the Python planner.

This is observed surface range change, not Doppler or crossing velocity. It does
not complete TTC integration. Default production prefab mode is unchanged LiDAR.
No Radar standalone Player run, original scene validation or performance claim.
Unity allocation-lifetime warnings remain. CSV status is the first passenger
request; cargo completion is tested separately by ID. Source hashes preserve scope.
