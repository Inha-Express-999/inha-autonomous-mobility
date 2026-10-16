"""Merge only pinned OSM intervals into the Unity-scoped simulation map."""

from __future__ import annotations

import math
from collections import defaultdict

from campus_sim.map_coordinates import MapCoordinates, projection
from campus_sim.osm_roads import read_network
from campus_sim.unity_coverage import UnityCoverage

EXPANDED_OSM = "Docs/MapResearch/2026-09-17/expanded_campus.osm"
TARGETS = {"hitech", "dorm_1", "dorm_2", "dorm_3", "inha_station"}


def merge_intervals(intervals):
    """Union overlapping/touching pieces; retain real gaps between pieces."""
    merged = []
    for lo, hi, source in sorted(intervals, key=lambda x: (x[0], x[1])):
        if merged and lo <= merged[-1][1] + 1e-10:
            merged[-1][1] = max(hi, merged[-1][1])
            merged[-1][2].append(source)
        else:
            merged.append([lo, hi, [source]])
    return merged


def interval_point(original, raw_nodes, ratio):
    if ratio <= 1e-10:
        return "osm:" + original.u, raw_nodes[original.u]
    if ratio >= 1 - 1e-10:
        return "osm:" + original.v, raw_nodes[original.v]
    ratio = round(ratio, 12)
    xy = tuple(
        raw_nodes[original.u][i] + ratio * (raw_nodes[original.v][i] - raw_nodes[original.u][i])
        for i in (0, 1)
    )
    return f"clip:expanded:{original.way_id}:{original.index}:{ratio}", xy


def integrate_corridors(root, base_nodes, base_segments, policy):
    coordinates = MapCoordinates()
    raw_nodes, raw_segments, excluded = read_network(root / EXPANDED_OSM, coordinates)
    index = {segment.key: segment for segment in raw_segments}
    by_way = defaultdict(list)
    for segment in raw_segments:
        by_way[segment.way_id].append(segment)
    config = policy["extension_corridors"]
    if set(config["targets"]) != TARGETS:
        raise ValueError("extension policy must pin exactly the five requested landmarks")
    forbidden = set(config["excluded_osm_way_ids"])
    if "1215102704" not in forbidden:
        raise ValueError("station must exclude the Unity-unbacked shortcut 1215102704")
    intervals = defaultdict(list)
    # Normalize old clipped pieces onto their own expanded OSM segment. Matching
    # geometry never joins different ways or distinct OSM nodes at a crossing.
    for base in base_segments:
        way_id = base["osm_way_id"]
        if way_id in forbidden:
            continue
        a, b = base_nodes[base["u"]], base_nodes[base["v"]]
        matches = []
        for segment in by_way[way_id]:
            pa = projection(a, raw_nodes[segment.u], raw_nodes[segment.v])
            pb = projection(b, raw_nodes[segment.u], raw_nodes[segment.v])
            if max(pa[0], pb[0]) <= 0.003:
                matches.append((max(pa[0], pb[0]), segment.index, pa[1], pb[1], segment))
        if not matches:
            raise ValueError(f"core/expanded OSM disagreement: {way_id}; no guessed bridge")
        _, _, start, end, segment = min(matches, key=lambda x: (x[0], x[1]))
        lo, hi = sorted((start, end))
        if hi - lo > 1e-12:
            intervals[segment.key].append(
                (
                    lo,
                    hi,
                    {
                        "kind": "unity_core",
                        "unity_feature_id": base["unity_feature_id"],
                        "source_file": "Assets/InhaCampus/Source/campus.json",
                    },
                )
            )
    count = 0
    for target, corridor in config["targets"].items():
        if not corridor["segments"]:
            raise ValueError(f"empty extension corridor: {target}")
        if corridor["stop_way_id"] not in {p["osm_way_id"] for p in corridor["segments"]}:
            raise ValueError(f"Stop way outside pinned corridor: {target}")
        for piece in corridor["segments"]:
            key = piece["osm_way_id"], piece["segment_index"]
            if key[0] in forbidden or key not in index:
                raise ValueError(f"excluded/missing OSM corridor segment: {key}")
            segment = index[key]
            if segment.tags["highway"] not in policy["allowed_highways"]:
                raise ValueError(f"corridor class not enabled: {key}")
            lo, hi = piece["start_fraction"], piece["end_fraction"]
            if (
                any(type(v) not in (int, float) or not math.isfinite(v) for v in (lo, hi))
                or not 0 <= lo < hi <= 1
            ):
                raise ValueError("invalid corridor interval; no free-space geometry accepted")
            intervals[key].append(
                (
                    lo,
                    hi,
                    {"kind": "pinned_extension", "target": target, "source_file": EXPANDED_OSM},
                )
            )
            count += 1
    coverage = UnityCoverage(root, coordinates)
    nodes, segments = {}, []
    coverage_lengths = defaultdict(float)
    for key, pieces in sorted(intervals.items()):
        original = index[key]
        for part, (lo, hi, sources) in enumerate(merge_intervals(pieces)):
            u, a = interval_point(original, raw_nodes, lo)
            v, b = interval_point(original, raw_nodes, hi)
            length = math.dist(a, b)
            if length < 1e-6:
                continue
            supported = set()
            for sample in range(max(1, math.ceil(length / 2))):
                ratio = (sample + 0.5) / max(1, math.ceil(length / 2))
                xy = tuple(a[i] + ratio * (b[i] - a[i]) for i in (0, 1))
                source = next(
                    (
                        name
                        for name, ways in coverage.lines.items()
                        if any(
                            projection(xy, c, d)[0] <= 0.05
                            for c, d in ways.get(original.way_id, ())
                        )
                    ),
                    None,
                )
                if source is None:
                    raise ValueError(f"OSM corridor lacks Unity centerline support: {key}")
                supported.add(source)
                coverage_lengths[source] += length / max(1, math.ceil(length / 2))
            nodes[u], nodes[v] = a, b
            segments.append(
                {
                    "u": u,
                    "v": v,
                    "key": f"{original.way_id}:{original.index}:{part}",
                    "osm_way_id": original.way_id,
                    "unity_feature_id": None,
                    "osm_tags": original.tags,
                    "directions": original.directions,
                    "source_osm_file": EXPANDED_OSM,
                    "source_segment_index": original.index,
                    "source_fraction_interval": [lo, hi],
                    "source_pieces": sources,
                    "unity_centerline_sources": sorted(supported),
                }
            )
    return (
        nodes,
        segments,
        {
            "targets": sorted(TARGETS),
            "pinned_interval_count": count,
            "excluded_osm_way_ids": sorted(forbidden),
            "merged_segment_count": len(segments),
            "centerline_length_estimate_m_by_source": dict(coverage_lengths),
            "coverage_method": "same-way midpoint samples <=2m; 0.05m tolerance; not active mesh/height/access validation",
            "filtered_expanded_osm_ways": excluded,
        },
    )
