# Vehicle asset and physics shell readiness

Project version: **0.3.1.0** · implementation status checked 2026-09-25

## Existing vehicle visuals

Unity Editor 6000.3.21f1 inspection found three visual prefabs under `Assets/CampusSim/Prefabs/`. Each source prefab had only `Transform`, `MeshFilter`, and `MeshRenderer`; none had a `Rigidbody` or collider. Their source root scales were 100 (Annyoung), 120 (Default), and 100 (Induck), and their source positions were campus-scene coordinates. These original assets are retained without modification.

## Generated actor prefab wrappers

`Assets/CampusSim/Prefabs/Vehicles/VehicleActor_*.prefab` wrap each original visual under a normalized unit-scale actor root. The source root position is reset to zero beneath a `Visual` child, while its imported orientation and scale are retained. The editor authoring command measures the instantiated renderer AABB and uses it as a root `BoxCollider` envelope. Unity Editor asset inspection verified all three root scales are `(1,1,1)`, visual local positions are zero, and each actor contains a `Rigidbody` and `BoxCollider`.

| Actor prefab | Measured renderer AABB in Unity X/Y/Z units (metre conversion is not independently validated) |
|---|---:|
| `VehicleActor_Annyoung` | 1.90 × 1.17 × 0.96 |
| `VehicleActor_Default` | 2.28 × 1.18 × 1.12 |
| `VehicleActor_Induck` | 1.90 × 1.21 × 0.88 |

These are Unity-renderer bounds under the current import transforms, not manufacturer dimensions or verified vehicle footprints. The box is a simple bounding envelope; its size/height/center have not been validated against wheel contact, body overhang, passenger clearance, or collision safety. Do not infer length/width from X/Z until each asset's forward axis is verified.

The wrappers deliberately have a **kinematic Rigidbody, gravity disabled, interpolation enabled, and Continuous Speculative collision mode**. Unity's default `Rigidbody.mass` is still 1 kg and is only a placeholder; it is not vehicle data. They are reusable physics shells only. They do not implement acceleration, braking, steering, wheel-ground contact, configured mass/center of gravity, collision response policy, or SensorRig. No dynamics or safety claim is implied.

## PC operator actor spawn and pose reporting

`PC_Operator.unity` retains an explicit V01→`VehicleActor_Default` binding and an empty required `mapVersion` guard. It does not display or move actors while the server uses the synthetic graph. The separate `RoadGraphSyntheticPreview.unity` contains the matching exact `synthetic-campus-6stop-v1` binding and a PC WebSocket bootstrap configured for `ws://127.0.0.1:8765/v1/client/ws`; it is the only scene where this follower can be enabled. Open that scene in Unity Editor and enter Play Mode after starting the local server. Its bootstrap uses the same scene as its already-loaded world and role scene, leaving the synthetic ground/graph visible while binding the actor spawner.

Start the server from the repository root in PowerShell:

```powershell
$env:PYTHONPATH = 'backend/src'
python -m campus_sim.cli serve
```

`VehicleActorSpawner` creates only explicitly configured vehicle IDs, attaches `VehicleEgoLocalizationReporter` and `VehicleRouteFollower`, and applies a 0.37 m height offset. This offset is estimated from the synthetic ground plane at -0.22 m and the actor collider's measured 1.18 m height; actual ground contact still requires PlayMode verification. The follower accepts only route polylines whose map version equals the synthetic snapshot, follows each waypoint with a kinematic Rigidbody, caps speed at 1.0 m/s, and brakes using a configured deceleration. A route update timeout of 0.5 s or missing/mismatched route makes it stop. To request a demo ride while Play Mode is running, post a synthetic request from another PowerShell window:

```powershell
Invoke-RestMethod -Method Post -Uri http://127.0.0.1:8765/v1/requests `
  -ContentType 'application/json' `
  -Body (@{
    command_id = 'preview-ride-001'
    owner_id = 'preview-passenger'
    service_type = 'PASSENGER'
    pickup_landmark_id = 'fixture_landmark_1'
    dropoff_landmark_id = 'fixture_landmark_2'
    party_size = 1
  } | ConvertTo-Json)
```

The request begins at V01's synthetic start node and targets the second fixture landmark. The follower is a low-speed visualization prototype only: it has no sensor observation, pedestrian/obstacle response, TTC/RRT, or collision safety. The 0.37 m spawn height is tailored to the synthetic preview ground and is not a campus-map offset. Forward-axis alignment, wheel contact, collider clearance, and Unity Player end-to-end execution have not been verified.

`VehicleActorSpawnerTests` adds EditMode coverage for coordinate/heading conversion, reporter/follower setup, route binding, avoiding duplicate actors, ignoring unconfigured vehicles, rejecting mismatched or non-synthetic map versions, and suppressing spawn in Fixture mode. `VehicleRouteFollowerPlayModeTests` adds Unity Physics movement-to-waypoint and stale-route stop checks. Presentation, EditMode, and PlayMode test assemblies compile with Unity 6000.3.21f1 Bee reference response files using the bundled Mono compiler. Unity Test Runner execution, preview scene reimport, and runtime PlayMode execution are still pending; standalone compiler source-generator compatibility warnings are expected and are not Unity Editor diagnostics.

## Rebuild and next integration work

In Edit Mode, use `InhaExpress > Vehicles > Create Physics Actor Prefabs`. The authoring script refuses to overwrite an existing actor prefab, measures each current source renderer again, and saves nested prefab instances so source visuals remain shared.

Next, run the preview scene through Unity Editor Play Mode against the local API and verify the full request→route→Rigidbody movement→localization→arrival→completion cycle. Check actor forward/up axes and the synthetic ground offset. Keep the root actor at unit scale and do not promote renderer bounds to an approved RoadGraph footprint without separate vehicle-clearance validation. Implement sensor-based braking and dynamic-object safety before enabling movement on any real campus map.

## 2026-09-26 original prefab EditMode gate

Unity 6000.3.21f1 passed 11/11 selected EditMode tests using the original three
vehicle wrappers and their resolved visual dependencies in an isolated project.
Tests cover unit roots, kinematic bodies, renderer/BoxCollider bound agreement,
missing scripts, route authority, and immutable full server route rendering.
ApplyRoute now resolves Rigidbody before Awake when necessary. Held-color checks
allow the LineRenderer's 8-bit channel precision. Evidence and source hashes:
`artifacts/validation/2026-09-26-prefab-editmode/`.

Run with `AgentScripts/RunUnityPrefabTests.ps1 -PythonPath <python-with-backend-dev>`.
This does not verify forward axes, ground contact, vehicle dynamics, original scene
Player behavior, Android, or rendering performance. Earlier Player/follower artifacts
remain evidence for their recorded source hashes; the subsequent Rigidbody lookup
and route display changes were checked by this EditMode run.
