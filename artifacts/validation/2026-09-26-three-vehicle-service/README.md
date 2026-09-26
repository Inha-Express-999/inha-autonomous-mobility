# Three-vehicle passenger/cargo service — 2026-09-26

Uncommitted work after v0.3.2.0. Separate synthetic eight-node graph with actual
Unity Physics V01/V02/V03 actors. Two passenger requests and a 5 kg cargo request
use normal service/dispatch, route planning, resource admission and actuator control.
Test request creation is a loopback setup endpoint, not mobile UI verification.

Normal command: `AgentScripts/RunUnityPhysicsIntegration.ps1 -PythonPath tmp/backend-venv/Scripts/python.exe -Reservations -FleetThree`.
Unity 6000.3.21f1 isolated PlayMode: **1/1 passed**. Corridor entries V02,V01,V03;
passenger V01/V02 and cargo V03 all completed. Three distinct request and vehicle
IDs. Sensor ACKs=3138, rejected=0. Every physical pair checked for sampled overlap;
no overlap or simultaneous corridor occupancy. All claims/closures/faults/pending
requests zero on completion.

V03 initially received a pre-entry lease but was held by sensor safety. It expired
before any resource entry. Other traffic then passed, V03 reacquired permission
and completed. Event audit verifies every grant follows release/expiry of the
prior group and no active grant remains. Entered occupancy is never cleared by
this pre-entry expiration mechanism.

Ruff and whitespace checks passed. Production Python algorithms did not change in
this step; the preceding 355-test Python suite is historical, not rerun here.
The normal run predates the later fault-injection predicate correction; each run
has exact source hashes. A first three-vehicle fault attempt did not reach its
central coordinate within 20 seconds, before fault injection. The predicate was
changed to require a moving body fully inside the corridor at any valid interior
pose; this preserves the internal-fault condition across approach directions.

No measured campus geometry, actual vehicle specifications, Player/Android,
original scene, universal deadlock freedom, CBS comparison or performance/load
claim. Frame-sampled collision checks are not a continuous collision proof.
Runtime sensor stale/clear holds may occur; no safety threshold was relaxed.

## Internal fault run

Command adds `-ReservationFault`: **1/1 passed**. Moving V03 was fully inside the
corridor when its ego reporter was disabled. Actual actuator/pose checks verify
command-expiry plus braking-distance bound and a further 22-second stationary
hold, exceeding the 20-second pre-entry TTL. No other body entered the corridor.
Claims=3, closures=3, tracker faults=1, pending vehicles=2; no lease completed or
was reassigned. Sensor ACKs=857, expected stale-localization rejections from V03=196,
other rejections=0. The two passenger requests remain in transit under the hold;
no cancellation, unload, actor deletion or recovery is fabricated.
