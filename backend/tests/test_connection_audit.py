from pathlib import Path

import pytest

from campus_sim.connection_audit import (
    audit_connections,
    nearest_ways,
    query_graph,
)
from campus_sim.map_builder import digest
from campus_sim.osm_roads import Segment
from campus_sim.planning import NoPath

ROOT = Path(__file__).resolve().parents[2]


def point(segment, ratio, nodes):
    xy = tuple(
        nodes[segment.u][i] + ratio * (nodes[segment.v][i] - nodes[segment.u][i]) for i in (0, 1)
    )
    return 0.0, segment.index, ratio, xy, segment


def test_oneway_projection_does_not_invent_return_edge():
    nodes = {"A": (0.0, 0.0), "B": (10.0, 0.0)}
    segment = Segment("road", 0, "A", "B", {"highway": "service"}, (1,))
    _, outward, returning = query_graph(
        nodes, [segment], point(segment, 0.2, nodes), point(segment, 0.8, nodes), 30 / 3.6
    )
    assert outward.length_m == pytest.approx(6)
    assert isinstance(returning, NoPath)


def test_projection_on_same_segment_preserves_distance_and_lower_speed_limit():
    nodes = {"A": (0.0, 0.0), "B": (10.0, 0.0)}
    segment = Segment("road", 0, "A", "B", {"highway": "service", "maxspeed": "5"}, (1, -1))
    _, outward, returning = query_graph(
        nodes, [segment], point(segment, 0.25, nodes), point(segment, 0.75, nodes), 30 / 3.6
    )
    assert outward.length_m == returning.length_m == 5
    assert outward.cost_s == pytest.approx(5 / (5 / 3.6))


def test_crossing_lines_with_distinct_osm_nodes_stay_disconnected():
    nodes = {"A": (0.0, 0.0), "B": (10.0, 0.0), "C": (5.0, -5.0), "D": (5.0, 5.0)}
    a = Segment("horizontal", 0, "A", "B", {"highway": "service"}, (1, -1))
    b = Segment("vertical", 0, "C", "D", {"highway": "service"}, (1, -1))
    _, outward, returning = query_graph(nodes, [a, b], point(a, 0, nodes), point(b, 1, nodes), 2)
    assert isinstance(outward, NoPath) and isinstance(returning, NoPath)


def test_nearest_candidates_are_distinct_ways_not_repeated_segments():
    nodes = {"A": (0.0, 0.0), "B": (1.0, 0.0), "C": (2.0, 0.0), "D": (0.0, 1.0)}
    segments = [
        Segment("same", 0, "A", "B", {}, (1, -1)),
        Segment("same", 1, "B", "C", {}, (1, -1)),
        Segment("other", 0, "A", "D", {}, (1, -1)),
    ]
    candidates = nearest_ways((0, 0), nodes, segments)
    assert {c[4].way_id for c in candidates} == {"same", "other"}
    assert len(candidates) == 2


@pytest.fixture(scope="module")
def report():
    before = digest(ROOT / "maps/inha_osm_runtime/graph.json")
    result = audit_connections(ROOT)
    assert digest(ROOT / "maps/inha_osm_runtime/graph.json") == before
    return result


def test_five_candidates_have_separate_graph_routes_and_no_service_approval(report):
    assert report["speed_assumption_kmh"] == pytest.approx(30)
    assert report["runtime_geometry_changed"] is False
    assert len(report["targets"]) == 5
    for target in report["targets"]:
        assert target["operationally_verified"] is False
        best = target["best_candidate"]
        assert best is not None and best["within_stop_gap"]
        assert best["outward"]["reachable"] and best["return"]["reachable"]
        assert best["outward_edge_ids"] and best["return_edge_ids"]
    modes = {t["landmark_id"]: t["best_candidate"]["diagnostic_mode"] for t in report["targets"]}
    assert modes["hitech"] == "service_only"
    assert all(modes[name] == "including_arterials" for name in modes if name != "hitech")


def test_dorm1_old_exit_cannot_be_misreported_as_round_trip(report):
    dorm = next(t for t in report["targets"] if t["landmark_id"] == "dorm_1")
    exit_road = next(c for c in dorm["modes"]["service_only"] if c["osm_way_id"] == "1098489687")
    assert exit_road["tags"]["oneway"] == "yes"
    assert exit_road["outward"]["reachable"] and not exit_road["return"]["reachable"]
    assert not exit_road["round_trip"]


def test_station_return_has_osm_segment_missing_from_unity_exports(report):
    station = next(t for t in report["targets"] if t["landmark_id"] == "inha_station")
    missing = station["best_candidate"]["return_unity_coverage"]
    assert missing["missing_way_ids"] == ["1215102704"]
    assert missing["length_estimate_m_by_source"]["not_in_unity_exports"] > 70
    alternate = station["best_candidate"]["unity_export_backed_alternative"]
    assert alternate is not None
    assert "1215102704" in alternate["excluded_uncovered_way_ids"]
    assert not alternate["return_unity_coverage"]["missing_way_ids"]
    assert alternate["return"]["length_m"] > station["best_candidate"]["return"]["length_m"]


def test_hitech_break_is_old_crop_not_absent_osm_road(report):
    hitech = next(t for t in report["targets"] if t["landmark_id"] == "hitech")
    best = hitech["best_candidate"]
    assert best["osm_way_id"] == "481241714"
    assert "481241715" in best["outward"]["osm_way_sequence"]
    assert best["outward_unity_coverage"]["length_estimate_m_by_source"]["extension_export"] > 60
    assert not best["outward_unity_coverage"]["missing_way_ids"]
