"""Local Editor transaction: build/validate first, publish, reload only an idle server."""

from __future__ import annotations

import argparse
import json
import os
import tempfile
from pathlib import Path
from urllib.error import HTTPError, URLError
from urllib.parse import urlsplit
from urllib.request import Request, urlopen

from campus_sim.lanes import build_profiles
from campus_sim.map_builder import build_map, write_json
from campus_sim.map_data import load_map_bundle
from campus_sim.service import MobilityService
from campus_sim.service_locations import validate_locations
from campus_sim.unity_map import unity_map


def request_json(url, body=None):
    request = Request(
        url,
        data=None if body is None else json.dumps(body).encode(),
        headers={} if body is None else {"Content-Type": "application/json"},
    )
    with urlopen(request, timeout=10) as response:
        return json.load(response)


def normalized_locations(document, projection):
    normalized = {"schema_version": 1, "stops": [], "hubs": []}
    for kind in ("stops", "hubs"):
        by_id = {a["id"]: a for a in projection[kind]}
        normalized[kind] = [
            {
                "id": r["id"],
                "x": by_id[r["id"]]["position"]["x"],
                "z": by_id[r["id"]]["position"]["z"],
            }
            for r in document[kind]
        ]
    return normalized


def preview_locations(root, document):
    """Return final graph-projected anchors without publishing or resetting a server."""
    root = Path(root).resolve()
    validate_locations(document)
    with tempfile.TemporaryDirectory(prefix=".editor-preview-", dir=root / "maps") as working:
        stage = Path(working) / "candidate"
        build_map(root, stage, location_overrides=document)
        load_map_bundle(stage, source_root=root)
        projection = unity_map(MobilityService.from_osm_map(stage))
        return {
            "geometry_hash": projection["geometry_hash"],
            "locations": normalized_locations(document, projection),
        }


def apply_locations(
    root,
    document,
    server_url="http://127.0.0.1:8765",
    expected_hash=None,
    expected_preview_hash=None,
):
    root = Path(root).resolve()
    validate_locations(document)
    url = urlsplit(server_url)
    if (
        url.scheme != "http"
        or url.hostname not in ("127.0.0.1", "localhost", "::1")
        or url.path not in ("", "/")
        or url.query
        or url.fragment
        or url.username
    ):
        raise ValueError("Editor synchronization requires a loopback HTTP server")
    base = server_url.rstrip("/")
    server = None
    try:
        server = request_json(base + "/v1/desktop/info")
    except HTTPError as error:
        raise ValueError("Configured port belongs to an incompatible server") from error
    except URLError as error:
        # Only connection refusal means no server. Timeouts and other failures
        # are not permission to replace a potentially live service.
        import errno

        if getattr(error.reason, "errno", None) not in (errno.ECONNREFUSED, 10061):
            raise ValueError("Existing server could not be checked") from error
    if server and server.get("service_id") != "inha-mobil-local-demo":
        raise ValueError("Configured port belongs to an incompatible server")

    target = root / "maps/inha_osm_runtime"
    snapshot = root / "Assets/CampusSim/Data/server_map.json"
    config = root / "configs/service_locations.json"
    if (
        expected_hash
        and snapshot.exists()
        and json.loads(snapshot.read_text(encoding="utf-8"))["geometry_hash"] != expected_hash
    ):
        raise ValueError("Scene is stale: import the current server map before editing")
    lock = root / "configs/.service_locations.lock"
    descriptor = os.open(lock, os.O_CREAT | os.O_EXCL | os.O_WRONLY)
    os.close(descriptor)
    try:
        with tempfile.TemporaryDirectory(prefix=".editor-locations-", dir=target.parent) as working:
            working = Path(working)
            stage = working / "candidate"
            build_map(root, stage, location_overrides=document)
            load_map_bundle(stage, source_root=root)
            service = MobilityService.from_osm_map(stage)
            projection = unity_map(service)
            if expected_preview_hash and projection["geometry_hash"] != expected_preview_hash:
                raise ValueError(
                    "Road projection changed after Unity validation; retry location validation"
                )
            write_json(
                stage / "lane_profiles.json",
                build_profiles(service.graph, projection["geometry_hash"], root),
            )
            # Stable snapshots use the actual projected coordinates, not the
            # dragged point a few centimetres off the road centreline.
            normalized = normalized_locations(document, projection)
            backup = working / "previous-map"
            previous = {p: p.read_bytes() if p.exists() else None for p in (snapshot, config)}
            replaced = False
            try:
                if target.exists():
                    target.rename(backup)
                stage.rename(target)
                replaced = True
                write_json(snapshot, projection)
                write_json(config, normalized)
                if server and server["geometry_hash"] != projection["geometry_hash"]:
                    try:
                        acknowledgement = request_json(
                            base + "/v1/desktop/editor-map",
                            {
                                "expected_geometry_hash": server["geometry_hash"],
                                "geometry_hash": projection["geometry_hash"],
                            },
                        )
                    except HTTPError as error:
                        if error.code == 404:
                            raise ValueError(
                                "기존 서버가 편집 반영 API를 지원하지 않습니다. 서버를 재시작하세요."
                            ) from error
                        try:
                            detail = json.load(error).get("detail", str(error))
                        except (ValueError, AttributeError):
                            detail = str(error)
                        raise ValueError(f"서버 반영 거부: {detail}") from error
                    if acknowledgement.get("geometry_hash") != projection["geometry_hash"]:
                        raise ValueError("Server did not acknowledge the generated map")
            except Exception:
                if replaced:
                    target.rename(working / "rejected-map")
                if backup.exists():
                    backup.rename(target)
                for path, data in previous.items():
                    if data is None:
                        path.unlink(missing_ok=True)
                    else:
                        path.write_bytes(data)
                raise
            return {
                "geometry_hash": projection["geometry_hash"],
                "stops": len(projection["stops"]),
                "hubs": len(projection["hubs"]),
                "server_reloaded": bool(server),
                "locations": normalized,
            }
    finally:
        lock.unlink(missing_ok=True)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--project-root", type=Path, default=Path(__file__).resolve().parents[3])
    parser.add_argument("--input", type=Path, required=True)
    parser.add_argument("--result", type=Path, required=True)
    parser.add_argument("--server-url", default="http://127.0.0.1:8765")
    parser.add_argument("--preview", action="store_true")
    parser.add_argument("--expected-preview-hash")
    args = parser.parse_args()
    try:
        request = json.loads(args.input.read_text(encoding="utf-8"))
        document = {k: request[k] for k in ("schema_version", "stops", "hubs")}
        result = (
            preview_locations(args.project_root, document)
            if args.preview
            else apply_locations(
                args.project_root,
                document,
                args.server_url,
                request.get("base_geometry_hash"),
                args.expected_preview_hash,
            )
        )
        write_json(args.result, dict(success=True, **result))
    except Exception as error:
        write_json(args.result, {"success": False, "error": str(error)})
        raise SystemExit(1) from error


if __name__ == "__main__":
    main()
