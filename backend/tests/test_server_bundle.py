import importlib.util
from pathlib import Path

import pytest

from campus_sim.map_data import load_map_bundle

ROOT = Path(__file__).resolve().parents[2]
SPEC = importlib.util.spec_from_file_location(
    "package_pc_server", ROOT / "scripts/package_pc_server.py"
)
PACKAGER = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(PACKAGER)


def test_player_sidecar_keeps_all_pinned_source_hashes_and_web_assets(tmp_path):
    destination = tmp_path / "CampusServer"
    summary = PACKAGER.package_server(ROOT, destination)
    bundle = load_map_bundle(destination / "maps/inha_osm_runtime", source_root=destination)
    assert len(bundle.graph.nodes) == 187
    assert bundle.metadata["source_hashes_checked"]
    assert (destination / "backend/src/campus_sim/desktop_server.py").is_file()
    assert (destination / "backend/src/campus_sim/web/index.html").is_file()
    assert (destination / "backend/src/campus_sim/web/fonts/OFL.txt").is_file()
    assert summary["python_runtime_bundled"] is False
    assert summary["local_dependencies_bundled"] is False


def test_packager_refuses_to_overwrite_project_or_parent():
    for destination in (ROOT, ROOT.parent):
        with pytest.raises(ValueError, match="overwrite"):
            PACKAGER.package_server(ROOT, destination)
