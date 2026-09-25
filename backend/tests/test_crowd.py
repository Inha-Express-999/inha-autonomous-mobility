from __future__ import annotations

from dataclasses import replace
from pathlib import Path

import pytest
from pydantic import ValidationError

from campus_sim.crowd import CrowdModelConfig
from campus_sim.domain import (
    CreateRequest,
    EgoLocalization,
    MapPosition,
    SensorObservation,
    ServiceType,
)
from campus_sim.planning import astar
from campus_sim.realtime import make_snapshot
from campus_sim.road_graph import load_road_graph
from campus_sim.service import MobilityService

ROOT = Path(__file__).resolve().parents[2]
GRAPH_PATH = ROOT / "maps/fixtures/campus-synthetic-6.json"
TEST_ZONE_ID = "synthetic-test-corridor"


def _graph_with_test_zone():
    graph = load_road_graph(GRAPH_PATH)
    edges = [
        edge.model_copy(update={"zone_ids": [TEST_ZONE_ID]}) if edge.id == "e25" else edge
        for edge in graph.edges
    ]
    return graph.model_copy(update={"edges": edges})


def _make_request(service: MobilityService) -> CreateRequest:
    assert service.graph is not None
    return CreateRequest(
        command_id="crowd-route-request",
        owner_id="crowd-route-test",
        service_type=ServiceType.PASSENGER,
        pickup_landmark_id=service.graph.nodes[3].landmark_id,
        dropoff_landmark_id=service.graph.nodes[0].landmark_id,
        party_size=1,
    )


def _disable_peak_avoidance(service: MobilityService, penalty_s_per_density_m: float = 4.0) -> None:
    service.crowd_model = service.crowd_model.model_copy(update={
        "zones": {
            zone_id: zone.model_copy(update={
                "avoid_during_peak": False,
                "avoid_density": 999.0,
                "penalty_s_per_density_m": penalty_s_per_density_m,
            })
            for zone_id, zone in service.crowd_model.zones.items()
        }
    })


def test_default_crowd_profile_uses_simulation_time_and_marks_values_synthetic() -> None:
    profile = CrowdModelConfig.from_default_config()

    assert profile.profile_status == "SYNTHETIC_ASSUMPTION"
    assert profile.timezone == "Asia/Seoul"
    assert profile.density_prior(TEST_ZONE_ID, 0.0) == 0.003
    assert profile.density_prior(TEST_ZONE_ID, 1_500.0) > 0.2
    assert profile.density_prior(TEST_ZONE_ID, 1_800.0) > 0.2
    assert profile.density_prior(TEST_ZONE_ID, 1_800.0) < 1.0
    unzoned_graph = load_road_graph(GRAPH_PATH)
    assert profile.edge_cost_snapshot_s(unzoned_graph, 1_800.0) == ({}, {})


@pytest.mark.parametrize(
    ("changes", "message"),
    [
        ({"transition_times": ["09:00:00", "09:00:00"]}, "must be unique"),
        ({"pre_peak_weight": 0.5}, "must sum to 1"),
    ],
)
def test_crowd_config_rejects_ambiguous_peak_settings(
    changes: dict[str, object], message: str
) -> None:
    profile = CrowdModelConfig.from_default_config()
    data = profile.model_dump()
    data.update(changes)

    with pytest.raises(ValidationError, match=message):
        CrowdModelConfig.model_validate(data)


def test_peak_crowd_prior_changes_only_tagged_edge_cost_and_route() -> None:
    graph = _graph_with_test_zone()
    profile = CrowdModelConfig.from_default_config()
    base_edge_cost = next(edge.cost_s() for edge in graph.edges if edge.id == "e25")

    off_peak_costs, _ = profile.edge_cost_snapshot_s(graph, 0.0)
    peak_costs, _ = profile.edge_cost_snapshot_s(graph, 1_800.0)
    assert off_peak_costs["e25"] == base_edge_cost
    assert peak_costs["e25"] > base_edge_cost

    off_peak_route = astar(graph, "n1", "n4", edge_costs_s=off_peak_costs)
    peak_route = astar(graph, "n1", "n4", edge_costs_s=peak_costs)
    assert "e25" in off_peak_route.edge_ids
    assert "e25" not in peak_route.edge_ids


