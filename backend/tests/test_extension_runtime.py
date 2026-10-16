import json
from collections import defaultdict
from itertools import pairwise
from pathlib import Path
from shutil import copyfile

import pytest
from fastapi.testclient import TestClient

from campus_sim.api import create_app
from campus_sim.domain import CreateRequest
from campus_sim.map_builder import SOURCE_FILES, build_map
from campus_sim.map_coordinates import MapCoordinates, projection
from campus_sim.map_data import default_map_dir
from campus_sim.map_extension import merge_intervals
from campus_sim.osm_roads import read_network
from campus_sim.service import MobilityService
from campus_sim.unity_coverage import UnityCoverage

ROOT = Path(__file__).resolve().parents[2]
TARGETS = ("hitech", "dorm_1", "dorm_2", "dorm_3", "inha_station")


def command(identifier, pickup="main_gate", dropoff="hitech"):
    return CreateRequest(
        command_id=identifier,
        owner_id="extensions",
        service_type="PASSENGER",
        pickup_landmark_id=pickup,
        dropoff_landmark_id=dropoff,
    )


def test_five_queued_destinations_complete_in_fifo_order_only_after_home_return():
    service = MobilityService.from_osm_map(vehicle_count=1, battery_enabled=False, clock=lambda: 0)
    assert list(service.vehicles) == ["V01"]
    requests = [
        service.create_request(command(str(i), dropoff=target)).request
        for i, target in enumerate(TARGETS)
    ]
    assert all(r.status == "QUEUED" for r in requests[1:])
    expected_departure = 0
    for i, request in enumerate(requests):
        trip = service.trips[request.id]
        assert trip.departed_at_s == pytest.approx(expected_departure)
        assert request.vehicle_id == "V01"
        assert trip.returns_at_s - trip.departed_at_s == pytest.approx(
            sum(
                route.length_m / (30 / 3.6)
                for route in (trip.pickup_route, trip.delivery_route, trip.return_route)
            )
        )
        service.step(trip.dropoff_at_s)
        assert request.status == "COMPLETED" and not service.vehicles["V01"].available
        service.step(trip.returns_at_s - 1e-5)
        assert all(r.status == "QUEUED" for r in requests[i + 1 :])
        service.step(trip.returns_at_s)
        expected_departure = trip.returns_at_s
    assert len(service.trips) == 5
    assert all(r.status == "COMPLETED" for r in requests)
    assert all(t.returned for t in service.trips.values()) and service.vehicles["V01"].available


@pytest.mark.parametrize("index", range(5))
def test_added_stop_can_be_pickup_and_delivery_is_not_always_from_home(index):
    service = MobilityService.from_osm_map(vehicle_count=1, battery_enabled=False, clock=lambda: 0)
    pickup, dropoff = TARGETS[index], TARGETS[(index + 1) % 5]
    request = service.create_request(command(str(index), pickup, dropoff)).request
    trip = service.trips[request.id]
    assert trip.pickup_route.length_m > 0
    pickup_node = service.stop_nodes[request.pickup_stop_id]
    assert trip.pickup_route.polyline[-1] == service.graph.nodes[pickup_node]
    assert trip.delivery_route.polyline[0] == trip.pickup_route.polyline[-1]
    assert trip.return_route.polyline[0] == trip.delivery_route.polyline[-1]
    service.step(trip.returns_at_s)
    assert request.status == "COMPLETED" and service.vehicles["V01"].available


def test_station_return_uses_the_unity_backed_detour_and_has_no_shortcut():
    service = MobilityService.from_osm_map(vehicle_count=1, battery_enabled=False, clock=lambda: 0)
    raw = json.loads((default_map_dir() / "graph.json").read_text(encoding="utf-8"))
    source = {tuple(p["edge_id"]): p["osm_way_id"] for p in raw["provenance"]}
    assert "1215102704" not in source.values()
    request = service.create_request(command("station", dropoff="inha_station")).request
    route = service.trips[request.id].return_route
    ways = []
    for edge in route.edge_ids:
        if not ways or ways[-1] != source[edge]:
            ways.append(source[edge])
    assert ways == ["955217469", "281621314", "481241683"]
    assert route.length_m == pytest.approx(407.157469298, abs=0.01)
    coverage = UnityCoverage(ROOT, MapCoordinates()).route(route, service.graph)
    assert not coverage["missing_way_ids"]
    assert set(coverage["length_estimate_m_by_source"]) <= {"core_unity", "scoped_unity"}


