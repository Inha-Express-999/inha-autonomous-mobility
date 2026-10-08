from __future__ import annotations

import json
import sys
from pathlib import Path
from unittest.mock import patch

import pytest

from campus_sim.cli import main
from campus_sim.map_validation import inspect_map

ROOT = Path(__file__).resolve().parents[2]


def test_synthetic_graph_is_structurally_valid_but_not_mvp_ready() -> None:
    report = inspect_map(ROOT / "maps/fixtures/campus-synthetic-6.json")
    assert report["road_graph_contract_valid"] is True
    assert report["mvp_map_ready"] is False
    assert report["node_count"] == 6
    assert report["stop_node_count"] == 6
    assert report["weak_component_sizes_desc"] == [6]


def test_osm_candidates_cannot_pass_map_gate() -> None:
    report = inspect_map(ROOT / "maps/candidates/inha-campus-osm-road-candidates.json")
    assert report["road_graph_contract_valid"] is False
    assert report["mvp_map_ready"] is False
    assert report["reason"] == "candidate_only_not_routable"


def test_validate_map_cli_requires_explicit_synthetic_opt_in(capsys) -> None:
    graph = ROOT / "maps/fixtures/campus-synthetic-6.json"
    with (
        patch.object(sys, "argv", ["campus-sim", "validate-map", "--map", str(graph)]),
        pytest.raises(SystemExit) as exit_result,
    ):
        main()
    assert exit_result.value.code == 2
    assert json.loads(capsys.readouterr().out)["mvp_map_ready"] is False

    with patch.object(
        sys, "argv", ["campus-sim", "validate-map", "--map", str(graph), "--allow-synthetic"]
    ):
        main()
    assert json.loads(capsys.readouterr().out)["road_graph_contract_valid"] is True


def test_self_declared_verified_status_is_not_accepted(tmp_path) -> None:
    graph = json.loads((ROOT / "maps/fixtures/campus-synthetic-6.json").read_text(encoding="utf-8"))
    graph["data_status"] = "VERIFIED"
    path = tmp_path / "self-declared.json"
    path.write_text(json.dumps(graph), encoding="utf-8")
    report = inspect_map(path)
    assert report["mvp_map_ready"] is False
    assert report["reason"] == "unsupported_or_unverified_map_status"