def test_service_applies_synthetic_crowd_prior_to_assigned_route_and_speed() -> None:
    off_peak = MobilityService.from_synthetic_graph(GRAPH_PATH)
    assert off_peak.graph is not None
    off_peak.graph = _graph_with_test_zone()
    off_peak.create_request(_make_request(off_peak))
    off_peak_runtime = off_peak.vehicle_runtime["V01"]
    assert any(abs(y - 120.0) < 1e-6 for _, y in off_peak_runtime.route_points)
    off_peak_route_duration = off_peak._route_duration(off_peak_runtime)

    caution = MobilityService.from_synthetic_graph(GRAPH_PATH)
    assert caution.graph is not None
    caution.graph = _graph_with_test_zone()
    _disable_peak_avoidance(caution)
    caution.simulation_time_s = 1_500.0
    caution.create_request(_make_request(caution))
    caution_runtime = caution.vehicle_runtime["V01"]
    assert any(abs(y - 120.0) < 1e-6 for _, y in caution_runtime.route_points)
    assert caution._route_duration(caution_runtime) > off_peak_route_duration

    peak = MobilityService.from_synthetic_graph(GRAPH_PATH)
    assert peak.graph is not None
    peak.graph = _graph_with_test_zone()
    peak.simulation_time_s = 1_800.0
    peak.create_request(_make_request(peak))
    peak_runtime = peak.vehicle_runtime["V01"]
    assert all(abs(y - 120.0) >= 1e-6 for _, y in peak_runtime.route_points)


def test_synthetic_route_replans_at_peak_only_after_material_eta_improvement() -> None:
    service = MobilityService.from_synthetic_graph(GRAPH_PATH)
    assert service.graph is not None
    service.graph = _graph_with_test_zone()
    _disable_peak_avoidance(service, penalty_s_per_density_m=5.0)
    service.create_request(_make_request(service))
    runtime = service.vehicle_runtime["V01"]
    initial_route_id = runtime.route_id
    initial_zone_speed_mps = runtime.route_speeds[runtime.route_edge_ids.index("e25")]
    assert any(abs(y - 120.0) < 1e-6 for _, y in runtime.route_points)

    service.advance(0.05)
    service.simulation_time_s = 1_500.0
    service.advance(0.05)
    assert runtime.route_id == initial_route_id
    assert runtime.route_speeds[runtime.route_edge_ids.index("e25")] < initial_zone_speed_mps

    service.simulation_time_s = 1_800.0
    service.advance(0.05)
    assert runtime.route_id != initial_route_id
    assert all(abs(y - 120.0) >= 1e-6 for _, y in runtime.route_points)


def test_service_avoids_synthetic_zone_within_detour_budget_and_reports_reason() -> None:
    service = MobilityService.from_synthetic_graph(GRAPH_PATH)
    assert service.graph is not None
    service.graph = _graph_with_test_zone()
    service.simulation_time_s = 900.0
    service.create_request(_make_request(service))
    runtime = service.vehicle_runtime["V01"]
    assert all(abs(y - 120.0) >= 1e-6 for _, y in runtime.route_points)
    assert runtime.route_reason == "CROWD_AVOIDANCE"
    snapshot = make_snapshot(service, "PC_Operator", None, 1)
    assert snapshot["vehicles"][0]["reason"] == "CROWD_AVOIDANCE"


def test_service_penalizes_zone_when_detour_exceeds_budget() -> None:
    service = MobilityService.from_synthetic_graph(GRAPH_PATH)
    assert service.graph is not None
    service.graph = _graph_with_test_zone()
    zone = service.crowd_model.zones[TEST_ZONE_ID].model_copy(
        update={"detour_extra_s": 1.0, "detour_ratio": 0.0,
                "avoid_penalty_s_per_m": 0.001, "penalty_s_per_density_m": 0.0}
    )
    service.crowd_model = service.crowd_model.model_copy(
        update={"zones": {TEST_ZONE_ID: zone}}
    )
    service.simulation_time_s = 900.0
    service.create_request(_make_request(service))
    runtime = service.vehicle_runtime["V01"]
    assert any(abs(y - 120.0) < 1e-6 for _, y in runtime.route_points)
    assert runtime.route_reason == "CROWD_AVOIDANCE"
    assert any(speed < 5.0 for speed in runtime.route_speeds)


def test_service_route_cost_matches_capped_speed_and_edge_delays() -> None:
    service = MobilityService.from_synthetic_graph(GRAPH_PATH)
    assert service.graph is not None
    service.graph = service.graph.model_copy(update={
        "edges": [
            edge.model_copy(update={
                "allowed_speed_mps": 10.0,
                "crowd_penalty_s": 2.0,
                "zone_penalty_s": 3.0,
                "expected_wait_s": 4.0,
            })
            for edge in service.graph.edges
        ]
    })
    request = _make_request(service)
    created = service.create_request(request).request
    assert created is not None
    runtime = service.vehicle_runtime["V01"]
    route, _, _ = service._plan_route(
        runtime.node_id,
        "n4",
        request.service_type,
        request.service_needs.requires_step_free,
    )
    expected_cost_s = sum(
        edge.length_m / 5.0
        + edge.crowd_penalty_s
        + edge.zone_penalty_s
        + edge.expected_wait_s
        for edge in service.graph.edges
        if edge.id in route.edge_ids
    )
    assert route.path_cost_s == pytest.approx(expected_cost_s)
    assert service._route_duration(runtime) == pytest.approx(expected_cost_s, abs=0.1)


