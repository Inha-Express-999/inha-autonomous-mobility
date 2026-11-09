"""Time-of-day pedestrian crowd prior per road (AGENTS §7/§8, prior only).

This is a synthetic planning/display prior. It never reads Unity pedestrian
Ground Truth and never produces CLOSED: closing a zone needs a current sensor
observation or an explicit closure, which are not implemented yet.
"""

from __future__ import annotations

from collections.abc import Mapping
from dataclasses import dataclass
from itertools import pairwise
from math import ceil, dist, exp, isfinite
from pathlib import Path
from typing import Literal

from pydantic import BaseModel, ConfigDict, Field, field_validator, model_validator

from campus_sim.planning import EdgeId, Point, RoadEdge, RoadGraph

DAY_S = 86_400.0
CONFIG_PATH = Path("configs/crowd_prior.json")
Level = Literal["NORMAL", "CAUTION", "AVOID"]
LEVEL_RANK = {"NORMAL": 0, "CAUTION": 1, "AVOID": 2}


def _clock_seconds(value: str) -> float:
    parts = value.split(":")
    if len(parts) not in (2, 3) or not all(p.isdigit() and len(p) == 2 for p in parts):
        raise ValueError("time must be HH:MM or HH:MM:SS")
    hours, minutes, seconds = (int(p) for p in (*parts, "00")[:3])
    if hours > 23 or minutes > 59 or seconds > 59:
        raise ValueError("time out of range")
    return float(hours * 3600 + minutes * 60 + seconds)


def format_clock(seconds: float) -> str:
    total = int(seconds) % int(DAY_S)
    return f"{total // 3600:02d}:{total // 60 % 60:02d}:{total % 60:02d}"


class CrowdZone(BaseModel):
    model_config = ConfigDict(extra="forbid", allow_inf_nan=False, strict=True)
    id: str = Field(min_length=1)
    name: str = Field(min_length=1)
    anchor_landmark_id: str = Field(min_length=1)
    radius_m: float = Field(gt=0)
    usable_area_m2: float = Field(gt=0)
    base_rate_pps: float = Field(ge=0)
    peak_rate_pps: float = Field(ge=0)
    multiplier: float = Field(ge=0)
    mean_dwell_s: float = Field(gt=0)
    avoid_during_window: bool


class CrowdPriorConfig(BaseModel):
    model_config = ConfigDict(extra="forbid", allow_inf_nan=False, strict=True)
    schema_version: Literal[1]
    source: str = Field(min_length=1)
    verification: Literal["synthetic"]
    note: str
    timezone: Literal["Asia/Seoul"]
    start_time_of_day: str
    transition_times: list[str] = Field(min_length=1)
    window_before_s: float = Field(gt=0)
    window_after_s: float = Field(gt=0)
    pre_peak_offset_s: float
    post_peak_offset_s: float
    pre_peak_weight: float = Field(ge=0)
    post_peak_weight: float = Field(ge=0)
    sigma_s: float = Field(gt=0)
    integration_step_s: float = Field(gt=0, le=60)
    caution_density: float = Field(gt=0)
    avoid_density: float = Field(gt=0)
    close_density: float = Field(gt=0)
    road_min_overlap_m: float = Field(ge=0)
    road_min_overlap_ratio: float = Field(gt=0, le=1)
    zones: list[CrowdZone] = Field(min_length=1)

    @field_validator("start_time_of_day")
    @classmethod
    def _start(cls, value: str) -> str:
        _clock_seconds(value)
        return value

    @field_validator("transition_times")
    @classmethod
    def _transitions(cls, values: list[str]) -> list[str]:
        seconds = [_clock_seconds(v) for v in values]
        if seconds != sorted(set(seconds)):
            raise ValueError("transition_times must be unique and increasing")
        return values

    @model_validator(mode="after")
    def _thresholds(self) -> CrowdPriorConfig:
        if not self.caution_density < self.avoid_density < self.close_density:
            raise ValueError("require caution < avoid < close density")
        if len({zone.id for zone in self.zones}) != len(self.zones):
            raise ValueError("duplicate zone id")
        return self

    @classmethod
    def load(cls, path: Path) -> CrowdPriorConfig:
        return cls.model_validate_json(path.read_text(encoding="utf-8"))


@dataclass(frozen=True)
class Road:
    """Undirected display road: one or both directed edges with the same geometry."""

    id: str
    edge_ids: tuple[EdgeId, ...]
    polyline: tuple[Point, ...]
    length_m: float


