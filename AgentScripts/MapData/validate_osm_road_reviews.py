from __future__ import annotations

import argparse
import csv
import json
import math
from datetime import date
from pathlib import Path
from typing import Any

ROOT = Path(__file__).resolve().parents[2]
DEFAULT_CANDIDATES = ROOT / "maps/candidates/inha-campus-osm-road-candidates.json"
DEFAULT_REVIEW = ROOT / "maps/candidates/inha-campus-osm-road-candidates-field-review.csv"
DECISIONS = {"", "KEEP_CANDIDATE", "REJECT", "NEEDS_MORE_EVIDENCE"}
REQUIRED_COLUMNS = {
    "candidate_segment_id",
    "source_sha256",
    "source_way_osm_id",
    "from_osm_node_id",
    "to_osm_node_id",
    "source_node_index_start",
    "source_node_index_end",
    "highway_tag",
    "oneway_tag",
    "access_tag",
    "vehicle_tag",
    "motor_vehicle_tag",
    "motorcar_tag",
    "bridge_tag",
    "tunnel_tag",
    "layer_tag",
    "surface_tag",
    "width_tag",
    "maxspeed_tag",
    "incline_tag",
    "geometry_wgs84_json",
    "verification_status",
    "routable",
    "review_decision",
    "observed_vehicle_access",
    "observed_direction",
    "measured_width_m",
    "measured_grade_percent",
    "surface_condition",
    "intersection_checked",
    "evidence_type",
    "evidence_reference",
    "reviewer",
    "reviewed_on",
    "notes",
}


def _number(value: str) -> float | None:
    try:
        number = float(value)
    except (TypeError, ValueError):
        return None
    return number if math.isfinite(number) else None


def _spreadsheet_safe(value: str) -> str:
    return "'" + value if value.startswith(("=", "+", "-", "@", "\t", "\r")) else value


