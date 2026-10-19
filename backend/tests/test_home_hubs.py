import json
from pathlib import Path
from shutil import copyfile

import pytest

from campus_sim.domain import CreateRequest
from campus_sim.map_builder import SOURCE_FILES, build_map
from campus_sim.map_data import default_map_dir, load_map_bundle
from campus_sim.service import MobilityService
from campus_sim.web_demo import DemoSession

ROOT = Path(__file__).resolve().parents[2]


def test_default_homes_are_three_distinct_source_road_nodes_and_fully_connected():
    bundle = load_map_bundle(default_map_dir(), source_root=ROOT)
    assert bundle.home_hub_ids == {
        "V01": "main_gate",
        "V02": "rear_gate",
        "V03": "west_field_parking",
    }
    assert len(set(bundle.home_nodes.values())) == 3
    assert bundle.home_nodes["V03"] == "osm:10058484941"
    assert len(bundle.graph.nodes) == 187 and len(bundle.graph.edges) == 402
    assert all(h["round_trip_reachable_stop_count"] == 14 for h in bundle.hubs.values())
    assert bundle.hubs["west_field_parking"]["source_parking_osm_way_id"] == "568420971"
    assert bundle.hubs["west_field_parking"]["source_unity_hub_id"] == "west_field_autonomous_hub"
    assert bundle.hubs["west_field_parking"]["verification_status"].startswith("UNVERIFIED")


def test_each_vehicle_routes_from_and_back_to_own_home_and_charges_there():
    service = MobilityService.from_osm_map(clock=lambda: 0)
    requests = [
        service.create_request(
            CreateRequest(
                command_id=str(i),
                owner_id="hubs",
                service_type="PASSENGER",
                pickup_landmark_id="main_gate",
                dropoff_landmark_id="inha_station",
            )
        ).request
        for i in range(3)
    ]
    assert [r.vehicle_id for r in requests] == ["V01", "V02", "V03"]
    for request in requests:
        trip = service.trips[request.id]
        home = service.home_nodes[request.vehicle_id]
        assert trip.home_node == home
        assert trip.pickup_route.polyline[0] == service.graph.nodes[home]
        assert trip.return_route.polyline[-1] == service.graph.nodes[home]
        assert sum(trip.energy_by_leg_wh) + service.battery_policy.reserve_wh <= 2000
        if request.vehicle_id != "V01":
            assert trip.pickup_route.cost_s > 0  # No teleport to the requested main gate.
    service.step(max(t.returns_at_s for t in service.trips.values()))
    assert len(service.charges) == 3
    for charge in service.charges:
        trip = next(t for t in service.trips.values() if t.vehicle_id == charge.vehicle_id)
        assert charge.started_at_s == trip.returns_at_s
    service.step(max(c.ends_at_s for c in service.charges))
    assert service.virtual_state()["vehicle_fifo"] == [
        c.vehicle_id for c in sorted(service.charges, key=lambda c: c.ends_at_s)
    ]


def test_web_reset_restores_hub_positions_and_labels_with_stationary_home_charge():
    wall = [0.0]
    demo = DemoSession(wall_clock=lambda: wall[0])
    initial = demo.state()
    assert [v["home_name"] for v in initial["vehicles"]] == ["정문", "후문", "운동장 옆 주차장"]
    assert len({tuple(v["position"]) for v in initial["vehicles"]}) == 3
    assert len(demo.map["hubs"]) == 3
    assert {area["id"] for area in demo.map["areas"]} >= {"568420971"}
    demo.scenario("battery")
    wall[0] = 6
    charging = demo.state()
    assert all(v["phase"] == "CHARGING" for v in charging["vehicles"])
    assert [v["position"] for v in charging["vehicles"]] == [
        v["position"] for v in initial["vehicles"]
    ]
    assert any("후문 충전 시작" in event["text"] for event in charging["events"])
    assert any("운동장 옆 주차장 충전 시작" in event["text"] for event in charging["events"])
    reset = demo.reset()
    assert [v["position"] for v in reset["vehicles"]] == [
        v["position"] for v in initial["vehicles"]
    ]


def test_distributed_homes_keep_vehicle_fifo_instead_of_nearest_matching():
    service = MobilityService.from_osm_map(clock=lambda: 0)
    for i, expected in enumerate(("V01", "V02")):
        request = service.create_request(
            CreateRequest(
                command_id=str(i),
                owner_id="fifo-hubs",
                service_type="PASSENGER",
                pickup_landmark_id="rear_gate",
                dropoff_landmark_id="library",
            )
        ).request
        assert request.vehicle_id == expected
        trip = service.trips[request.id]
        if expected == "V01":
            assert trip.pickup_route.cost_s > 0
        else:
            assert trip.pickup_route.cost_s == 0


@pytest.mark.parametrize(
    "change", ["outside_parking", "unknown_hub", "duplicate_binding", "unity_mismatch"]
)
def test_invalid_hub_bindings_fail_instead_of_inventing_connections(tmp_path, change):
    for source in SOURCE_FILES.values():
        destination = tmp_path / source
        destination.parent.mkdir(parents=True, exist_ok=True)
        copyfile(ROOT / source, destination)
    path = tmp_path / SOURCE_FILES["policy"]
    policy = json.loads(path.read_text(encoding="utf-8"))
    if change == "outside_parking":
        bundle = load_map_bundle(default_map_dir())
        policy["hubs"][2]["node_id"] = bundle.home_nodes["V01"]
    elif change == "unknown_hub":
        policy["vehicles"][1]["home_hub_id"] = "missing"
    elif change == "duplicate_binding":
        policy["vehicles"][1]["home_hub_id"] = "main_gate"
    else:
        policy["hubs"][2]["source_unity_hub_id"] = "wrong"
    path.write_text(json.dumps(policy), encoding="utf-8")
    with pytest.raises(ValueError):
        build_map(tmp_path, tmp_path / "output")