def road_groups(graph: RoadGraph) -> list[Road]:
    """Group directed edges sharing one geometry, in first-appearance order."""
    groups: dict[tuple, list[RoadEdge]] = {}
    for edge in graph.edges:
        key = tuple(sorted((edge.polyline, tuple(reversed(edge.polyline)))))
        groups.setdefault(key, []).append(edge)
    roads, seen = [], set()
    for edges in groups.values():
        base = min(edges, key=lambda e: e.id)
        key = base.key[:-2] if base.key.endswith((":f", ":r")) else base.key
        identifier = key if key not in seen else f"{key}@{base.u}"
        if identifier in seen:
            raise ValueError("road ids are not unique")
        seen.add(identifier)
        roads.append(
            Road(identifier, tuple(e.id for e in edges), edges[0].polyline, edges[0].length_m)
        )
    return roads


def length_inside_circle(polyline: tuple[Point, ...], center: Point, radius: float) -> float:
    """Polyline length within a circle, sampled at <=1 m (display precision)."""
    inside = 0.0
    for a, b in pairwise(polyline):
        length = dist(a, b)
        steps = max(1, ceil(length))
        for i in range(steps):
            t = (i + 0.5) / steps
            point = (a[0] + (b[0] - a[0]) * t, a[1] + (b[1] - a[1]) * t)
            if dist(point, center) <= radius:
                inside += length / steps
    return inside


