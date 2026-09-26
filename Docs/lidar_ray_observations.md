# LiDAR ray observations — synthetic alpha

2026-09-26 · project v0.3.2.0

The previous hit-only payload could not distinguish an untested direction from
a ray that returned no collider. Unity now emits ordered per-ray samples for
LiDAR, with a positive `rayMaxRangeM` and up to 64 `rays`:

| Field | Meaning |
|---|---|
| bearingRad | Sensor-local forward=0, left-positive radians, strictly increasing |
| outcome=HIT | Nearest external ray intersection; rangeM matches one ordered detection |
| outcome=MISS | No external intersection for this ray up to rangeM=rayMaxRangeM |
| outcome=INVALID | Saturated buffer or unusable return; rangeM is null, frame valid=false |

Time and sensor capture pose must accompany ray samples. Radar's grouped returns
do not emit this LiDAR contract. Old hit-only frames are still accepted with an
empty ray list and no maximum range; they provide no ray coverage information.
The schema version remains 3 (optional fields). Updated clients require an updated
strict server, so deploy the pair together. No map version change is involved.
The WebSocket message limit is now 64 KiB (previously 16 KiB): a full 64-hit
frame with valid object IDs and ray samples can exceed the old bound. The cap
remains finite and oversized-message closure is retained. A separate Python
WebSocket test sends and verifies a full frame exceeding 16 KiB.

Unity scans actual Physics colliders with the rig's layer mask, ignores triggers
and its own actor hierarchy, and never enumerates hidden actor transforms. A
full ray hit buffer invalidates that ray instead of reporting a miss. Tilted
sensors and sensor origins overlapping external colliders invalidate the scan;
the latter avoids Unity raycasts missing a collider enclosing their origin.

**A MISS is not a free-space polygon.** Rays have no area. Space between rays,
behind the first return, above/below the 2D plane, excluded layers, and absent
colliders remains unverified. No interpolation or obstacle-size assumption is
used to label those spaces clear. RRT candidates remain non-executable. A future
coverage policy needs explicit assumptions, uncertainty and swept-body coverage,
plus observation-time alignment and safety arbitration before actual detours.

Python validates ordering, count, finite ranges, outcome consistency, LiDAR/capture
metadata, invalid-frame propagation and HIT/detection correspondence. This data is
retained by normal sensor ingress and included in local candidate fingerprints
through the full frame payload; any changed ray invalidates an old candidate.

Validation: Python/MapData 259 tests; isolated Unity PlayMode 4/4. The new tests
exercise real occlusion, saturation and origin overlap, and query the isolated
test server's retained production observations during the crossing scenario.
The diagnostic HTTP endpoint exists only in the loopback test harness.
