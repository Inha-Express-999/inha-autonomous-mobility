# Timed local candidates and dynamic sweep

2026-09-26 · project v0.3.1.0 · no live actuation

`LocalCandidate.timed_trajectory(MotionLimits(...))` converts RRT pose stages to
analytic stop-turn-translate timing. Limits and provenance are mandatory.
Translation uses triangular/trapezoidal speed with separately bounded acceleration
and braking; every stage begins and ends at rest. In-place yaw follows the shortest
arc at bounded angular speed. Sideways translation and simultaneous translation/
yaw are rejected. Angular acceleration and vehicle forces remain unmodeled.

`check_timed_prefix` checks moving disc bounds through each whole trajectory
interval. Its swept rectangle radius is inflated by `a_max * dt² / 8` to cover
accelerated center motion's deviation from a straight chord. Rotation uses the
existing continuous conservative yaw envelope. Position/velocity uncertainty
expands obstacle bounds; surface returns require bounds about the observed surface.

Prediction stops at two seconds from observation, including supplied age. Results
separately report checked duration, collision/unknown status and whether the whole
trajectory was checked. Expiry is unknown. A clear prefix does not validate an
unchecked tail or unseen space. Initial overlap and rotation-only contact count.

Validation: Python/MapData 233 tests and Ruff passed. Thirteen new tests verify
analytic duration/distance and acceleration/braking, triangular profiles, yaw and
endpoint rest, crossing timing, initial/rotating contact, uncertainty and expiry.
The service-ingress→RRT test also generates a timed candidate ending at rest.

Remaining: observation-age clock/latency validation, dynamic perception conversion,
visibility coverage, corridor/rejoin checks, live control arbitration, Python
command transport and Unity following. `LocalCandidate.executable` remains false.
Current Unity preview timing is independent; no new Physics/Player result is
claimed for these analytic trajectories.
