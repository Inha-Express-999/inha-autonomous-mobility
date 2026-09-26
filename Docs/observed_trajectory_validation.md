# Observed motion → timed footprint validation

2026-09-26 · project v0.3.2.0

`check_observed_trajectory` connects current sensor returns and tracked surface
motion to the continuous timed footprint checker. It uses neither other actors'
Transforms nor inferred positions behind occlusion.

## Time and error bounds

Unity capture timestamps and server receipt timestamps can have unrelated origins.
The adapter never subtracts them. It requires explicit `ObservationBounds`:
object full diameters, position/velocity error, maximum capture-to-receipt delay
in capture seconds, server freshness window, capture/server clock-rate ratio and
its error, plus provenance. These are caller-supplied assumptions, not measured
or inferred guarantees. Pause, acceleration of simulation time or any rate change
outside the supplied interval invalidates the assumptions and requires new bounds.
Automatic clock-rate/delay qualification remains pending.

For receipt age R, clock-rate interval [r-e, r+e], and maximum delay L:

- capture age lies in [R(r-e), R(r+e)+L];
- propagate the observed surface by the midpoint age;
- inflate its disc by position error + speed × half age interval + velocity error
  × upper age;
- convert nominal velocity to server-time units with r and add uncertainty growth
  `velocity_error × (r+e) + speed × e` per future server second;
- cap the shared prediction horizon at `(2 - oldest_upper_age)/(r+e)`.

This encloses all constant-velocity predictions in the configured timing interval.
The configured velocity-error envelope must hold throughout the prediction;
unbounded acceleration is not covered. Static object returns use full diameter
about the observed surface rather than treating it as an object center.

## Rejection and integration

Every sample must be valid, fresh and match vehicle/session/map/ego-pose tick.
Pedestrians require a current tracked identity whose time and nearest observed
surface match the current frame. Missing motion, unsupported vehicle/unknown
classes, malformed timing and unrepresentable predictions return an unknown
collision result, never a clear path. The oldest sensor caps the whole horizon.

`LocalCandidate.assess_current_observations` checks its input fingerprint first,
then evaluates the timed candidate against the service-retained frames. Changed
pose, sensors, route, mission or service epoch reject the candidate. This call is
synchronous and results cannot be retained as authority across future ticks.
RRT capture now optionally accepts explicit `dynamic_bounds`. It uses the same
conversion as this checker to build conservative geometric motion envelopes,
then assesses the resulting timed prefix. The original static-only entry mode
still rejects dynamic objects. Changed time, frames or motion estimates invalidate
dynamic candidates. Live controller arbitration remains to be implemented.

The result distinguishes collision, clear observed prefix and unknown. It never
grants execution. A clear prefix says nothing about unseen space or about the
unchecked remainder. Ray-to-area coverage and continuous actual Physics motion
validation remain independent requirements.

Tests cover different clock origins and rates, bounded delay causing immediate
contact, oldest-sensor horizon limits, current-surface association, missing motion,
context/freshness rejection, transformed static returns and service candidate
fingerprint invalidation. These are Python algorithm/service checks; no new Unity
execution or vehicle dynamics claim is made.
