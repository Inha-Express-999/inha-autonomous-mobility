"""Group unapproved OSM road candidates into reproducible field-survey batches.

This is a review aid only. It never creates RoadGraph edges or changes routability.
"""

from __future__ import annotations

import argparse
import csv
import json
from collections import Counter
from pathlib import Path

from validate_osm_road_reviews import (
    DEFAULT_CANDIDATES,
    DEFAULT_REVIEW,
    validate_review_rows,
)


def build_batches(candidates: dict, rows: list[dict[str, str]], columns: list[str]) -> dict:
    validation = validate_review_rows(candidates, rows, columns)
    if not validation["structurally_valid"]:
        raise ValueError(f"review source integrity failed: {validation['issues'][:3]}")

    topology = candidates["candidate_topology"]
    nodes = {node["id"]: node for node in topology["nodes"]}
    segments = topology["segments"]
    parent = {node_id: node_id for node_id in nodes}

    def find(node_id: str) -> str:
        while parent[node_id] != node_id:
            parent[node_id] = parent[parent[node_id]]
            node_id = parent[node_id]
        return node_id

    for segment in segments:
        start, end = segment["from_node_id"], segment["to_node_id"]
        parent[find(end)] = find(start)

    components: dict[str, set[str]] = {}
    for node_id in nodes:
        components.setdefault(find(node_id), set()).add(node_id)
    ordered = sorted(components.values(), key=lambda group: (-len(group), min(group)))
    bbox = candidates["source"]["query_bbox_wgs84"]
    review_by_id = {row["candidate_segment_id"]: row for row in rows}
    batches = []
    for index, group in enumerate(ordered, start=1):
        component_id = f"C{index:02d}"
        items = []
        for segment in segments:
            if segment["from_node_id"] not in group:
                continue
            outside = sum(
                not (bbox["west_lon"] <= point["lon"] <= bbox["east_lon"]
                     and bbox["south_lat"] <= point["lat"] <= bbox["north_lat"])
                for point in segment["geometry_wgs84"]
            )
            review = review_by_id[segment["id"]]
            items.append({
                "component_id": component_id,
                "candidate_segment_id": segment["id"],
                "source_way_osm_id": segment["source_way_ref"]["id"],
                "from_osm_node_id": segment["from_node_id"],
                "to_osm_node_id": segment["to_node_id"],
                "outside_query_bbox_vertices": outside,
                "review_decision": (review.get("review_decision") or "").strip().upper(),
                "routable": False,
            })
        items.sort(key=lambda item: item["candidate_segment_id"])
        counts = Counter(item["review_decision"] or "OPEN" for item in items)
        batches.append({
            "component_id": component_id,
            "node_count": len(group),
            "segment_count": len(items),
            "review_counts": dict(sorted(counts.items())),
            "segments_with_vertices_outside_query_bbox": sum(
                item["outside_query_bbox_vertices"] > 0 for item in items
            ),
            "segments": items,
        })

    if sum(batch["segment_count"] for batch in batches) != len(segments):
        raise ValueError("candidate segments are not fully covered by topology batches")
    return {
        "report_type": "m2_osm_field_survey_batches",
        "source_sha256": validation["source_sha256"],
        "review_complete": validation["review_complete"],
        "candidate_node_count": len(nodes),
        "candidate_segment_count": len(segments),
        "component_count": len(batches),
        "batches": batches,
        "limitation": (
            "Candidate topology and survey ordering only; "
            "no segment is approved or routable."
        ),
    }


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--candidates", type=Path, default=DEFAULT_CANDIDATES)
    parser.add_argument("--review", type=Path, default=DEFAULT_REVIEW)
    parser.add_argument("--output", type=Path, help="Write the full JSON survey worklist")
    args = parser.parse_args()
    candidates = json.loads(args.candidates.read_text(encoding="utf-8"))
    with args.review.open(encoding="utf-8-sig", newline="") as handle:
        reader = csv.DictReader(handle)
        report = build_batches(candidates, list(reader), reader.fieldnames or [])
    if args.output:
        args.output.parent.mkdir(parents=True, exist_ok=True)
        args.output.write_text(
            json.dumps(report, ensure_ascii=False, indent=2) + "\n", encoding="utf-8"
        )
    summary = {key: value for key, value in report.items() if key != "batches"}
    summary["batches"] = [
        {key: value for key, value in batch.items() if key != "segments"}
        for batch in report["batches"]
    ]
    print(json.dumps(summary, ensure_ascii=False, indent=2))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
