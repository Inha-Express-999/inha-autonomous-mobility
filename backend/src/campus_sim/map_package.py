"""Integrity gate for a proposed real-map review package.

Passing this gate proves file identity and declared versions only. It does not
approve access, geometry, accessibility, or runtime routing.
"""

from __future__ import annotations

import json
from datetime import date
from hashlib import sha256
from itertools import combinations
from math import hypot, isclose, isfinite
from pathlib import Path
from typing import Literal

from pydantic import BaseModel, ConfigDict, Field, ValidationError, field_validator
from pyproj import CRS, Transformer
from pyproj.exceptions import CRSError, ProjError

from campus_sim.map_review_content import Point, validate_review_content

ARTIFACT_NAMES = frozenset({
    "vehicle_graph", "pedestrian_graph", "landmarks", "stops", "zones",
})


class Wgs84Origin(BaseModel):
    model_config = ConfigDict(extra="forbid", frozen=True)

    lon: float = Field(ge=-180, le=180, allow_inf_nan=False)
    lat: float = Field(ge=-90, le=90, allow_inf_nan=False)


class HashedFile(BaseModel):
    model_config = ConfigDict(extra="forbid", frozen=True)

    path: str = Field(min_length=1)
    sha256: str = Field(pattern=r"^[0-9a-f]{64}$")

    @field_validator("path")
    @classmethod
    def require_package_relative_path(cls, value: str) -> str:
        path = Path(value)
        if path.is_absolute() or ".." in path.parts or path.drive or not path.name:
            raise ValueError("file path must stay inside the map package")
        return value


class SourceFile(HashedFile):
    retrieved_on: date
    source_description: str = Field(min_length=1)


class CoordinateControl(BaseModel):
    model_config = ConfigDict(extra="forbid", frozen=True)

    id: str = Field(min_length=1)
    vehicle_node_id: str = Field(min_length=1)
    wgs84: Wgs84Origin
    local_m: Point


class ReviewMapManifest(BaseModel):
    model_config = ConfigDict(extra="forbid", frozen=True)

    schema_version: Literal[1]
    map_id: str = Field(min_length=1)
    map_version: str = Field(min_length=1)
    data_status: Literal["REVIEW_PACKAGE"]
    coordinate_frame: Literal["LOCAL_METERS"]
    crs: str = Field(pattern=r"^EPSG:[1-9][0-9]+$")
    origin_wgs84: Wgs84Origin
    coordinate_controls: list[CoordinateControl] = Field(min_length=3)
    source_files: list[SourceFile] = Field(min_length=1)
    artifacts: dict[str, HashedFile]
    review_evidence_refs: list[str] = Field(min_length=1)

    @field_validator("artifacts")
    @classmethod
    def require_artifact_set(cls, value: dict[str, HashedFile]) -> dict[str, HashedFile]:
        if set(value) != ARTIFACT_NAMES:
            raise ValueError(f"artifacts must contain exactly {sorted(ARTIFACT_NAMES)}")
        return value

    @field_validator("review_evidence_refs")
    @classmethod
    def require_evidence_references(cls, value: list[str]) -> list[str]:
        if any(not ref.strip() for ref in value):
            raise ValueError("review evidence references must be non-empty")
        return value


