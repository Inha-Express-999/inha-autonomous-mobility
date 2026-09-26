import math
from dataclasses import replace

import pytest

from campus_sim.domain import EgoLocalization, MapPosition, SensorObservation
from campus_sim.local_candidate import (
    capture_local_input,
    compute_local_candidate,
    inputs_still_current,
    motion_sweep_discs,
)
from campus_sim.local_rrt import RectCorridor
from campus_sim.observed_trajectory import ObservationBounds
from campus_sim.service import MobilityService
from campus_sim.swept_geometry import BoxFootprint, Pose2
from campus_sim.timed_collision import MovingDiscBound
from campus_sim.trajectory import MotionLimits

BOUNDS = ObservationBounds(0.3, 0.6, 0.05, 0.05, 0.1, 0.3, 1, 0,
                           "Synthetic object, error, latency and equal-rate clock bounds")
LIMITS = MotionLimits(1, 0.5, 2, 1, "Synthetic controller limits")


def observed_service(*, second_frame=True, kind="PEDESTRIAN", with_static=False):
    service = MobilityService.synthetic_fixture()
    service.record_ego_localization(EgoLocalization(
        vehicle_id="V01", session_id="dynamic-plan", observed_tick=1,
        map_version=service.graph.map_version, position=MapPosition(x=0, y=0, z=0),
        heading_rad=math.pi / 2, speed_mps=0,
    ))
    for tick in range(1, 3 if second_frame else 2):
        x, y = 3, 0.6 - tick * 0.05
        hits = [{"rangeM": math.hypot(x, y), "bearingRad": math.atan2(y, x),
                 "localPositionM": {"x": x, "y": y, "z": 0}, "entityClass": kind,
                 "entityId": "visible-pedestrian"}]
        if with_static:
            hits.append({"rangeM": math.hypot(4, -2), "bearingRad": math.atan2(-2, 4),
                         "localPositionM": {"x": 4, "y": -2, "z": 0},
                         "entityClass": "STATIC_OBSTACLE"})
        service.record_sensor_observation(SensorObservation.model_validate({
            "vehicleId": "V01", "sensorId": "lidar", "sensorType": "LIDAR_2D",
            "sessionId": "dynamic-plan", "mapVersion": service.graph.map_version,
            "observedTick": tick, "egoPoseTick": 1, "valid": True,
            "observedTimeS": 100 + tick * 0.1,
            "sensorPositionM": {"x": 0, "y": 0, "z": 0}, "sensorHeadingRad": math.pi / 2,
            "detections": hits,
        }))
    return service


def capture(service, policy=BOUNDS):
    return capture_local_input(service, "V01", object_diameter_bound_m=0.3,
                               bound_provenance="Synthetic static diameter", dynamic_bounds=policy)


def candidate(service):
    inputs = capture(service)
    assert inputs is not None
    return compute_local_candidate(inputs, Pose2(6, 0, math.pi / 2),
                                    footprint=BoxFootprint(0.4, 0.4),
                                    corridor=RectCorridor(-1, -3, 7, 3),
                                    corridor_provenance="Synthetic test corridor", seed=17)


def test_real_ingress_tracks_pedestrian_builds_rrt_and_checks_timed_prefix():
    service = observed_service(with_static=True)
    planned = candidate(service)
    assert planned is not None
    assert any(abs(p.y) > 1 for p in planned.poses)
    assert 0 < planned.inputs.prediction_horizon_s < 2
    assert inputs_still_current(service, planned.inputs)
    assessed = planned.assess_current_observations(service, LIMITS, BOUNDS)
    assert assessed.sweep.collision is False
    assert not assessed.sweep.full_trajectory_checked
    assert not assessed.executable and not planned.executable
    assert service.vehicle_runtime["V01"].route_id is None


def test_no_path_when_swept_pedestrian_envelope_blocks_narrow_corridor():
    planned = compute_local_candidate(capture(observed_service()), Pose2(6, 0, math.pi / 2),
                                      footprint=BoxFootprint(0.4, 0.4),
                                      corridor=RectCorridor(-1, -0.7, 7, 0.7),
                                      corridor_provenance="Synthetic narrow corridor", seed=17)
    assert planned is None


@pytest.mark.parametrize("kwargs", [{"second_frame": False}, {"kind": "UNKNOWN"}, {"kind": "VEHICLE"}])
def test_missing_or_unsupported_motion_does_not_create_frozen_obstacle_plan(kwargs):
    assert capture(observed_service(**kwargs)) is None


def test_static_only_api_keeps_rejecting_dynamic_observations_without_explicit_bounds():
    assert capture_local_input(observed_service(), "V01", object_diameter_bound_m=0.3,
                               bound_provenance="Synthetic static only") is None


def test_candidate_rejected_on_time_tick_motion_change_or_weaker_policy():
    service = observed_service()
    planned = candidate(service)
    assert planned is not None
    assert planned.assess_current_observations(service, LIMITS,
                                              replace(BOUNDS, pedestrian_diameter_m=0.1)) is None
    service.simulation_time_s = 0.01
    assert not inputs_still_current(service, planned.inputs)
    service.simulation_time_s = 0
    service.sensor_motion_estimates[("V01", "lidar")] = ()
    assert not inputs_still_current(service, planned.inputs)


@pytest.mark.parametrize("velocity", [(0, 0), (0.3, -0.8), (100, 0)])
def test_bounded_disc_chain_contains_every_sampled_moving_envelope(velocity):
    obstacle = MovingDiscBound((3, 4), velocity, 0.4, 0.1, 0.2)
    horizon = 1.8
    discs = motion_sweep_discs((obstacle,), horizon)
    assert 1 <= len(discs) <= 64
    # Independent containment check, including exact start/end and slab boundaries.
    for i in range(1001):
        t = horizon * i / 1000
        center = (3 + velocity[0] * t, 4 + velocity[1] * t)
        radius = 0.4 + 0.1 + 0.2 * t
        assert any(math.dist(center, (d.x, d.y)) + radius <= d.radius_m + 1e-9 for d in discs)


def test_explicit_policy_must_agree_with_static_object_bound():
    with pytest.raises(ValueError, match="diameter"):
        capture(observed_service(), replace(BOUNDS, static_diameter_m=2))
