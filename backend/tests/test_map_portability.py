"""Fresh checkouts must preserve map integrity across Git newline conversions."""

import json
from pathlib import Path
from shutil import copytree

import pytest

from campus_sim.map_builder import SOURCE_FILES, build_map, map_text_digest
from campus_sim.map_data import PAYLOAD_FILES, default_map_dir, load_map_bundle

ROOT = Path(__file__).resolve().parents[2]


def copy_inputs(root, newline):
    inputs = [*SOURCE_FILES.values(), "configs/service_locations.json"]
    for name in inputs:
        destination = root / name
        destination.parent.mkdir(parents=True, exist_ok=True)
        destination.write_text(
            (ROOT / name).read_text(encoding="utf-8"), encoding="utf-8", newline=newline
        )


@pytest.mark.parametrize("newline", ["\n", "\r\n"])
def test_source_and_payload_checkout_newlines_load_and_rebuild_identically(tmp_path, newline):
    root, output = tmp_path / "checkout", tmp_path / "rebuilt"
    copy_inputs(root, newline)
    checked_out = tmp_path / "map"
    copytree(default_map_dir(), checked_out)
    for path in checked_out.iterdir():
        path.write_text(path.read_text(encoding="utf-8"), encoding="utf-8", newline=newline)

    bundle = load_map_bundle(checked_out, source_root=root)
    assert len(bundle.graph.nodes) == 188
    assert len(bundle.graph.edges) == 404
    assert bundle.metadata["source_hashes_checked"]
    build_map(root, output)
    for name in PAYLOAD_FILES | {"manifest.json"}:
        assert (output / name).read_bytes() == (default_map_dir() / name).read_bytes()
        assert b"\r" not in (output / name).read_bytes()


@pytest.mark.parametrize("target", ["source", "payload"])
def test_real_content_change_still_fails_after_newline_conversion(tmp_path, target):
    root, copied = tmp_path / "checkout", tmp_path / "map"
    copy_inputs(root, "\r\n")
    copytree(default_map_dir(), copied)
    path = root / SOURCE_FILES["policy"] if target == "source" else copied / "policy.json"
    policy = json.loads(path.read_text(encoding="utf-8"))
    policy["default_speed_mps"] += 1
    path.write_text(json.dumps(policy), encoding="utf-8", newline="\r\n")
    with pytest.raises(ValueError, match=f"{target} hash mismatch"):
        load_map_bundle(copied, source_root=root)


@pytest.mark.parametrize(
    "fields", [{"schema_version": 1}, {"hash_format": "sha256"}, {"hash_format": None}]
)
def test_legacy_or_unknown_hash_format_requires_explicit_rebuild(tmp_path, fields):
    copied = tmp_path / "map"
    copytree(default_map_dir(), copied)
    path = copied / "manifest.json"
    manifest = json.loads(path.read_text(encoding="utf-8"))
    manifest.update(fields)
    path.write_text(json.dumps(manifest), encoding="utf-8")
    with pytest.raises(ValueError, match="rebuild map bundle"):
        load_map_bundle(copied)


def test_hash_preserves_whitespace_and_bom_and_rejects_invalid_utf8(tmp_path):
    path = tmp_path / "text"
    path.write_bytes(b"a\nb\n")
    expected = map_text_digest(path)
    for equivalent in (b"a\r\nb\r\n", b"a\rb\r"):
        path.write_bytes(equivalent)
        assert map_text_digest(path) == expected
    for changed in (b"a \nb\n", b"a\nb", b"\xef\xbb\xbfa\nb\n"):
        path.write_bytes(changed)
        assert map_text_digest(path) != expected
    path.write_bytes(b"\xff")
    with pytest.raises(UnicodeDecodeError):
        map_text_digest(path)
