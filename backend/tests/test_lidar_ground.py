"""Protocol 6 geometry-only ground segmentation on synthetic range images.

The synthetic ray caster below is a test fixture, not a model of measured
campus terrain. It mirrors the PC sensor layout (360 x 7, -30..+15 deg, 15 m).
"""
import math

import pytest
from fastapi.testclient import TestClient
from jsonschema import Draft202012Validator
from pydantic import ValidationError

from campus_sim.api import create_app
from campus_sim.lidar_ground import segment_scan
from campus_sim.physical_lidar import evaluate_lidar
from campus_sim.unity_drive import AutoMission, DriveCommand, EgoObservation, LidarScan
from campus_sim.web_demo import DemoSession

ELEVATIONS = [-30 + 7.5 * i for i in range(7)]
SENSOR_UP = 1.8


def flat(lateral, forward):
    return 0.


def ramp(start_m, grade):
    return lambda lateral, forward: max(0., forward - start_m) * grade


def incline(grade):
    """Constant grade through the ego contact point (vehicle already on the slope)."""
    return lambda lateral, forward: forward * grade


def box(lat0, lat1, fwd0, fwd1, height, bottom=0.):
    return lat0, lat1, fwd0, fwd1, bottom, bottom + height


def standing(terrain, lat0, lat1, fwd0, fwd1, height):
    """Box resting on the terrain at its centre."""
    return box(lat0, lat1, fwd0, fwd1, height, terrain((lat0 + lat1) / 2, (fwd0 + fwd1) / 2))


def cast(direction, terrain, boxes, max_range):
    dx, dy, dz = direction
    nearest = None
    for lat0, lat1, fwd0, fwd1, bottom, top in boxes:
        t0, t1 = 0., max_range
        for origin, d, low, high in ((0., dx, lat0, lat1), (0., dy, fwd0, fwd1), (SENSOR_UP, dz, bottom, top)):
            if abs(d) < 1e-12:
                if not low <= origin <= high:
                    t0, t1 = 1., 0.
                continue
            a, b = (low - origin) / d, (high - origin) / d
            t0, t1 = max(t0, min(a, b)), min(t1, max(a, b))
        if t0 <= t1 and t0 > 0 and (nearest is None or t0 < nearest):
            nearest = t0
    limit = nearest if nearest is not None else max_range
    t, step = 0., .05
    below = lambda s: SENSOR_UP + s * dz <= terrain(s * dx, s * dy)
    while t + step <= limit:
        if below(t + step):
            low, high = t, t + step
            for _ in range(25):
                middle = (low + high) / 2
                low, high = (low, middle) if below(middle) else (middle, high)
            return high
        t += step
    return nearest


def scan(terrain=flat, boxes=(), tick=1, grade_forward=0., max_range=15., **changes):
    ranges = []
    for elevation in ELEVATIONS:
        e = math.radians(elevation)
        for column in range(360):
            a = math.radians(-180 + column)
            r = cast((math.cos(e) * math.sin(a), math.cos(e) * math.cos(a), math.sin(e)), terrain, boxes, max_range)
            ranges.append(0 if r is None else round(r * 1000))
    value = dict(tick=tick, valid=True, max_range_m=max_range, columns=360, azimuth_start_deg=-180,
                 azimuth_span_deg=360, channel_elevations_deg=ELEVATIONS, sensor_lateral_m=0.,
                 sensor_forward_m=0., sensor_up_m=SENSOR_UP, ground_up_m=0., ground_grade_forward=grade_forward,
                 ground_grade_lateral=0., ranges_mm=ranges)
    value.update(changes)
    return value


def ahead(segmentation, max_lateral=1.5):
    return [p for p in segmentation.obstacles if p.forward_m > 0 and abs(p.lateral_m) <= max_lateral]


@pytest.mark.parametrize("terrain,grade", [
    (flat, 0.), (ramp(4., .12), 0.), (incline(.15), .15), (incline(-.15), -.15), (ramp(3., -.1), 0.),
    (lambda lat, fwd: .1 if 5. <= fwd <= 5.6 else 0., 0.),          # 10 cm drivable bump
    (lambda lat, fwd: .03 * abs(lat), 0.),                         # 3 % road crown / cross slope
])
def test_drivable_ground_and_gentle_hills_are_not_obstacles(terrain, grade):
    segmentation = segment_scan(LidarScan(**scan(terrain, grade_forward=grade)))
    assert segmentation.return_count > 0
    assert segmentation.obstacles == ()


def test_measured_contact_grade_is_needed_when_ego_already_climbs_a_steep_grade():
    unseeded = segment_scan(LidarScan(**scan(incline(.25), grade_forward=0.)))
    seeded = segment_scan(LidarScan(**scan(incline(.25), grade_forward=.25)))
    assert ahead(unseeded) and not ahead(seeded)


@pytest.mark.parametrize("extent", [
    (-.3, .3, 6., 6.4, 1.2),        # crate in lane
    (-4., 4., 8., 8.3, 2.5),        # wall across road
    # Wide 1 m wall: only the column walk sees its centre. With 7 rings 7.5 deg
    # apart it is seen from ~9.6 m on flat but only ~7.6 m on a 12 % hill,
    # where the -7.5 deg ring meets its foot and the 0 deg ring passes over it.
    (-4., 4., 7., 7.3, 1.),
    (-.25, .25, 9., 9.4, 1.7),      # pedestrian-sized column
    (-.25, .25, 12., 12.4, 1.7),    # far pedestrian seen by one ring: needs the edge cue
    (-.3, .3, -4.4, -4., 1.),       # rear return
])
@pytest.mark.parametrize("terrain", [flat, ramp(4., .12)])
def test_vertical_structure_on_hills_is_still_an_obstacle(extent, terrain):
    obstacle = standing(terrain, *extent)
    points = segment_scan(LidarScan(**scan(terrain, (obstacle,)))).obstacles
    lat0, lat1, fwd0, fwd1, _, _ = obstacle
    lateral = min(1.2, max(abs(lat0), abs(lat1)))
    inside = [p for p in points if abs(p.lateral_m) <= lateral + .1 and fwd0 - .1 <= p.forward_m <= fwd1 + .1]
    assert inside
    assert all(p.entity_class == "unknown" for p in points)