def test_snapshot_keeps_route_geometry_immutable_while_runtime_progresses() -> None:
    service = MobilityService.from_synthetic_graph(GRAPH_PATH)
    assert service.graph is not None
    service.graph = _graph_with_test_zone()
    service.create_request(_make_request(service))
    runtime = service.vehicle_runtime["V01"]
    route_id = runtime.route_id
    initial_runtime_points = runtime.route_points.copy()
    before = make_snapshot(service, "PC_Operator", None, 1)["routes"][0]

    service.advance(25.0)
    after = make_snapshot(service, "PC_Operator", None, 2)["routes"][0]

    assert runtime.route_id == route_id
    assert runtime.route_points != initial_runtime_points
    assert after["id"] == before["id"]
    assert after["polyline"] == before["polyline"]
    assert len(after["segmentSpeedsMps"]) == len(after["polyline"]) - 1


def test_localized_actual_speed_survives_route_and_cost_profile_refresh() -> None:
    service = MobilityService.from_synthetic_graph(GRAPH_PATH)
    assert service.graph is not None
    created = service.create_request(_make_request(service))
    assert created.request is not None
    runtime = service.vehicle_runtime["V01"]
    request = service.requests[created.request.id]
    pose = EgoLocalization(
        vehicle_id="V01",
        session_id="route-speed-session",
        observed_tick=1,
        map_version=service.graph.map_version,
        position=MapPosition(x=0.0, y=0.0, z=0.0),
        heading_rad=0.0,
        speed_mps=0.05,
    )
    assert service.record_ego_localization(pose)

    service._set_route(runtime, request.pickup_stop_id or "", request, "TO_PICKUP")
    assert runtime.speed_mps == pytest.approx(0.05)
    service._refresh_route_speeds(runtime, {})
    assert runtime.speed_mps == pytest.approx(0.05)


def test_localized_route_replans_only_at_fresh_stopped_graph_node() -> None:
    service = MobilityService.from_synthetic_graph(GRAPH_PATH)
    assert service.graph is not None
    service.graph = _graph_with_test_zone()
    _disable_peak_avoidance(service, penalty_s_per_density_m=5.0)
    service.create_request(_make_request(service))
    runtime = service.vehicle_runtime["V01"]
    initial_route_id = runtime.route_id
    assert any(abs(y - 120.0) < 1e-6 for _, y in runtime.route_points)
    service.safety_policy = replace(service.safety_policy, resume_clear_s=0.0)

    def report_pose_and_clear_sensor(tick: int, x: float, speed_mps: float) -> None:
        assert service.graph is not None
        pose = EgoLocalization(
            vehicle_id="V01",
            session_id="localized-replan-session",
            observed_tick=tick,
            map_version=service.graph.map_version,
            position=MapPosition(x=x, y=0.0, z=0.0),
            heading_rad=0.0,
            speed_mps=speed_mps,
        )
        assert service.record_ego_localization(pose)
        frame = SensorObservation(
            vehicle_id="V01",
            sensor_id="front-lidar",
            sensor_type="LIDAR_2D",
            session_id=pose.session_id,
            map_version=pose.map_version,
            observed_tick=tick,
            ego_pose_tick=tick,
            valid=True,
            detections=[],
        )
        assert service.record_sensor_observation(frame)

    service.simulation_time_s = 1_800.0
    report_pose_and_clear_sensor(1, 0.0, 0.2)
    service._maybe_replan_localized_route("V01", runtime)
    assert runtime.route_id == initial_route_id, "A moving vehicle must keep its current route."

    service.simulation_time_s = 1_805.0
    report_pose_and_clear_sensor(2, 5.0, 0.0)
    service._maybe_replan_localized_route("V01", runtime)
    assert runtime.route_id == initial_route_id, "An off-node ego pose must not create a connector."

    service.simulation_time_s = 1_810.0
    report_pose_and_clear_sensor(3, 0.0, 0.0)
    service.advance(0.05)
    assert runtime.route_id != initial_route_id
    assert all(abs(y - 120.0) >= 1e-6 for _, y in runtime.route_points)
