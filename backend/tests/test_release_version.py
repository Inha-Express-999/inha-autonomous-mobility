"""The integration release must report the same project version at every boundary."""

import re
import tomllib
from pathlib import Path

from campus_sim import __version__
from campus_sim.api import app
from campus_sim.map_data import default_map_dir, load_map_bundle

ROOT = Path(__file__).resolve().parents[2]


def test_release_versions_match_without_changing_independent_contract_versions():
    version = (ROOT / "VERSION").read_text(encoding="utf-8").strip()
    assert re.fullmatch(r"\d+\.\d+\.\d+\.\d+", version)
    package = tomllib.loads((ROOT / "backend/pyproject.toml").read_text(encoding="utf-8"))
    assert version == __version__ == app.version == package["project"]["version"]
    settings = (ROOT / "ProjectSettings/ProjectSettings.asset").read_text(encoding="utf-8")
    assert re.search(r"^  bundleVersion: (.+)$", settings, re.MULTILINE)[1] == version
    for name in ("README.md", "AGENTS.md", "Docs/implementation_status.md", "CHANGELOG.md"):
        assert version in (ROOT / name).read_text(encoding="utf-8")[:1000]
    bundle = load_map_bundle(default_map_dir(), source_root=ROOT)
    assert bundle.metadata["build_project_version"] == version
    assert bundle.metadata["schema_version"] == 2
    assert bundle.graph.map_version == "inha-osm-unity-v2"
    editor = (ROOT / "ProjectSettings/ProjectVersion.txt").read_text(encoding="utf-8")
    assert "m_EditorVersion: 6000.3.21f1\n" in editor
