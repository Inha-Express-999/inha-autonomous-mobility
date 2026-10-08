from __future__ import annotations

import json

import pytest
from pydantic import ValidationError

from campus_sim.domain import CreateRequest, RequestStatus
from campus_sim.planning import astar, dijkstra, node_for_stop
from campus_sim.road_graph import RoadGraphDocument, load_road_graph
from campus_sim.service import MobilityService


def _junction_graph() -> dict:
    def edge(edge_id: str, start: str, end: str, start_x: float, end_x: float) -> dict:
        return {
            "id": edge_id,
            "from_node": start,
            "to_node": end,
            "key": "0",
            "geometry_m": [{"x": start_x, "y": 0}, {"x": end_x, "y": 0}],
            "length_m": 50,
            "width_m": 4,
            "allowed_speed_mps": 5,
            "allowed_service_types": ["PASSENGER", "CARGO"],
            "allowed_vehicle_classes": ["CAMPUS_SHUTTLE"],
            "source": "synthetic-junction-test",
            "verification_status": "SYNTHETIC",
            "step_free": True,
        }

    return {
        "schema_version": 1,
        "map_id": "junction-test",
        "map_version": "synthetic-junction-test-v1",
        "data_status": "SYNTHETIC_FIXTURE",
        "coordinate_frame": "SYNTHETIC_LOCAL_METERS",
        "verification_status": "SYNTHETIC_FIXTURE",
        "source_description": "Test-only road junction with no Stop",
        "nodes": [
            {"id": "n1", "landmark_id": "fixture_landmark_1", "stop_id": "stop1",
             "position_m": {"x": 0, "y": 0}},
            {"id": "junction", "position_m": {"x": 50, "y": 0}},
            {"id": "n2", "landmark_id": "fixture_landmark_2", "stop_id": "stop2",
             "position_m": {"x": 100, "y": 0}},
        ],
        "edges": [
            edge("e1j", "n1", "junction", 0, 50),
            edge("ej2", "junction", "n2", 50, 100),
            edge("e2j", "n2", "junction", 100, 50),
            edge("ej1", "junction", "n1", 50, 0),
        ],
    }


def test_planner_and_service_route_through_unlabeled_junction(tmp_path) -> None:
    graph_path = tmp_path / "junction.json"
    graph_path.write_text(json.dumps(_junction_graph()), encoding="utf-8")
    graph = load_road_graph(graph_path)

    a_star = astar(graph, "n1", "n2")
    assert a_star.node_ids == ("n1", "junction", "n2")
    assert a_star.stop_ids == ("stop1", "stop2")
    assert a_star.path_cost_s == dijkstra(graph, "n1", "n2").path_cost_s
    with pytest.raises(ValueError, match="unknown stop id"):
        node_for_stop(graph, None)

    service = MobilityService.from_synthetic_graph(graph_path)
    assert set(service.stops) == {"stop1", "stop2"}
    assert service._route_duration_between_nodes(
        "junction", "n2", service_type=service.graph.edges[0].allowed_service_types[0],
        requires_step_free=False,
    ) == 10
    with pytest.raises(ValueError, match="route node is unavailable"):
        service._route_duration_between_nodes(
            "missing", "missing", service_type=service.graph.edges[0].allowed_service_types[0],
            requires_step_free=False,
        )
    ack = service.create_request(CreateRequest(
        command_id="junction-trip",
        owner_id="junction-passenger",
        service_type="PASSENGER",
        pickup_landmark_id="fixture_landmark_1",
        dropoff_landmark_id="fixture_landmark_2",
    ))
    assert ack.request.status is RequestStatus.ASSIGNED
    assert service.vehicle_runtime["V01"].route_edge_ids == []
    service.advance(2.0)
    assert "ej2" in service.vehicle_runtime["V01"].route_edge_ids


def test_junction_cannot_claim_landmark_without_stop() -> None:
    payload = _junction_graph()
    payload["nodes"][1]["landmark_id"] = "invented_landmark"
    with pytest.raises(ValidationError, match="must both be set or both omitted"):
        RoadGraphDocument.model_validate(payload)
