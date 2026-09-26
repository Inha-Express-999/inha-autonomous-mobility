# Fleet lifecycle and terminal mobile projection

2026-09-26, Unity 6000.3.21f1. EditMode 14/14; combined Physics/Python PlayMode 1/1.
Final PlayMode accepted 391 pose and 390 sensor ACKs, zero sensor rejections.
The test completes passenger and cargo missions, then destroys the vehicle actor
and requires recreation with increasing ticks and new accepted pose/sensor frames.
It also checks the mobile WorldStateStore accepts completion and keeps advancing
while the former vehicle serves cargo. No missing-reference reconnect warning was
found in the final run. An earlier run exposed the dangling terminal vehicle ID.

The CSV request status is the first (passenger) request; cargo completion is
asserted by request ID in the test, not inferred from that CSV column.
EditMode covers omitted idle actors and reappearance, prefab/route checks, and
host replacement. Source hashes identify each exact tested revision.

Unity emitted allocation-lifetime warnings and transient stale-sensor holds were
observed. Passing correctness does not prove timing, memory, safety or load targets.
Original scenes/Android/production Player and real map remain unverified here.
