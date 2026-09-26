# ADR 0002: Explicit vehicle telemetry ownership

2026-09-26 · working tree after v0.2.4.0

## Decision

The vehicle spawner passes its initialized PC ClientRuntimeHost to the ego
localization reporter when creating an actor. The sensor rig uses that reporter's
owner, so pose ticks and sensor frames share the same session and map context.
Neither component discovers arbitrary scene hosts. An unbound actor sends nothing;
a destroyed owner never causes fallback to another connection.

Rebinding the spawner to a different host retires its actors even when both hosts
report the same run ID. Same-host rebinding preserves actors. Mobile and uninitialized
hosts are rejected by reporter configuration. A configured prefab alone is no longer
sufficient to transmit telemetry: custom spawning code must pass the PC owner.
No wire contract or schema change is required.

## Evidence and limits

The mixed-role PlayMode integration creates a mobile host and PC host in one scene,
then completes the real Python/Raycast/Physics passenger and cargo flow. It accepted
352 localization frames and 351 sensor frames with zero sensor rejections.
EditMode tests cover host replacement and invalid-role binding alongside the
original prefab/route checks. Evidence lives in
`artifacts/validation/2026-09-26-telemetry-ownership/`.

This establishes local component ownership, not server authentication or session
isolation security. Original scene, Android, multi-vehicle conflict handling and
production deployment remain separate acceptance gates. The updated mixed-host
scenario has not been rerun in a standalone Player.

## Fleet lifecycle follow-up

The PC snapshot is a complete fleet. Omitted or externally destroyed actors are
retired before processing the remaining fleet; retirement immediately deactivates
colliders and telemetry before Unity's deferred destruction. A reappearing vehicle
gets a new actor. Per-vehicle ego and per-vehicle/sensor sequence counters now belong
to ClientRuntimeHost, so rebuilding an actor does not restart a live stream at zero.
The latest observed tick still remains -1 until that actor successfully sends.

The integration test now destroys and rebuilds the completed vehicle and requires
continued accepted ego/sensor ACKs with increasing stream ticks. EditMode also
checks an omitted idle vehicle is removed and can reappear exactly once.
