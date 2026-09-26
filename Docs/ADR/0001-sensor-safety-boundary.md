# ADR 0001: Separate sensor safety decisions from service orchestration

Date: 2026-09-26 · status: implemented in working tree based on 0.2.3.0

## Context

`MobilityService` owns requests, dispatch, routes, localization, sensor ingress,
and clock-driven state transitions. Embedding safety policy and the sensor gate
inside that class makes it difficult to inspect safety independently or extend
it with TTC and local planning without affecting request handling.

## Decision

- `campus_sim.safety` owns `SafetyPolicy`, `SensorSample`, `SafetyDecision`, and
  the pure `evaluate_sensor_safety` function. It consumes validated sensor DTOs,
  receipt times, the expected ego pose tick, speed, and explicit recovery state.
- The service retains ownership of vehicle/session selection, ingress validation,
  bounded sensor storage, simulation time, and applying the returned decision.
- The evaluator never changes requests, observations, routes, or clocks and never
  receives dynamic Ground Truth transforms. The existing `service.SafetyPolicy`
  import remains available for callers through its module import.
- All streams are checked for expiry and validity before handling a temporary
  pose/frame tick mismatch. Otherwise insertion order could hide an invalid or
  expired stream and incorrectly preserve an already satisfied clear interval.
- A fresh pose/frame handoff still withholds authority while preserving the clear
  interval; resetting it on every 10 Hz pose would prevent recovery. Expired,
  missing, invalid, future-dated receipt timestamps and hazards reset recovery.

## Consequences and limits

No WebSocket schema or map version change is required. Policy numeric defaults
and the forward-sector stopping-distance model are unchanged. This extraction
does not implement TTC, footprint association, Radar tracking, RRT, or dynamic
vehicle braking. Those remain MVP requirements, with separate verification.

## Verification

The existing backend/MapData suite passed 102 tests before extraction. The new
suite passed 111 tests, including stream-order independence, future timestamps,
handoff authority, obstacle stop, and full recovery hold. The C# WebSocket smoke
passed against the modified service; it does not execute Unity Physics.
