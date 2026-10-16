import json
import math
import xml.etree.ElementTree as ET
from dataclasses import replace
from pathlib import Path
from shutil import copyfile, copytree

import pytest
from fastapi.testclient import TestClient

from campus_sim.api import create_app
from campus_sim.domain import CreateRequest
from campus_sim.map_builder import SOURCE_FILES, build_map, digest
from campus_sim.map_coordinates import MapCoordinates
from campus_sim.map_data import default_map_dir, load_map_bundle
from campus_sim.planning import CostSnapshot, DijkstraPlanner, NoPath
from campus_sim.service import MobilityService

ROOT = Path(__file__).resolve().parents[2]


def command(identifier, pickup="main_gate", dropoff="library", **extra):
    return CreateRequest(
        command_id=identifier,
        owner_id="map-test",
        service_type="PASSENGER",
        pickup_landmark_id=pickup,
        dropoff_landmark_id=dropoff,
        **extra,
    )


def test_pinned_bundle_sources_topology_and_oneway():
    bundle = load_map_bundle(default_map_dir(), source_root=ROOT)
    assert not bundle.metadata["synthetic_geometry"]
    assert not bundle.metadata["operationally_verified"]
    assert bundle.metadata["source_hashes_checked"]
    assert bundle.graph.synthetic  # Permission to use assumptions, NOT geometry provenance.
    assert bundle.metadata["coverage"]["osm_way_count"] == 59
    assert bundle.metadata["coverage"]["round_trip_reachable_stop_count"] == 14
    assert [v.id for v in bundle.vehicles] == ["V01", "V02", "V03"]
    raw = json.loads((default_map_dir() / "graph.json").read_text(encoding="utf-8"))
    assert len(raw["provenance"]) == len(bundle.graph.edges)
    for edge, source in zip(bundle.graph.edges, raw["provenance"]):
        assert source["osm_tags"]["highway"] in {"service", "residential", "secondary"}
        assert edge.key.startswith(source["osm_way_id"] + ":")
        assert source["source_pieces"] and source["unity_centerline_sources"]
        assert not edge.verified and edge.width_m is None
        assert edge.length_m == pytest.approx(math.dist(*edge.polyline))
        if source["osm_tags"].get("oneway") == "yes":
            assert edge.key.endswith(":f")
        for node in (edge.u, edge.v):
            assert node.startswith(("osm:", "clip:", "split:"))


@pytest.mark.parametrize("lon,lat", [(126.6535, 37.4506), (126.652, 37.448), (126.657, 37.451)])
def test_three_projection_control_points_and_unity_round_trip(lon, lat):
    adapter = MapCoordinates()
    unity = adapter.wgs84_to_unity(lon, lat)
    aeqd = adapter.from_wgs84(lon, lat)
    assert math.dist(aeqd, adapter.from_unity(*unity)) <= 0.01
    assert math.dist(unity, adapter.to_unity(*aeqd)) <= 0.01


def test_rebuild_is_deterministic_and_does_not_edit_sources(tmp_path):
    before = {name: digest(ROOT / name) for name in SOURCE_FILES.values()}
    one, two = tmp_path / "one", tmp_path / "two"
    build_map(ROOT, one)
    build_map(ROOT, two)
    for name in (
        "manifest.json",
        "graph.json",
        "landmarks.json",
        "stops.json",
        "build_report.json",
    ):
        assert (one / name).read_bytes() == (two / name).read_bytes()
        assert (one / name).read_bytes() == (default_map_dir() / name).read_bytes()
    assert before == {name: digest(ROOT / name) for name in SOURCE_FILES.values()}


def test_payload_tampering_and_missing_data_fail_without_fallback(tmp_path):
    copied = tmp_path / "map"
    copytree(default_map_dir(), copied)
    (copied / "graph.json").write_text("{}", encoding="utf-8")
    with pytest.raises(ValueError, match="hash"):
        MobilityService.from_osm_map(copied)
    with pytest.raises(FileNotFoundError):
        MobilityService.from_osm_map(tmp_path / "missing")


