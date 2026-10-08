from __future__ import annotations

import csv
import json

import pytest
from build_m2_survey_batches import build_batches
from validate_osm_road_reviews import DEFAULT_CANDIDATES, DEFAULT_REVIEW


def _source() -> tuple[dict, list[dict[str, str]], list[str]]:
    candidates = json.loads(DEFAULT_CANDIDATES.read_text(encoding="utf-8"))
    with DEFAULT_REVIEW.open(encoding="utf-8-sig", newline="") as handle:
        reader = csv.DictReader(handle)
        return candidates, list(reader), reader.fieldnames or []


def test_survey_batches_cover_source_without_approving_edges() -> None:
    candidates, rows, columns = _source()
    report = build_batches(candidates, rows, columns)

    assert report["review_complete"] is False
    assert [batch["node_count"] for batch in report["batches"]] == [225, 4, 3]
    assert [batch["segment_count"] for batch in report["batches"]] == [292, 3, 2]
    assert sum(batch["segments_with_vertices_outside_query_bbox"]
               for batch in report["batches"]) == 61
    items = [item for batch in report["batches"] for item in batch["segments"]]
    assert len({item["candidate_segment_id"] for item in items}) == 297
    assert all(item["review_decision"] == "" and item["routable"] is False for item in items)


def test_survey_batches_reject_tampered_source_fields() -> None:
    candidates, rows, columns = _source()
    rows[0]["source_way_osm_id"] = "invented-way"

    with pytest.raises(ValueError, match="review source integrity failed"):
        build_batches(candidates, rows, columns)
