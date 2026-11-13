import json
from math import exp
from pathlib import Path

import pytest
from fastapi.testclient import TestClient
from pydantic import ValidationError

from campus_sim.api import create_app
from campus_sim.crowd import (
    CrowdPrior,
    CrowdPriorConfig,
    format_clock,
    length_inside_circle,
    road_groups,
)
from campus_sim.planning import RoadEdge, RoadGraph
from campus_sim.web_demo import DemoControl, DemoSession

ROOT = Path(__file__).resolve().parents[2]
CONFIG = ROOT / "configs/crowd_prior.json"


def raw_config():
    return json.loads(CONFIG.read_text(encoding="utf-8"))


def line_graph():
    nodes = {"a": (0.0, 0.0), "b": (100.0, 0.0), "c": (300.0, 0.0)}
    edges = []
    for u, v, key in (("a", "b", "ab"), ("b", "c", "bc")):
        for direction, (s, t) in (("f", (u, v)), ("r", (v, u))):
            edges.append(
                RoadEdge(s, t, f"{key}:{direction}", (nodes[s], nodes[t]), dist(s, t, nodes), 8.0)
            )
    return RoadGraph("test", nodes, tuple(edges))


def dist(s, t, nodes):
    return abs(nodes[s][0] - nodes[t][0])


def single_zone_config(**changes):
    data = raw_config()
    zone = {
        "id": "plaza",
        "name": "plaza",
        "anchor_landmark_id": "plaza_landmark",
        "radius_m": 30,
        "usable_area_m2": 900,
        "base_rate_pps": 0.3,
        "peak_rate_pps": 2.0,
        "multiplier": 2.0,
        "mean_dwell_s": 180,
        "avoid_during_window": False,
    } | changes
    data["zones"] = [zone]
    return CrowdPriorConfig.model_validate(data)


def test_repository_config_is_valid_and_marked_synthetic():
    config = CrowdPriorConfig.model_validate(raw_config())
    assert config.verification == "synthetic"
    assert config.transition_times == ["09:00", "10:30", "12:00", "13:30", "15:00"]
    assert config.start_time_of_day == "08:30:00"


@pytest.mark.parametrize(
    "change",
    [
        {"caution_density": 0.6},
        {"transition_times": ["10:30", "09:00"]},
        {"start_time_of_day": "25:00:00"},
        {"verification": "measured"},
        {"unknown": 1},
    ],
)
def test_invalid_config_is_rejected(change):
    with pytest.raises(ValidationError):
        CrowdPriorConfig.model_validate(raw_config() | change)


def test_zone_without_required_rates_is_rejected_not_defaulted():
    data = raw_config()
    del data["zones"][0]["peak_rate_pps"]
    with pytest.raises(ValidationError):
        CrowdPriorConfig.model_validate(data)


def test_roads_group_directions_and_measure_circle_overlap():
    graph = line_graph()
    roads = road_groups(graph)
    assert [r.id for r in roads] == ["ab", "bc"]
    assert all(len(r.edge_ids) == 2 for r in roads)
    assert length_inside_circle(((0.0, 0.0), (100.0, 0.0)), (50.0, 0.0), 10) == pytest.approx(
        20, abs=1
    )
    assert length_inside_circle(((0.0, 0.0), (100.0, 0.0)), (50.0, 50.0), 10) == 0


def test_pulse_matches_formula_and_is_zero_outside_window():
    prior = CrowdPrior(single_zone_config(), line_graph(), {"plaza_landmark": (50.0, 0.0)})
    nine = 9 * 3600
    expected = 0.6 * exp(-0.5 * (300 / 240) ** 2) + 0.4 * exp(-0.5 * (300 / 240) ** 2)
    assert prior.pulse(nine) == pytest.approx(expected)
    assert prior.pulse(nine - 901) == 0
    assert prior.pulse(nine + 901) == 0
    assert prior.in_window(nine + 900) and not prior.in_window(nine + 901)


def test_density_rises_before_peak_and_returns_to_base():
    prior = CrowdPrior(single_zone_config(), line_graph(), {"plaza_landmark": (50.0, 0.0)})
    zone = prior.zones[0]
    base = prior.density(zone, 8 * 3600)
    assert base == pytest.approx(0.3 * 180 / 900)
    peak = prior.density(zone, 9 * 3600 - 300)
    assert peak > prior.config.avoid_density > base
    assert prior.density(zone, 9 * 3600 + 3600) == pytest.approx(base)


def test_snapshot_levels_roads_time_of_day_and_never_closes():
    prior = CrowdPrior(single_zone_config(), line_graph(), {"plaza_landmark": (50.0, 0.0)})
    assert prior.road_zones == {"ab": ("plaza",)}
    quiet = prior.snapshot(0)
    assert quiet["time_of_day"] == "08:30:00"
    assert quiet["roads"] == [] and quiet["observed"] is False
    busy = prior.snapshot(25 * 60)  # 08:55
    assert busy["in_congestion_window"]
    assert busy["roads"] == [
        {
            "road_id": "ab",
            "level": "AVOID",
            "rho_prior": busy["zones"][0]["rho_prior"],
            "zone_id": "plaza",
            "reason": "PRIOR_DENSITY",
        }
    ]
    assert all(z["level"] != "CLOSED" for z in busy["zones"])
    with pytest.raises(ValueError):
        prior.snapshot(-1)


