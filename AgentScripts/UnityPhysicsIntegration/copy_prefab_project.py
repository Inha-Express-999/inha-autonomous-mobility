"""Copy current source and prefab GUID dependencies into a disposable test project."""
import argparse
import hashlib
import json
import re
import shutil
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
GUID = re.compile(rb"guid:\s*([0-9a-f]{32})")
TEXT_ASSETS = {".prefab", ".mat", ".asset", ".unity", ".controller", ".overridecontroller"}


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("destination", type=Path)
    args = parser.parse_args()
    target = args.destination.resolve()
    if not target.is_relative_to((ROOT / "tmp").resolve()) or target == ROOT / "tmp":
        raise ValueError("Destination must be a dedicated directory under repository tmp/")
    hashes = {}

    def copy(path):
        relative = path.relative_to(ROOT)
        output = target / relative
        output.parent.mkdir(parents=True, exist_ok=True)
        shutil.copyfile(path, output)
        hashes[relative.as_posix()] = hashlib.sha256(path.read_bytes()).hexdigest()

    assets = {}
    for meta in (ROOT / "Assets").rglob("*.meta"):
        match = GUID.search(meta.read_bytes())
        if match:
            assets[match[1].decode()] = meta.with_suffix("")

    seeds = [* (ROOT / "Assets/CampusSim/Prefabs/Vehicles").glob("*.prefab"),
             ROOT / "Assets/CampusSim/Prefabs/Navigation/VehicleRouteLine.prefab"]
    pending, visited, external = list(seeds), set(), set()
    while pending:
        path = pending.pop()
        if path in visited or not path.is_file():
            continue
        visited.add(path)
        copy(path)
        meta = Path(str(path) + ".meta")
        content = b""
        if meta.is_file():
            copy(meta)
            content += meta.read_bytes()
        if path.suffix.lower() in TEXT_ASSETS:
            content += path.read_bytes()
        for raw in GUID.findall(content):
            guid = raw.decode()
            if guid.startswith("0000000000000000"):
                continue  # Unity built-in resource.
            dependency = assets.get(guid)
            if dependency is None:
                external.add(guid)  # Package references are resolved by Unity's package manager.
            elif dependency not in visited:
                pending.append(dependency)

    for path in (ROOT / "Assets/CampusSim/Scripts").rglob("*"):
        if path.is_file():
            copy(path)
    for name in ("VehicleActorSpawnerTests.cs", "VehicleActorSpawnerTests.cs.meta"):
        copy(ROOT / "Assets/CampusSim/Tests/EditMode" / name)
    for name in ("ProjectVersion.txt", "TagManager.asset"):
        copy(ROOT / "ProjectSettings" / name)
    (target / "source-manifest.json").write_text(json.dumps({
        "scope": "original vehicle and route prefab dependencies with current runtime source",
        "sources": hashes, "package_or_unresolved_guids": sorted(external),
    }, indent=2), encoding="utf-8")
    print(f"Copied {len(hashes)} files; {len(external)} external/package GUID references")


if __name__ == "__main__":
    main()