def test_overhead_structure_above_vehicle_clearance_is_ignored():
    beam = box(-5., 5., 5., 6., .4, 3.)
    assert segment_scan(LidarScan(**scan(boxes=(beam,)))).obstacles == ()


@pytest.mark.parametrize("changes", [
    dict(ranges_mm=[0] * 2519), dict(columns=361), dict(channel_elevations_deg=[0, -7.5]),
    dict(ranges_mm=[16000] * 2520), dict(ground_up_m=2.), dict(azimuth_span_deg=180),
    dict(entity_class="pedestrian"), dict(hits=[]),
])
def test_incomplete_or_labelled_range_image_is_rejected(changes):
    value = {**scan(), **changes}
    if "channel_elevations_deg" in changes:
        value["ranges_mm"] = [0] * 720
    with pytest.raises(ValidationError):
        LidarScan(**value)


def observed(session, tick=1, lidar=..., **changes):
    value = dict(schema_version=6, run_id=session.run_id, geometry_hash=session.physical.geometry_hash,
                 tick=tick, x=70.07, z=-159.69, heading_rad=math.radians(-60), speed_mps=0,
                 wheelbase_m=1.3, max_steering_rad=.52, half_width_m=.46, rear_axle_offset_m=-.65,
                 half_length_m=1.55, sensor_valid=True, sensor_age_s=0, obstacle_detection_enabled=True,
                 rays=[], lidar=scan(tick=tick) if lidar is ... else lidar)
    value.update(changes)
    return EgoObservation(**value)


FLAT = scan()
HILL = scan(ramp(4., .12))
BLOCKED = scan(boxes=(box(-.3, .3, 3., 3.4, 1.2),))


def with_tick(value, tick):
    return {**value, "tick": tick}


def drive_session():
    session = DemoSession()
    token = session.physical.connect("V01")
    session.physical.observation("V01", token, observed(session))
    session.physical.mission("V01", AutoMission(run_id=session.run_id, command_id="lidar6",
        stop_id="main_building_road_candidate", driving_profile="campus_lidar", cruise_speed_kmh=18))
    return session, token


def test_protocol6_hill_keeps_driving_without_hesitation():
    s, t = drive_session()
    for tick in range(2, 12):
        result = s.physical.observation("V01", t, observed(s, tick, with_tick(HILL, tick), speed_mps=3))
        assert not result["emergency_stop"], result["reason"]
        assert result["schema_version"] == 6 and result["target_speed_kmh"] > 0


def test_protocol6_obstacle_brakes_and_resumes_after_five_clear_scans():
    s, t = drive_session()
    result = s.physical.observation("V01", t, observed(s, 2, with_tick(BLOCKED, 2), speed_mps=3))
    assert result["emergency_stop"] and "LiDAR" in result["reason"]
    for tick in range(3, 7):
        assert s.physical.observation("V01", t, observed(s, tick, with_tick(FLAT, tick)))["emergency_stop"]
    assert not s.physical.observation("V01", t, observed(s, 7, with_tick(FLAT, 7)))["emergency_stop"]


def test_protocol6_rrt_preview_never_trusts_unlabelled_returns():
    s, _ = drive_session()
    ego = observed(s, 2, with_tick(BLOCKED, 2))
    assert evaluate_lidar("V01", s.physical.vehicles["V01"], ego, 10.).stop


@pytest.mark.parametrize("changes", [
    dict(lidar=None), dict(rays=[dict(range_m=1, bearing_rad=0)]), dict(obstacle_detection_enabled=False),
    dict(lidar={**FLAT, "tick": 9}),
    dict(lidar=dict(tick=1, ray_count=2520, max_range_m=15, valid=True, hits=[])),
])
def test_protocol6_requires_same_tick_raw_range_image(changes):
    with pytest.raises(ValidationError):
        observed(DemoSession(), **changes)


def test_protocol5_frame_cannot_claim_protocol6_and_vice_versa():
    session = DemoSession()
    with pytest.raises(ValidationError):
        observed(session, schema_version=5)


def test_protocol6_schema_command_and_websocket():
    obs = observed(DemoSession())
    Draft202012Validator(EgoObservation.model_json_schema()).validate(obs.model_dump())
    DriveCommand(schema_version=6, run_id="run", tick=1, target_speed_kmh=-2, steering_angle_rad=0,
                 emergency_stop=False, valid_for_s=.3, status="MANEUVER", reason="observed reverse")
    with TestClient(create_app()) as client:
        state = client.get("/v1/demo/state").json()
        data = client.get("/v1/demo/unity-map").json()
        with client.websocket_connect("/v1/demo/unity/vehicles/V01/control") as ws:
            payload = obs.model_dump()
            payload.update(run_id=state["run_id"], geometry_hash=data["geometry_hash"])
            ws.send_json(payload)
            assert DriveCommand(**ws.receive_json()).schema_version == 6