def test_window_policy_forces_avoid_even_at_low_density():
    config = single_zone_config(peak_rate_pps=0.0, avoid_during_window=True)
    prior = CrowdPrior(config, line_graph(), {"plaza_landmark": (50.0, 0.0)})
    road = prior.snapshot(30 * 60)["roads"][0]
    assert road["level"] == "AVOID" and road["reason"] == "CONGESTION_WINDOW"
    assert prior.snapshot(0)["roads"] == []


def test_missing_anchor_is_reported_not_silently_placed():
    prior = CrowdPrior(single_zone_config(), line_graph(), {})
    snapshot = prior.snapshot(25 * 60)
    assert snapshot["unavailable_zones"] == [{"id": "plaza", "reason": "ANCHOR_NOT_IN_MAP"}]
    assert snapshot["zones"] == [] and snapshot["roads"] == []


def test_format_clock_wraps_day():
    assert format_clock(86_400 + 61) == "00:01:01"


def test_real_map_peak_marks_biryong_avoid_and_offpeak_is_clear():
    demo = DemoSession(wall_clock=lambda: 0.0)
    assert demo.crowd.unavailable == []
    assert demo.crowd_state()["roads"] == []
    road_ids = {road["road_id"] for road in demo.map["roads"]}
    assert len(road_ids) == len(demo.map["roads"])
    peak = demo.crowd.snapshot(25 * 60)
    levels = {(r["zone_id"], r["level"]) for r in peak["roads"]}
    assert ("biryong_plaza_front", "AVOID") in levels
    assert any(level == "CAUTION" for _, level in levels)
    assert {r["road_id"] for r in peak["roads"]} <= road_ids


def test_api_serves_crowd_and_unity_roads_with_matching_hash():
    client = TestClient(create_app())
    unity_map = client.get("/v1/demo/unity-map").json()
    roads = client.get("/v1/demo/unity-roads").json()
    crowd = client.get("/v1/demo/crowd").json()
    assert roads["geometry_hash"] == unity_map["geometry_hash"] == crowd["geometry_hash"]
    assert roads["coordinate_system"] == unity_map["coordinate_system"]
    web_roads = client.get("/v1/demo/map").json()["roads"]
    assert [r["road_id"] for r in roads["roads"]] == [r["road_id"] for r in web_roads]
    assert all(p["y"] == 0.0 for r in roads["roads"] for p in r["points"])
    assert crowd["source"] == "time_of_day_prior" and crowd["unit"] == "people/m2"
    # Shared vehicle state stays unchanged; crowd is a separate PC/web endpoint.
    assert "crowd" not in client.get("/v1/demo/state").json()


def test_crowd_follows_simulation_clock():
    wall = [0.0]
    demo = DemoSession(wall_clock=lambda: wall[0])
    demo.control(DemoControl(paused=False, speed=300))
    wall[0] += 5  # 1500 s -> 08:55
    demo.state()
    snapshot = demo.crowd_state()
    assert snapshot["time_of_day"] == "08:55:00"
    assert snapshot["roads"]


def test_time_override_moves_prior_only_and_cycles_through_peaks():
    prior = CrowdPrior(single_zone_config(), line_graph(), {"plaza_landmark": (50.0, 0.0)})
    assert prior.snapshot(0)["time_source"] == "configured_start"
    visited = []
    for _ in range(6):
        prior.next_peak(100)
        visited.append(prior.snapshot(100)["time_of_day"])
    assert visited == ["08:55:00", "10:25:00", "11:55:00", "13:25:00", "14:55:00", "08:31:40"]
    prior.set_time_of_day(100, "12:00")
    assert prior.snapshot(160)["time_of_day"] == "12:01:00"  # keeps advancing with sim time
    assert prior.snapshot(160)["time_override"] is True
    prior.reset_time()
    assert prior.snapshot(160)["time_of_day"] == "08:32:40"
    with pytest.raises(ValueError):
        prior.set_time_of_day(100, "24:00")


def test_crowd_time_api_does_not_touch_simulation_clock():
    client = TestClient(create_app())
    before = client.get("/v1/demo/state").json()["time_s"]
    peak = client.post("/v1/demo/crowd/time", json={"action": "next_peak"}).json()
    assert peak["time_of_day"] == "08:55:00" and peak["time_override"] and peak["roads"]
    assert client.get("/v1/demo/crowd").json()["time_of_day"] == "08:55:00"
    assert client.get("/v1/demo/state").json()["time_s"] == before
    assert (
        client.post("/v1/demo/crowd/time", json={"action": "set", "time_of_day": "13:25"}).json()[
            "time_of_day"
        ]
        == "13:25:00"
    )
    reset = client.post("/v1/demo/crowd/time", json={"action": "reset"}).json()
    assert reset["time_source"] == "configured_start" and reset["roads"] == []
    for body in ({"action": "set"}, {"action": "jump"}, {"action": "set", "time_of_day": "9:00"}):
        assert client.post("/v1/demo/crowd/time", json=body).status_code == 422
