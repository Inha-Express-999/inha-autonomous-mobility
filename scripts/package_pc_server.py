"""Copy the pinned local server next to a Windows Player; never rewrite source data."""

from __future__ import annotations

import argparse
import json
import shutil
import sys
from pathlib import Path


def package_server(
    root: Path, destination: Path, include_dependencies: bool = False
) -> dict:
    root, destination = root.resolve(), destination.resolve()
    if destination == root or root.is_relative_to(destination):
        raise ValueError("server bundle cannot overwrite the project or its parents")
    manifest = json.loads(
        (root / "maps/inha_osm_runtime/manifest.json").read_text(encoding="utf-8")
    )
    relative_files = {source["path"] for source in manifest["sources"].values()}
    relative_files.update({"backend/pyproject.toml", "backend/requirements-dev.lock"})
    program_extensions = {
        ".py",
        ".json",
        ".html",
        ".css",
        ".js",
        ".svg",
        ".png",
        ".woff2",
        ".txt",
    }
    for folder in (root / "backend/src/campus_sim", root / "maps/inha_osm_runtime"):
        relative_files.update(
            str(p.relative_to(root))
            for p in folder.rglob("*")
            if p.is_file()
            and "__pycache__" not in p.parts
            and p.suffix in program_extensions
        )
    for name in sorted(relative_files):
        source = (root / name).resolve()
        if not source.is_relative_to(root):
            raise ValueError("server source escapes project")
        target = destination / source.relative_to(root)
        target.parent.mkdir(parents=True, exist_ok=True)
        shutil.copyfile(source, target)
    dependencies = root / ".utmp/fifo-dijkstra-deps"
    bundled = include_dependencies and dependencies.is_dir()
    if bundled:
        shutil.copytree(
            dependencies, destination / ".utmp/fifo-dijkstra-deps", dirs_exist_ok=True
        )
    summary = {
        "project_version": (root / "VERSION").read_text(encoding="utf-8").strip(),
        "python_version": sys.version.split()[0],
        "local_dependencies_bundled": bundled,
        "python_runtime_bundled": False,
        "copied_server_files": len(relative_files),
    }
    (destination / "server-runtime.json").write_text(
        json.dumps(summary, indent=2) + "\n", encoding="utf-8", newline="\n"
    )
    return summary


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument(
        "--project-root", type=Path, default=Path(__file__).resolve().parents[1]
    )
    parser.add_argument("--destination", type=Path, required=True)
    parser.add_argument("--include-local-dependencies", action="store_true")
    args = parser.parse_args()
    print(
        json.dumps(
            package_server(
                args.project_root, args.destination, args.include_local_dependencies
            )
        )
    )


if __name__ == "__main__":
    main()