class CrowdPrior:
    """Static road/zone binding plus time-of-day density evaluation."""

    def __init__(
        self,
        config: CrowdPriorConfig,
        graph: RoadGraph,
        anchors: Mapping[str, Point],
        config_label: str = CONFIG_PATH.as_posix(),
    ):
        self.config = config
        self.config_label = config_label
        self.start_s = _clock_seconds(config.start_time_of_day)
        self.peaks_s = [_clock_seconds(t) for t in config.transition_times]
        # Operator override of the prior's time of day only: (sim_time_s, time_of_day_s).
        # The simulation clock is never jumped (sensor age / command expiry depend on it).
        self.override: tuple[float, float] | None = None
        self.roads = road_groups(graph)
        self.zones: list[CrowdZone] = []
        self.unavailable: list[dict] = []
        self.centers: dict[str, Point] = {}
        for zone in config.zones:
            center = anchors.get(zone.anchor_landmark_id)
            if center is None:
                self.unavailable.append({"id": zone.id, "reason": "ANCHOR_NOT_IN_MAP"})
                continue
            self.zones.append(zone)
            self.centers[zone.id] = center
        self.road_zones: dict[str, tuple[str, ...]] = {}
        for road in self.roads:
            members = []
            for zone in self.zones:
                inside = length_inside_circle(road.polyline, self.centers[zone.id], zone.radius_m)
                if inside > 0 and (
                    inside >= config.road_min_overlap_m
                    or inside >= config.road_min_overlap_ratio * road.length_m
                ):
                    members.append(zone.id)
            if members:
                self.road_zones[road.id] = tuple(members)

    @classmethod
    def from_service(cls, service, root: Path) -> CrowdPrior:
        config = CrowdPriorConfig.load(root / CONFIG_PATH)
        anchors = {}
        for landmark in service.landmarks.values():
            node = service.stop_nodes.get(landmark.stop_ids[0]) if landmark.stop_ids else None
            if node in service.graph.nodes:
                anchors[landmark.id] = service.graph.nodes[node]
        return cls(config, service.graph, anchors)

    def time_of_day(self, sim_time_s: float) -> float:
        if not isfinite(sim_time_s) or sim_time_s < 0:
            raise ValueError("simulation time must be finite and nonnegative")
        if self.override is not None:
            anchor_sim, anchor_tod = self.override
            return (anchor_tod + max(0.0, sim_time_s - anchor_sim)) % DAY_S
        return (self.start_s + sim_time_s) % DAY_S

    def peak_targets(self) -> list[float]:
        """Busiest moment of each window: the pre-peak pulse center (Tk + pre offset)."""
        return [(peak + self.config.pre_peak_offset_s) % DAY_S for peak in self.peaks_s]

    def set_time_of_day(self, sim_time_s: float, time_of_day: str | float) -> None:
        tod = _clock_seconds(time_of_day) if isinstance(time_of_day, str) else float(time_of_day)
        if not isfinite(tod) or not 0 <= tod < DAY_S:
            raise ValueError("time of day out of range")
        self.time_of_day(sim_time_s)  # validates sim time
        self.override = (sim_time_s, tod)

    def next_peak(self, sim_time_s: float) -> None:
        """Move to the next busy window; after the last one return to the configured clock."""
        current = self.time_of_day(sim_time_s)
        later = [t for t in sorted(self.peak_targets()) if t > current + 60]
        if later:
            self.set_time_of_day(sim_time_s, later[0])
        else:
            self.override = None

    def reset_time(self) -> None:
        self.override = None

    def pulse(self, time_of_day_s: float) -> float:
        """Σ pulse_k(t); each pulse is zero outside [Tk-before, Tk+after]."""
        c = self.config
        total = 0.0
        for peak in self.peaks_s:
            if not peak - c.window_before_s <= time_of_day_s <= peak + c.window_after_s:
                continue
            for offset, weight in (
                (c.pre_peak_offset_s, c.pre_peak_weight),
                (c.post_peak_offset_s, c.post_peak_weight),
            ):
                total += weight * exp(-0.5 * ((time_of_day_s - peak - offset) / c.sigma_s) ** 2)
        return total

    def in_window(self, time_of_day_s: float) -> bool:
        c = self.config
        return any(
            peak - c.window_before_s <= time_of_day_s <= peak + c.window_after_s
            for peak in self.peaks_s
        )

    def rate(self, zone: CrowdZone, time_of_day_s: float) -> float:
        """λ_z(t) in people/s."""
        return zone.base_rate_pps + zone.peak_rate_pps * zone.multiplier * self.pulse(time_of_day_s)

    def density(self, zone: CrowdZone, time_of_day_s: float) -> float:
        """ρ_prior = ∫_{t-dwell}^{t} λ(s) ds / usable_area (people/m²), trapezoid rule."""
        steps = max(1, ceil(zone.mean_dwell_s / self.config.integration_step_s))
        width = zone.mean_dwell_s / steps
        start = time_of_day_s - zone.mean_dwell_s
        samples = [self.rate(zone, start + i * width) for i in range(steps + 1)]
        people = width * (sum(samples) - 0.5 * (samples[0] + samples[-1]))
        return people / zone.usable_area_m2

    def level(self, zone: CrowdZone, rho: float, window: bool) -> tuple[Level, str | None]:
        c = self.config
        if rho >= c.avoid_density:
            return "AVOID", "PRIOR_DENSITY"
        if zone.avoid_during_window and window:
            return "AVOID", "CONGESTION_WINDOW"
        if rho >= c.caution_density:
            return "CAUTION", "PRIOR_DENSITY"
        return "NORMAL", None

    def snapshot(self, sim_time_s: float) -> dict:
        c = self.config
        tod = self.time_of_day(sim_time_s)
        window = self.in_window(tod)
        zones = {}
        for zone in self.zones:
            rho = self.density(zone, tod)
            level, reason = self.level(zone, rho, window)
            zones[zone.id] = {
                "id": zone.id,
                "name": zone.name,
                "anchor_landmark_id": zone.anchor_landmark_id,
                "rho_prior": round(rho, 4),
                "level": level,
                "reason": reason,
                "exceeds_close_density": rho >= c.close_density,
            }
        roads = []
        for road in self.roads:
            members = [zones[z] for z in self.road_zones.get(road.id, ())]
            worst = max(
                members,
                key=lambda z: (LEVEL_RANK[z["level"]], z["rho_prior"]),
                default=None,
            )
            if worst is None or worst["level"] == "NORMAL":
                continue
            roads.append(
                {
                    "road_id": road.id,
                    "level": worst["level"],
                    "rho_prior": worst["rho_prior"],
                    "zone_id": worst["id"],
                    "reason": worst["reason"],
                }
            )
        return {
            "schema_version": 1,
            "source": "time_of_day_prior",
            "observed": False,
            "verification": c.verification,
            "config": self.config_label,
            "timezone": c.timezone,
            "start_time_of_day": c.start_time_of_day,
            "time_source": "operator_override" if self.override else "configured_start",
            "time_override": self.override is not None,
            "peak_targets": [format_clock(t) for t in self.peak_targets()],
            "sim_time_s": sim_time_s,
            "time_of_day_s": tod,
            "time_of_day": format_clock(tod),
            "in_congestion_window": window,
            "transition_times": list(c.transition_times),
            "unit": "people/m2",
            "thresholds": {
                "caution_density": c.caution_density,
                "avoid_density": c.avoid_density,
                "close_density": c.close_density,
            },
            "zones": list(zones.values()),
            "unavailable_zones": list(self.unavailable),
            "roads": roads,
            "road_count": len(self.roads),
        }

    def road_geometry(self) -> list[dict]:
        return [
            {"road_id": r.id, "points": r.polyline, "zone_ids": list(self.road_zones.get(r.id, ()))}
            for r in self.roads
        ]