def validate_review_rows(
    candidates: dict[str, Any],
    rows: list[dict[str, str]],
    columns: list[str] | None = None,
) -> dict[str, Any]:
    """Check review coverage/evidence; never approve or emit runtime graph edges."""
    issues: list[dict[str, str]] = []

    def issue(code: str, segment_id: str = "", detail: str = "") -> None:
        issues.append({"code": code, "segment_id": segment_id, "detail": detail})

    if candidates.get("data_status") != "CANDIDATE_ONLY":
        issue("candidate_dataset_not_candidate_only")
    if candidates.get("verification_status") != "UNVERIFIED":
        issue("candidate_dataset_not_unverified")

    if columns is not None:
        if len(columns) != len(set(columns)):
            issue("duplicate_csv_columns")
        for missing in sorted(REQUIRED_COLUMNS - set(columns)):
            issue("required_csv_column_missing", detail=missing)
        if None in columns:
            issue("malformed_csv_header")

    expected_hash = str(candidates.get("source", {}).get("sha256", ""))
    expected_ids = {
        segment["id"] for segment in candidates.get("candidate_topology", {}).get("segments", [])
    }
    segments_by_id = {
        segment["id"]: segment
        for segment in candidates.get("candidate_topology", {}).get("segments", [])
    }
    ways_by_source_id = {
        way["source_ref"]["id"]: way for way in candidates.get("ways", [])
    }
    seen: set[str] = set()
    count_open = count_keep = count_reject = count_more = 0
    eligible_for_graph_review: list[str] = []

    for index, row in enumerate(rows, start=2):
        segment_id = (row.get("candidate_segment_id") or "").strip()
        prefix = f"csv_row_{index}"
        if None in row:
            issue("extra_csv_fields", segment_id, prefix)
        if not segment_id:
            issue("segment_id_missing", detail=prefix)
            continue
        if segment_id not in expected_ids:
            issue("unknown_segment_id", segment_id, prefix)
            segment = None
        else:
            segment = segments_by_id[segment_id]
        if segment_id in seen:
            issue("duplicate_segment_id", segment_id, prefix)
        seen.add(segment_id)

        if (row.get("source_sha256") or "").strip() != expected_hash:
            issue("source_hash_mismatch", segment_id, prefix)
        if (row.get("verification_status") or "").strip() != "UNVERIFIED":
            issue("verification_status_must_remain_unverified", segment_id, prefix)
        if (row.get("routable") or "").strip().lower() != "false":
            issue("routable_must_remain_false", segment_id, prefix)

        if segment is not None:
            source_way_id = segment["source_way_ref"]["id"]
            way = ways_by_source_id.get(source_way_id)
            if way is None:
                issue("candidate_source_way_missing", segment_id, source_way_id)
                way = {"tags": {}}
            immutable_values = {
                "source_way_osm_id": source_way_id,
                "from_osm_node_id": segment["from_node_id"].removeprefix("osm-node-"),
                "to_osm_node_id": segment["to_node_id"].removeprefix("osm-node-"),
                "source_node_index_start": str(segment["source_node_index_range"][0]),
                "source_node_index_end": str(segment["source_node_index_range"][1]),
                "oneway_tag": way["tags"].get("oneway", ""),
            }
            tag_fields = (
                "highway", "access", "vehicle", "motor_vehicle", "motorcar", "bridge",
                "tunnel", "layer", "surface", "width", "maxspeed", "incline",
            )
            for tag in tag_fields:
                immutable_values[f"{tag}_tag"] = _spreadsheet_safe(
                    way["tags"].get(tag, "")
                )
            for field, expected in immutable_values.items():
                if (row.get(field) or "") != expected:
                    issue("source_field_mismatch", segment_id, field)
            expected_geometry = [
                [point["lon"], point["lat"]] for point in segment["geometry_wgs84"]
            ]
            try:
                actual_geometry = json.loads(row.get("geometry_wgs84_json") or "")
            except json.JSONDecodeError:
                actual_geometry = None
            if actual_geometry != expected_geometry:
                issue("source_geometry_mismatch", segment_id, prefix)

        decision = (row.get("review_decision") or "").strip().upper()
        if decision not in DECISIONS:
            issue("invalid_review_decision", segment_id, decision)
            continue
        if decision == "":
            count_open += 1
            continue
        evidence_reference = (row.get("evidence_reference") or "").strip()
        evidence_type = (row.get("evidence_type") or "").strip()
        reviewer = (row.get("reviewer") or "").strip()
        reviewed_on = (row.get("reviewed_on") or "").strip()
        notes = (row.get("notes") or "").strip()
        required_review_fields = {
            "evidence_type": evidence_type,
            "evidence_reference": evidence_reference,
            "reviewer": reviewer,
            "reviewed_on": reviewed_on,
            "notes": notes,
        }
        for field, value in required_review_fields.items():
            if not value:
                issue("review_field_required", segment_id, field)
        try:
            parsed_date = date.fromisoformat(reviewed_on)
            if parsed_date.isoformat() != reviewed_on:
                issue("reviewed_on_must_be_iso_date", segment_id, reviewed_on)
        except ValueError:
            if reviewed_on:
                issue("reviewed_on_must_be_iso_date", segment_id, reviewed_on)

        if decision == "REJECT":
            count_reject += 1
            continue
        if decision == "NEEDS_MORE_EVIDENCE":
            count_more += 1
            continue

        count_keep += 1
        keep_fields_valid = True

        def require(
            condition: bool, code: str, detail: str = "", current_segment_id: str = segment_id
        ) -> None:
            nonlocal keep_fields_valid
            if not condition:
                keep_fields_valid = False
                issue(code, current_segment_id, detail)

        access = (row.get("observed_vehicle_access") or "").strip().upper()
        direction = (row.get("observed_direction") or "").strip().upper()
        intersection_checked = (row.get("intersection_checked") or "").strip().upper()
        width = _number((row.get("measured_width_m") or "").strip())
        grade = _number((row.get("measured_grade_percent") or "").strip())
        source_tags = way.get("tags", {}) if segment is not None else {}
        access_keys = ("motor_vehicle", "motorcar", "vehicle", "access")
        explicit_denials = [
            key for key in access_keys if str(source_tags.get(key, "")).strip().lower() == "no"
        ]
        normalized_evidence = evidence_type.upper()
        require(access == "ALLOWED", "keep_requires_explicit_vehicle_access")
        require(direction in {"FORWARD", "REVERSE", "BIDIRECTIONAL"},
                "keep_requires_known_direction")
        require(width is not None and width > 0, "keep_requires_positive_measured_width")
        require(grade is not None, "keep_requires_finite_measured_grade")
        require(bool((row.get("surface_condition") or "").strip()),
                "keep_requires_surface_condition")
        require(intersection_checked == "YES", "keep_requires_intersection_review")
        require(not explicit_denials, "keep_conflicts_with_explicit_osm_access_denial",
                ",".join(explicit_denials))
        require("OFFICIAL" in normalized_evidence,
                "keep_requires_official_access_evidence")
        require("FIELD" in normalized_evidence or "MEASURED" in normalized_evidence,
                "keep_requires_field_measurement_evidence")
        if keep_fields_valid:
            eligible_for_graph_review.append(segment_id)

    for missing_id in sorted(expected_ids - seen):
        issue("candidate_segment_missing_from_review", missing_id)

    structurally_valid = not issues
    return {
        "report_type": "osm_field_review_validation",
        "source_sha256": expected_hash,
        "candidate_segment_count": len(expected_ids),
        "review_row_count": len(rows),
        "review_counts": {
            "open": count_open,
            "keep_candidate": count_keep,
            "reject": count_reject,
            "needs_more_evidence": count_more,
            "eligible_for_graph_review_only": len(eligible_for_graph_review),
        },
        "structurally_valid": structurally_valid,
        "review_complete": structurally_valid and count_open == 0 and count_more == 0,
        "issues": issues,
        "limitations": [
            "This report validates source integrity, review coverage, and recorded required fields only.",
            "KEEP_CANDIDATE means eligible for a separate graph review, not approved or routable.",
            "No RoadGraph edges are created and no candidate is made routable.",
            "Evidence authenticity and on-site conditions require human review.",
        ],
    }


def main() -> int:
    parser = argparse.ArgumentParser(
        description="Validate human OSM road review coverage without approving route edges."
    )
    parser.add_argument("--candidates", type=Path, default=DEFAULT_CANDIDATES)
    parser.add_argument("--review", type=Path, default=DEFAULT_REVIEW)
    parser.add_argument("--output", type=Path, help="Optional JSON report path")
    args = parser.parse_args()

    candidates = json.loads(args.candidates.read_text(encoding="utf-8"))
    with args.review.open(encoding="utf-8-sig", newline="") as handle:
        reader = csv.DictReader(handle)
        columns = reader.fieldnames or []
        rows = list(reader)
    report = validate_review_rows(candidates, rows, columns)
    serialized = json.dumps(report, ensure_ascii=False, indent=2) + "\n"
    if args.output:
        args.output.parent.mkdir(parents=True, exist_ok=True)
        args.output.write_text(serialized, encoding="utf-8")
    print(serialized, end="")
    if not report["structurally_valid"]:
        return 1
    if not report["review_complete"]:
        return 2
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
