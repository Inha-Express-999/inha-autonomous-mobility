from __future__ import annotations

import csv
import json
from pathlib import Path

from validate_osm_road_reviews import validate_review_rows

ROOT = Path(__file__).resolve().parents[2]


def candidate_data() -> dict:
    return {
        "data_status": "CANDIDATE_ONLY",
        "verification_status": "UNVERIFIED",
        "source": {"sha256": "source-hash"},
        "ways": [
            {"source_ref": {"id": "way-1"}, "tags": {"highway": "service", "oneway": "no"}}
        ],
        "candidate_topology": {
            "segments": [
                {
                    "id": segment_id,
                    "source_way_ref": {"id": "way-1"},
                    "from_node_id": "osm-node-1",
                    "to_node_id": "osm-node-2",
                    "source_node_index_range": [0, 1],
                    "geometry_wgs84": [
                        {"lon": 126.0, "lat": 37.0},
                        {"lon": 126.1, "lat": 37.1},
                    ],
                }
                for segment_id in ("segment-a", "segment-b")
            ]
        },
    }


def review_row(segment_id: str, **updates: str) -> dict[str, str]:
    row = {
        "candidate_segment_id": segment_id,
        "source_sha256": "source-hash",
        "source_way_osm_id": "way-1",
        "from_osm_node_id": "1",
        "to_osm_node_id": "2",
        "source_node_index_start": "0",
        "source_node_index_end": "1",
        "highway_tag": "service",
        "oneway_tag": "no",
        "access_tag": "",
        "vehicle_tag": "",
        "motor_vehicle_tag": "",
        "motorcar_tag": "",
        "bridge_tag": "",
        "tunnel_tag": "",
        "layer_tag": "",
        "surface_tag": "",
        "width_tag": "",
        "maxspeed_tag": "",
        "incline_tag": "",
        "geometry_wgs84_json": json.dumps([[126.0, 37.0], [126.1, 37.1]], separators=(",", ":")),
        "verification_status": "UNVERIFIED",
        "routable": "false",
        "review_decision": "",
        "observed_vehicle_access": "",
        "observed_direction": "",
        "measured_width_m": "",
        "measured_grade_percent": "",
        "surface_condition": "",
        "intersection_checked": "",
        "evidence_type": "",
        "evidence_reference": "",
        "reviewer": "",
        "reviewed_on": "",
        "notes": "",
    }
    row.update(updates)
    return row


def test_blank_template_is_well_formed_but_not_complete() -> None:
    report = validate_review_rows(
        candidate_data(),
        [review_row("segment-a"), review_row("segment-b")],
    )

    assert report["structurally_valid"] is True
    assert report["review_complete"] is False
    assert report["review_counts"]["open"] == 2
    assert report["review_counts"]["eligible_for_graph_review_only"] == 0


def test_keep_candidate_requires_complete_evidence_but_never_promotes_edge() -> None:
    keep = review_row(
        "segment-a",
        review_decision="KEEP_CANDIDATE",
        observed_vehicle_access="ALLOWED",
        observed_direction="BIDIRECTIONAL",
        measured_width_m="3.2",
        measured_grade_percent="4.5",
        surface_condition="paved, inspected dry",
        intersection_checked="YES",
        evidence_type="OFFICIAL_AND_FIELD",
        evidence_reference="survey-2026-09-25#segment-a",
        reviewer="reviewer-1",
        reviewed_on="2026-09-25",
        notes="Evidence captured; separate graph review still required.",
    )
    report = validate_review_rows(
        candidate_data(), [keep, review_row("segment-b", review_decision="REJECT",
            evidence_type="OFFICIAL_SOURCE", evidence_reference="policy-doc#4",
            reviewer="reviewer-1", reviewed_on="2026-09-25", notes="Vehicle access prohibited.")]
    )

    assert report["structurally_valid"] is True
    assert report["review_complete"] is True
    assert report["review_counts"]["eligible_for_graph_review_only"] == 1
    assert not any("routable" in key for key in report)
    assert any("No RoadGraph edges are created" in item for item in report["limitations"])


def test_source_mismatch_tampering_duplicates_and_missing_ids_are_rejected() -> None:
    report = validate_review_rows(
        candidate_data(),
        [
            review_row("segment-a", source_sha256="old-hash", routable="true",
                source_way_osm_id="edited-way", geometry_wgs84_json="not-json"),
            review_row("segment-a"),
            review_row("unknown-segment"),
        ],
    )

    codes = {item["code"] for item in report["issues"]}
    assert report["structurally_valid"] is False
    assert {"source_hash_mismatch", "routable_must_remain_false", "duplicate_segment_id",
            "unknown_segment_id", "candidate_segment_missing_from_review",
            "source_field_mismatch", "source_geometry_mismatch"} <= codes


