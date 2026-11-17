# Live recovery check — 2026-11-17

PASS: same request restored after user-operated server stop/restart, then completed once in Unity.

- Request: req-00000001-39fa3453891f; passenger 1; anniversary_60 to biryong_plaza; V02.
- Before stop: ASSIGNED / TO_PICKUP, approximately 18 km/h.
- User stopped CMD server with Ctrl+C. Dashboard observed 0/3, reconnect wait. Read-only DB inspection confirmed same request/job persisted.
- Agent-hosted restart attempts were unreachable and stopped; user restarted scripts/start_recovery_server.cmd with the same tmp/physical-service.db.
- Run changed from 207ee343414b4565ac917d65a3cbd672 to 7c20ffce4545484684e69d7507b028d4.
- Same request/V02 restored; fresh observations; new pickup mission dc00c1ffbc6344ecb4ec850f2685834d replaced bccfa8db251e45cdad6b6c207d72bbf5; approximately 18 km/h resumed.
- Dashboard observed 3/3 and request 1 restored.
- Request progressed IN_TRANSIT -> DROPOFF_SERVICE -> COMPLETED; completed count exactly 1.
- Vehicle began its return maneuver; full home return was not checked in this run. Unity and user-owned server remain running.

Evidence: before.json, request.json, pre-stop-latest.json, restarted.json, recovery-checks.json, timeline.jsonl, completed.json.
Limits: single vehicle/request/route; user-operated Ctrl+C/restart, not automatic restart or abrupt kill. No precise braking time/distance was measured. Screenshot observations are described, not saved as image artifacts.