def copy_sources(target):
    for name in SOURCE_FILES.values():
        destination = target / name
        destination.parent.mkdir(parents=True, exist_ok=True)
        copyfile(ROOT / name, destination)


def test_changed_original_source_invalidates_pinned_bundle(tmp_path):
    copy_sources(tmp_path)
    with (tmp_path / SOURCE_FILES["policy"]).open("a", encoding="utf-8") as stream:
        stream.write("\n")
    with pytest.raises(ValueError, match="source hash"):
        load_map_bundle(default_map_dir(), source_root=tmp_path)


def test_importer_caps_configured_30kmh_at_lower_osm_limit(tmp_path):
    copy_sources(tmp_path)
    data = json.loads((tmp_path / SOURCE_FILES["unity"]).read_text(encoding="utf-8"))
    road = next(f for f in data["features"] if f["kind"] == "road" and f["subtype"] == "service")
    way_id = road["id"].split("_")[0]
    path = tmp_path / SOURCE_FILES["expanded_osm"]
    tree = ET.parse(path)
    way = tree.getroot().find(f"way[@id='{way_id}']")
    ET.SubElement(way, "tag", {"k": "maxspeed", "v": "5"})
    tree.write(path, encoding="utf-8")
    build_map(tmp_path, tmp_path / "output")
    bundle = load_map_bundle(tmp_path / "output")
    speeds = [e.speed_mps for e in bundle.graph.edges if e.key.startswith(way_id + ":")]
    assert speeds and all(v == pytest.approx(5 / 3.6) for v in speeds)


@pytest.mark.parametrize("change", ["geometry", "missing_osm_node", "pedestrian", "verified"])
def test_invalid_imports_fail_without_inventing_roads(tmp_path, change):
    copy_sources(tmp_path)
    if change == "geometry":
        path = tmp_path / SOURCE_FILES["unity"]
        data = json.loads(path.read_text(encoding="utf-8"))
        road = next(
            f for f in data["features"] if f["kind"] == "road" and f["subtype"] == "service"
        )
        road["points"][1]["x"] += 10
        path.write_text(json.dumps(data), encoding="utf-8")
    elif change == "missing_osm_node":
        path = tmp_path / SOURCE_FILES["osm"]
        data = json.loads((tmp_path / SOURCE_FILES["unity"]).read_text(encoding="utf-8"))
        road = next(
            f for f in data["features"] if f["kind"] == "road" and f["subtype"] == "service"
        )
        tree = ET.parse(path)
        osm = tree.getroot()
        way = osm.find(f"way[@id='{road['id'].split('_')[0]}']")
        reference = way.find("nd").attrib["ref"]
        osm.remove(osm.find(f"node[@id='{reference}']"))
        tree.write(path, encoding="utf-8")
    else:
        path = tmp_path / SOURCE_FILES["policy"]
        data = json.loads(path.read_text(encoding="utf-8"))
        if change == "pedestrian":
            data["allowed_highways"].append("footway")
        else:
            data["routing_mode"] = "verified"
        path.write_text(json.dumps(data), encoding="utf-8")
    with pytest.raises(ValueError):
        build_map(tmp_path, tmp_path / "output")


def test_unknown_source_verification_cannot_be_silently_promoted(tmp_path):
    copied = tmp_path / "map"
    copytree(default_map_dir(), copied)
    manifest = json.loads((copied / "manifest.json").read_text(encoding="utf-8"))
    manifest["operationally_verified"] = True
    (copied / "manifest.json").write_text(json.dumps(manifest), encoding="utf-8")
    with pytest.raises(ValueError, match="unverified"):
        load_map_bundle(copied)