def test_compiled_edges_stay_on_source_segments_without_duplicate_overlap():
    raw = json.loads((default_map_dir() / "graph.json").read_text(encoding="utf-8"))
    nodes, segments, _ = read_network(
        ROOT / "Docs/MapResearch/2026-09-17/expanded_campus.osm", MapCoordinates()
    )
    originals = {s.key: s for s in segments}
    groups = defaultdict(list)
    for edge, source in zip(raw["edges"], raw["provenance"]):
        key = source["osm_way_id"], source["source_segment_index"]
        segment = originals[key]
        positions = [projection(p, nodes[segment.u], nodes[segment.v]) for p in edge["polyline"]]
        assert max(p[0] for p in positions) < 0.003
        assert source["source_fraction_interval"][0] - 1e-9 <= min(p[1] for p in positions)
        assert max(p[1] for p in positions) <= source["source_fraction_interval"][1] + 1e-9
        direction = "f" if edge["key"].endswith(":f") else "r"
        groups[key, direction].append(sorted(p[1] for p in positions))
        assert source["unity_centerline_sources"]
    for intervals in groups.values():
        ordered = sorted(intervals)
        assert all(b[0] >= a[1] - 1e-9 for a, b in pairwise(ordered))


def test_interval_merge_preserves_gaps_and_unions_real_overlap():
    merged = merge_intervals([(0.0, 0.3, "core"), (0.2, 0.5, "extension"), (0.7, 1.0, "separate")])
    assert [piece[:2] for piece in merged] == [[0.0, 0.5], [0.7, 1.0]]
    assert merged[0][2] == ["core", "extension"]


@pytest.mark.parametrize(
    "fault", ["shortcut", "missing_segment", "outside_fraction", "missing_target"]
)
def test_invalid_corridor_pins_fail_instead_of_inventing_connectors(tmp_path, fault):
    for name in SOURCE_FILES.values():
        destination = tmp_path / name
        destination.parent.mkdir(parents=True, exist_ok=True)
        copyfile(ROOT / name, destination)
    path = tmp_path / SOURCE_FILES["policy"]
    policy = json.loads(path.read_text(encoding="utf-8"))
    extension = policy["extension_corridors"]
    if fault == "shortcut":
        extension["excluded_osm_way_ids"] = []
    elif fault == "missing_segment":
        extension["targets"]["hitech"]["segments"][0]["segment_index"] = 999999
    elif fault == "outside_fraction":
        extension["targets"]["hitech"]["segments"][0]["end_fraction"] = 1.5
    else:
        extension["targets"].pop("dorm_3")
    path.write_text(json.dumps(policy), encoding="utf-8")
    with pytest.raises(ValueError):
        build_map(tmp_path, tmp_path / "output")


def test_http_five_requests_use_v2_and_reassign_single_vehicle_on_return():
    clock = [0.0]
    service = MobilityService.from_osm_map(
        vehicle_count=1, battery_enabled=False, clock=lambda: clock[0]
    )
    with TestClient(create_app(service)) as client:
        for index, target in enumerate(TARGETS):
            response = client.post(
                "/v1/requests", json=command(str(index), dropoff=target).model_dump(mode="json")
            )
            assert response.status_code == 201
        for index in range(5):
            state = client.get("/v1/debug/virtual-state").json()
            active = [t for t in state["trips"] if not t["returned"]]
            assert len(active) == 1
            assert state["map_version"] == "inha-osm-unity-v2" and not state["battery_model"]
            assert len(state["vehicles"]) == 1
            assert state["requests"][index]["dropoff_landmark_id"] == TARGETS[index]
            clock[0] = active[0]["returns_at_s"]
        state = client.get("/v1/debug/virtual-state").json()
        assert all(r["status"] == "COMPLETED" for r in state["requests"])
        assert state["vehicles"][0]["available"]
