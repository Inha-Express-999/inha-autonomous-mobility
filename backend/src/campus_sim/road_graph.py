from __future__ import annotations

import json
import math
from pathlib import Path
from typing import Literal

from pydantic import BaseModel, ConfigDict, Field, ValidationError, field_validator, model_validator

from campus_sim.domain import ServiceType


class RoadGraphLoadError(ValueError):
    """The road graph file is missing, malformed, or violates its data contract."""


class Point2D(BaseModel):
    model_config = ConfigDict(extra="forbid", frozen=True)

    x: float
    y: float

    @field_validator("x", "y")
    @classmethod
    def require_finite_coordinate(cls, value: float) -> float:
        if not math.isfinite(value):
            raise ValueError("coordinates must be finite")
        return value


class RoadNode(BaseModel):
    model_config = ConfigDict(extra="forbid", frozen=True)

    id: str = Field(min_length=1)
    landmark_id: str = Field(min_length=1)
    stop_id: str = Field(min_length=1)
    position_m: Point2D


class RoadEdge(BaseModel):
    model_config = ConfigDict(extra="forbid", frozen=True)

    id: str = Field(min_length=1)
    from_node: str = Field(min_length=1)
    to_node: str = Field(min_length=1)
    key: str = Field(min_length=1)
    geometry_m: list[Point2D] = Field(min_length=2)
    length_m: float = Field(gt=0)
    width_m: float = Field(gt=0)
    allowed_speed_mps: float = Field(gt=0)
    crowd_penalty_s: float = Field(default=0, ge=0)
    zone_penalty_s: float = Field(default=0, ge=0)
    expected_wait_s: float = Field(default=0, ge=0)
    allowed_service_types: list[ServiceType] = Field(min_length=1)
    allowed_vehicle_classes: list[str] = Field(min_length=1)
    zone_ids: list[str] = Field(default_factory=list)
    source: str = Field(min_length=1)
    verification_status: Literal["SYNTHETIC"]
    step_free: bool
    is_open: bool = True

    @field_validator(
        "length_m",
        "width_m",
        "allowed_speed_mps",
        "crowd_penalty_s",
        "zone_penalty_s",
        "expected_wait_s",
    )
    @classmethod
    def require_finite_cost_inputs(cls, value: float) -> float:
        if not math.isfinite(value):
            raise ValueError("edge costs and limits must be finite")
        return value

    @field_validator("allowed_service_types")
    @classmethod
    def require_unique_service_types(cls, value: list[ServiceType]) -> list[ServiceType]:
        if len(value) != len(set(value)):
            raise ValueError("allowed_service_types must not contain duplicates")
        return value

    @field_validator("allowed_vehicle_classes")
    @classmethod
    def require_unique_vehicle_classes(cls, value: list[str]) -> list[str]:
        if any(not item.strip() for item in value):
            raise ValueError("allowed_vehicle_classes must contain non-empty names")
        if len(value) != len(set(value)):
            raise ValueError("allowed_vehicle_classes must not contain duplicates")
        return value

    def cost_s(self) -> float:
        return (
            self.length_m / self.allowed_speed_mps
            + self.crowd_penalty_s
            + self.zone_penalty_s
            + self.expected_wait_s
        )


class RoadGraphDocument(BaseModel):
    """Versioned input contract for the current synthetic routing fixture."""

    model_config = ConfigDict(extra="forbid", frozen=True)

    schema_version: Literal[1]
    map_id: str = Field(min_length=1)
    map_version: str = Field(min_length=1)
    data_status: Literal["SYNTHETIC_FIXTURE"]
    coordinate_frame: Literal["SYNTHETIC_LOCAL_METERS"]
    verification_status: Literal["SYNTHETIC_FIXTURE"]
    source_description: str = Field(min_length=1)
    source_date: str | None = None
    source_hash: str | None = None
    origin_wgs84: Point2D | None = None
    nodes: list[RoadNode] = Field(min_length=2)
    edges: list[RoadEdge] = Field(min_length=1)

    @model_validator(mode="after")
    def validate_topology_and_geometry(self) -> RoadGraphDocument:
        node_by_id = {node.id: node for node in self.nodes}
        if len(node_by_id) != len(self.nodes):
            raise ValueError("node ids must be unique")
        stop_ids = [node.stop_id for node in self.nodes]
        if len(stop_ids) != len(set(stop_ids)):
            raise ValueError("stop ids must be unique")
        edge_ids = [edge.id for edge in self.edges]
        if len(edge_ids) != len(set(edge_ids)):
            raise ValueError("edge ids must be unique")

        for edge in self.edges:
            if edge.from_node not in node_by_id or edge.to_node not in node_by_id:
                raise ValueError(f"edge {edge.id!r} references an unknown node")
            start = node_by_id[edge.from_node].position_m
            end = node_by_id[edge.to_node].position_m
            if not _same_point(edge.geometry_m[0], start):
                raise ValueError(f"edge {edge.id!r} geometry does not start at from_node")
            if not _same_point(edge.geometry_m[-1], end):
                raise ValueError(f"edge {edge.id!r} geometry does not end at to_node")
            geometry_length = sum(
                math.hypot(right.x - left.x, right.y - left.y)
                for left, right in zip(edge.geometry_m, edge.geometry_m[1:])
            )
            if edge.length_m + 0.01 < geometry_length:
                raise ValueError(f"edge {edge.id!r} length is shorter than its geometry")
        return self


def _same_point(left: Point2D, right: Point2D) -> bool:
    return math.hypot(left.x - right.x, left.y - right.y) <= 0.01


def load_road_graph(path: str | Path) -> RoadGraphDocument:
    graph_path = Path(path)
    try:
        raw = json.loads(graph_path.read_text(encoding="utf-8"))
        return RoadGraphDocument.model_validate(raw)
    except OSError as error:
        raise RoadGraphLoadError(f"cannot read road graph {graph_path}: {error}") from error
    except json.JSONDecodeError as error:
        raise RoadGraphLoadError(f"invalid JSON in road graph {graph_path}: {error}") from error
    except ValidationError as error:
        raise RoadGraphLoadError(f"invalid road graph {graph_path}: {error}") from error
