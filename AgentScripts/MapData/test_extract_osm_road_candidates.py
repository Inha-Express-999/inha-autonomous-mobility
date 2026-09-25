from __future__ import annotations

import csv
import io
from pathlib import Path

from extract_osm_road_candidates import build_dataset, review_template_csv

ROOT = Path(__file__).resolve().parents[2]


def test_field_review_template_covers_every_candidate_without_approval() -> None:
    dataset = build_dataset(ROOT / "Assets/InhaCampus/Source/campus.osm")
    rows = list(csv.DictReader(io.StringIO(review_template_csv(dataset))))

    assert len(rows) == dataset["candidate_topology"]["candidate_segment_count"]
    assert len({row["candidate_segment_id"] for row in rows}) == len(rows)
    assert {row["source_sha256"] for row in rows} == {dataset["source"]["sha256"]}
    assert all(row["verification_status"] == "UNVERIFIED" for row in rows)
    assert all(row["routable"] == "false" for row in rows)
    assert all(row["review_decision"] == "" for row in rows)
    assert all(row["evidence_reference"] == "" for row in rows)
    assert all(row["geometry_wgs84_json"].startswith("[[") for row in rows)


def test_review_template_neutralizes_formula_like_external_tags() -> None:
    dataset = {
        "source": {"sha256": "abc123"},
        "ways": [
            {
                "id": "osm-way-1",
                "source_ref": {"id": "1"},
                "tags": {"highway": "service", "surface": "=HYPERLINK(1)"},
            }
        ],
        "candidate_topology": {
            "segments": [
                {
                    "id": "segment-1",
                    "source_way_ref": {"id": "1"},
                    "from_node_id": "osm-node-2",
                    "to_node_id": "osm-node-3",
                    "source_node_index_range": [0, 1],
                    "geometry_wgs84": [
                        {"lon": 126.0, "lat": 37.0},
                        {"lon": 126.1, "lat": 37.1},
                    ],
                }
            ]
        },
    }

    row = next(csv.DictReader(io.StringIO(review_template_csv(dataset))))

    assert row["surface_tag"] == "'=HYPERLINK(1)"
    assert row["routable"] == "false"