def inspect_review_package(directory: Path) -> dict:
    """Check declared file identities and versions; never grant operating authority."""
    manifest_path = directory / "manifest.json"
    try:
        manifest = ReviewMapManifest.model_validate_json(manifest_path.read_bytes())
    except (OSError, ValidationError) as error:
        return _failure(directory, "manifest_invalid", str(error))

    entries = [*manifest.source_files, *manifest.artifacts.values()]
    resolved_paths = [(directory / entry.path).resolve() for entry in entries]
    if len(resolved_paths) != len(set(resolved_paths)) or manifest_path.resolve() in resolved_paths:
        return _failure(directory, "package_file_paths_not_unique")

    root = directory.resolve()
    file_bytes: dict[Path, bytes] = {}
    for entry, file_path in zip(entries, resolved_paths):
        if not file_path.is_relative_to(root) or not file_path.is_file():
            return _failure(directory, "package_file_missing_or_outside", entry.path)
        try:
            content = file_path.read_bytes()
        except OSError as error:
            return _failure(directory, "package_file_unreadable", str(error))
        if sha256(content).hexdigest() != entry.sha256:
            return _failure(directory, "package_file_hash_mismatch", entry.path)
        file_bytes[file_path] = content

    artifacts: dict[str, dict] = {}
    for name, entry in manifest.artifacts.items():
        try:
            artifact = json.loads(file_bytes[(directory / entry.path).resolve()])
        except (UnicodeDecodeError, json.JSONDecodeError) as error:
            return _failure(directory, "artifact_json_invalid", f"{name}: {error}")
        if not isinstance(artifact, dict) or artifact.get("map_version") != manifest.map_version:
            return _failure(directory, "artifact_map_version_mismatch", name)
        artifacts[name] = artifact
    try:
        content_counts = validate_review_content(artifacts)
    except (ValidationError, ValueError) as error:
        return {
            **_failure(directory, "review_content_invalid", str(error)),
            "package_integrity_valid": True,
            "review_content_valid": False,
        }

    try:
        _validate_coordinates(manifest, artifacts["vehicle_graph"])
    except (ValueError, CRSError, ProjError) as error:
        return {
            **_failure(directory, "coordinate_contract_invalid", str(error)),
            "package_integrity_valid": True,
            "review_content_valid": True,
            "coordinate_contract_valid": False,
        }

    return {
        "report_type": "map_input_inspection",
        "path": str(directory),
        "map_id": manifest.map_id,
        "map_version": manifest.map_version,
        "data_status": manifest.data_status,
        "package_integrity_valid": True,
        "review_content_valid": True,
        "coordinate_contract_valid": True,
        "road_graph_contract_valid": False,
        "mvp_map_ready": False,
        "reason": "review_package_unapproved",
        "source_file_count": len(manifest.source_files),
        "artifact_names": sorted(manifest.artifacts),
        **content_counts,
        "limitations": [
            "Coordinate transforms agree with three graph controls; field accuracy is unverified.",
            "Graph structure and declared pedestrian paths are checked; real access and geometry are not.",
            "Required facility keys are counted; official identity, coverage and surveyed accuracy are unverified.",
            "No review package is accepted by the runtime RoadGraph loader.",
        ],
    }


def _validate_coordinates(manifest: ReviewMapManifest, vehicle_graph: dict) -> None:
    crs = CRS.from_user_input(manifest.crs)
    if not crs.is_projected or len(crs.axis_info) < 2:
        raise ValueError("CRS must have two projected metre axes")
    if any(not isclose(axis.unit_conversion_factor, 1.0, abs_tol=1e-9)
           for axis in crs.axis_info[:2]):
        raise ValueError("CRS axes must use metres")

    forward = Transformer.from_crs("EPSG:4326", crs, always_xy=True)
    inverse = Transformer.from_crs(crs, "EPSG:4326", always_xy=True)
    origin_x, origin_y = forward.transform(manifest.origin_wgs84.lon, manifest.origin_wgs84.lat)
    if not all(map(isfinite, (origin_x, origin_y))):
        raise ValueError("origin cannot be projected into declared CRS")
    nodes = {node["id"]: node["position_m"] for node in vehicle_graph["nodes"]}
    controls = manifest.coordinate_controls
    if len({control.id for control in controls}) != len(controls):
        raise ValueError("coordinate control IDs must be unique")
    if len({control.vehicle_node_id for control in controls}) != len(controls):
        raise ValueError("coordinate controls must reference distinct vehicle nodes")
    points = [control.local_m for control in controls]
    if not any(
        abs((b.x - a.x) * (c.y - a.y) - (b.y - a.y) * (c.x - a.x)) >= 1
        for a, b, c in combinations(points, 3)
    ):
        raise ValueError("coordinate controls must include three noncollinear points")
    for control in controls:
        node = nodes.get(control.vehicle_node_id)
        if node is None:
            raise ValueError(f"coordinate control {control.id} has no vehicle node")
        x, y = forward.transform(control.wgs84.lon, control.wgs84.lat)
        if not all(map(isfinite, (x, y))):
            raise ValueError(f"coordinate control {control.id} cannot be projected")
        if hypot(x - origin_x - control.local_m.x, y - origin_y - control.local_m.y) > 0.01:
            raise ValueError(f"coordinate control {control.id} disagrees with CRS and origin")
        if hypot(node["x"] - control.local_m.x, node["y"] - control.local_m.y) > 0.01:
            raise ValueError(f"coordinate control {control.id} disagrees with vehicle node")
        lon, lat = inverse.transform(x, y)
        roundtrip_x, roundtrip_y = forward.transform(lon, lat)
        if hypot(roundtrip_x - x, roundtrip_y - y) > 0.01:
            raise ValueError(f"coordinate control {control.id} failed round trip")


def _failure(directory: Path, reason: str, detail: str | None = None) -> dict:
    report = {
        "report_type": "map_input_inspection",
        "path": str(directory),
        "package_integrity_valid": False,
        "road_graph_contract_valid": False,
        "mvp_map_ready": False,
        "reason": reason,
    }
    if detail is not None:
        report["detail"] = detail
    return report