def test_service_one_vehicle_actual_route_lengths_time_and_fifo_return():
    service = MobilityService.from_osm_map(vehicle_count=1, battery_enabled=False, clock=lambda: 0)
    first = service.create_request(command("first")).request
    second = service.create_request(command("second", "library", "rear_gate")).request
    assert list(service.vehicles) == ["V01"]
    assert second.status == "QUEUED"
    trip = service.trips[first.id]
    assert trip.pickup_route.length_m == 0
    assert trip.delivery_route.length_m > 100
    speed = json.loads((default_map_dir() / "policy.json").read_text(encoding="utf-8"))[
        "default_speed_mps"
    ]
    assert speed == pytest.approx(30 / 3.6)
    assert service.costs.id.endswith(service.map_metadata["files"]["policy.json"][:12])
    expected = sum(
        route.length_m / speed
        for route in (trip.pickup_route, trip.delivery_route, trip.return_route)
    )
    assert trip.returns_at_s == pytest.approx(expected)
    service.step(trip.dropoff_at_s)
    assert first.status == "COMPLETED" and second.status == "QUEUED"
    service.step(trip.returns_at_s - 0.001)
    assert second.status == "QUEUED"
    service.step(trip.returns_at_s)
    assert second.vehicle_id == "V01"
    assert service.trips[second.id].departed_at_s == pytest.approx(expected)
    service.step(service.trips[second.id].returns_at_s)
    assert second.status == "COMPLETED" and service.vehicles["V01"].available


def test_step_free_access_is_not_claimed_by_adding_virtual_road_connections():
    service = MobilityService.from_osm_map(vehicle_count=1, battery_enabled=False, clock=lambda: 0)
    with pytest.raises(ValueError, match="NO_ACCESSIBLE_ALTERNATIVE"):
        service.create_request(
            command(
                "wheelchair",
                service_needs={
                    "requires_step_free": True,
                    "wheelchair_slots": 1,
                },
            )
        )
    assert not service.requests


def test_disconnected_stop_waits_and_does_not_block_feasible_next_request():
    service = MobilityService.from_osm_map(vehicle_count=1, battery_enabled=False, clock=lambda: 0)
    target = service.stop_nodes[service.landmarks["hitech"].stop_ids[0]]
    service.graph = replace(
        service.graph, edges=tuple(e for e in service.graph.edges if e.v != target)
    )
    service.costs = CostSnapshot.travel_time(service.graph)
    unreachable = service.create_request(command("unreachable", dropoff="hitech")).request
    assert unreachable.status == "QUEUED"
    assert service.virtual_state()["waiting_reasons"][unreachable.id] == "NO_ROUND_TRIP_ROUTE"
    feasible = service.create_request(command("feasible")).request
    assert feasible.vehicle_id == "V01"
    assert unreachable.status == "QUEUED"


def test_pinned_stop_reachability_matches_search_not_straight_line():
    bundle = load_map_bundle(default_map_dir())
    for stop in bundle.metadata["stop_candidates"]:
        planner, costs = DijkstraPlanner(), CostSnapshot.travel_time(bundle.graph)
        routes = [
            planner.search(bundle.graph, a, b, costs)
            for a, b in (
                (bundle.home_node, stop["node_id"]),
                (stop["node_id"], bundle.home_node),
            )
        ]
        assert stop["round_trip_reachable"] == all(
            not isinstance(route, NoPath) for route in routes
        )


def test_osm_http_state_has_explicit_mode_and_unity_path_coordinates():
    service = MobilityService.from_osm_map(vehicle_count=1, battery_enabled=False, clock=lambda: 0)
    with TestClient(create_app(service)) as client:
        response = client.post("/v1/requests", json=command("http").model_dump(mode="json"))
        assert response.status_code == 201
        state = client.get("/v1/debug/virtual-state").json()
        assert state["routing_mode"] == "osm_geometry_simulation"
        assert state["synthetic_geometry"] is False and state["battery_model"] is False
        route = state["trips"][0]["delivery_route"]
        assert len(route["unity_polyline_xz_m"]) == len(route["polyline"])
        assert state["map_version"] == "inha-osm-unity-v2"