def test_keep_candidate_rejects_unknown_permission_nonfinite_measurements() -> None:
    keep = review_row(
        "segment-a",
        review_decision="KEEP_CANDIDATE",
        observed_vehicle_access="UNKNOWN",
        observed_direction="UNKNOWN",
        measured_width_m="NaN",
        measured_grade_percent="Infinity",
        surface_condition="unknown",
        intersection_checked="NO",
        evidence_type="PHOTO",
        evidence_reference="photo-set#1",
        reviewer="reviewer-1",
        reviewed_on="2026-09-25",
        notes="Insufficient access/direction/measurement evidence.",
    )
    report = validate_review_rows(candidate_data(), [keep, review_row("segment-b")])
    codes = {item["code"] for item in report["issues"]}

    assert report["structurally_valid"] is False
    assert "keep_requires_explicit_vehicle_access" in codes
    assert "keep_requires_known_direction" in codes
    assert "keep_requires_positive_measured_width" in codes
    assert "keep_requires_finite_measured_grade" in codes
    assert "keep_requires_intersection_review" in codes
    assert "keep_requires_official_access_evidence" in codes
    assert "keep_requires_field_measurement_evidence" in codes


def test_keep_candidate_cannot_override_explicit_osm_access_denial() -> None:
    candidates = candidate_data()
    candidates["ways"][0]["tags"]["motor_vehicle"] = "no"
    keep = review_row(
        "segment-a",
        review_decision="KEEP_CANDIDATE",
        observed_vehicle_access="ALLOWED",
        observed_direction="BIDIRECTIONAL",
        measured_width_m="3.2",
        measured_grade_percent="4.5",
        surface_condition="paved, inspected dry",
        intersection_checked="YES",
        evidence_type="OFFICIAL_AND_FIELD",
        evidence_reference="survey-2026-09-25#segment-a",
        reviewer="reviewer-1",
        reviewed_on="2026-09-25",
        notes="OSM states motor_vehicle=no; it requires independent resolution before candidate review.",
    )
    report = validate_review_rows(
        candidates, [keep, review_row("segment-b", review_decision="REJECT",
            evidence_type="OFFICIAL_SOURCE", evidence_reference="policy-doc#4",
            reviewer="reviewer-1", reviewed_on="2026-09-25", notes="Excluded from vehicle routing.")]
    )

    assert report["structurally_valid"] is False
    denial_issues = [
        issue for issue in report["issues"]
        if issue["code"] == "keep_conflicts_with_explicit_osm_access_denial"
    ]
    assert denial_issues == [{
        "code": "keep_conflicts_with_explicit_osm_access_denial",
        "segment_id": "segment-a",
        "detail": "motor_vehicle",
    }]
    assert report["review_counts"]["eligible_for_graph_review_only"] == 0


def test_review_date_requires_canonical_iso_format() -> None:
    keep = review_row(
        "segment-a",
        review_decision="REJECT",
        evidence_type="OFFICIAL_SOURCE",
        evidence_reference="policy-doc#4",
        reviewer="reviewer-1",
        reviewed_on="20260925",
        notes="Vehicle access prohibited.",
    )

    report = validate_review_rows(candidate_data(), [keep, review_row("segment-b")])

    assert "reviewed_on_must_be_iso_date" in {item["code"] for item in report["issues"]}


def test_checked_in_campus_review_data_stays_candidate_only_until_human_review() -> None:
    candidates = json.loads(
        (ROOT / "maps/candidates/inha-campus-osm-road-candidates.json").read_text(
            encoding="utf-8"
        )
    )
    with (ROOT / "maps/candidates/inha-campus-osm-road-candidates-field-review.csv").open(
        encoding="utf-8-sig", newline=""
    ) as handle:
        rows = list(csv.DictReader(handle))

    report = validate_review_rows(candidates, rows)

    candidate_count = candidates["candidate_topology"]["candidate_segment_count"]
    assert report["structurally_valid"] is True
    assert report["review_complete"] is False
    assert report["candidate_segment_count"] == candidate_count
    assert report["review_counts"]["open"] == candidate_count
    assert report["review_counts"]["eligible_for_graph_review_only"] == 0
    assert candidates["data_status"] == "CANDIDATE_ONLY"
    assert candidates["verification_status"] == "UNVERIFIED"
    assert all(segment["verification_status"] == "UNVERIFIED"
               and segment["routable"] is False
               for segment in candidates["candidate_topology"]["segments"])
