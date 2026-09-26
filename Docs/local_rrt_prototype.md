# Local RRT and continuous footprint geometry

2026-09-26 · project v0.3.1.0 · algorithm implementation, not live driving authority

`swept_geometry.py` checks a centered planar rectangular vehicle against a disc
over a whole motion interval. Both centers interpolate linearly on the same time
interval. Fixed-heading translation uses the rectangle/disc Minkowski sum: two
rectangular strips and four circular corners, with inclusive contact. It can
detect crossing between clear endpoints and avoids the false corner contacts of
simple square inflation.

Yaw follows the shortest arc. Each angular slab uses its midpoint orientation and
inflates the obstacle radius by `2 * footprint_radius * sin(slab_angle / 4)`.
This bounds displacement of every body point from the midpoint orientation over
the entire slab. Translation is still checked continuously inside each slab.
Thus the rotation result is conservative, not a point-sampled clearance claim.
Default angular resolution is 0.05 rad; a 1e-9 m contact tolerance is included.
Finite input/dimension/margin validation prevents malformed geometry being treated
as clear. This model excludes pitch/roll, curved center motion and unknown shapes.

`local_rrt.py` implements seeded sample→nearest→steer→checked attach→goal connect.
Defaults are 1,500 expansion attempts, 0.5 m steps and 0.1 goal bias. Every accepted
connection consists of a turn in place, straight translation and final heading
alignment, with all stages checked by the swept geometry. The explicit rectangular
corridor is shrunk by the circumscribed vehicle radius plus margin, conservatively
containing rotation and the entire straight center segment. This is currently a
convex rectangular corridor adapter, not a general campus corridor importer.

Results are immutable pose stages or `None`. A blocked start/goal, exhausted budget
or unavailable route never returns an unchecked straight fallback. Random state
is local to the supplied seed. All dimensions and obstacle bounds are supplied by
the caller; the planner queries no Unity actors.

Tests cover translational tunneling with stationary/moving obstacles, rounded
corners, rotation-only contact between clear orientations, yaw wrap, grazing,
deterministic obstacle detour, blocked corridor, invalid starts and invalid bounds.
`pytest backend/tests/test_local_rrt.py` passes 12 tests.

## Remaining integration

### Sensor-derived inputs and candidate revision guard

`local_candidate.py` now provides the application boundary from actual service
ingress to RRT inputs. It requires a stopped Unity-localized vehicle whose safety
state already withholds motion, a current map/session/pose, and fresh valid frames
from every active sensor. Missing capture poses, invalid/stale secondary sensors,
dynamic or unknown detections, or any explicit zone closure reject the input.

Static return coordinates are transformed using the reported sensor capture pose.
A caller must supply the maximum object diameter and its provenance: the full
diameter conservatively bounds the object around a surface return. Each return is
kept as a bound; no hidden object position or guessed scene geometry is queried.
An empty observation does not certify that the corridor is free.

The immutable input binds the server instance epoch, full ego and sensor payloads,
receipt times, route geometry/speed profile/ID, mission/request, safety state and
bounds. Its expiry is the earliest pose/sensor expiry. `inputs_still_current`
rejects clock rollback, elapsed expiry, new frames/poses, session replacement,
server replacement, route/profile or mission changes, renewed movement and zone
closure. Pure RRT computation accepts this copied input so it can run in a worker;
the guard must be checked after computation and again before consumption.

Every `LocalCandidate.executable` remains **false**. Freshness establishes that the
inputs have not changed; it does not establish visibility, dynamic trajectory
safety or authority to drive. No service route is mutated and no command is sent.
Tests verify actual ego/sensor ingress→world obstacle bounds→RRT candidate and all
the listed invalidation categories. This currently supports static-return
candidate planning only; dynamic observations cause rejection, not an unsafe
freeze-in-place assumption.

### Still required

- Convert fresh sensor observations to conservative obstacle bounds and prove
  visibility/coverage for the candidate corridor; static surface bounds now exist,
  but verified object limits and coverage are still missing.
- Supply verified corridor geometry and vehicle/sensor envelope provenance.
- Predict dynamic obstacles and assign trajectory timing, acceleration and turn
  limits; this planner's static obstacle snapshot is insufficient for execution.
- Candidate revision guards now exist; add route-rejoin validation and actual
  control arbitration that keeps safety/stop authority dominant on every tick.
- Connect Python local planning/control to Unity actuation and verify blocked-path
  stop→RRT detour→rejoin with actual Physics and measured braking.

The existing service crossing/range gates are unchanged. No live vehicle receives
these candidate paths yet; full RRT/T07–T09/T21/M4 acceptance remains incomplete.
