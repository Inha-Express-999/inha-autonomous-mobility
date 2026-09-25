from __future__ import annotations

import math
from datetime import time
from pathlib import Path
from typing import Literal

from pydantic import BaseModel, ConfigDict, Field, ValidationError, field_validator, model_validator

from campus_sim.road_graph import RoadGraphDocument

SECONDS_PER_DAY = 24 * 60 * 60


class CrowdZoneProfile(BaseModel):
    model_config = ConfigDict(extra="forbid", frozen=True)

    base_rate_persons_per_s: float = Field(ge=0, allow_inf_nan=False)
    peak_rate_persons_per_s: float = Field(ge=0, allow_inf_nan=False)
    usable_area_m2: float = Field(gt=0, allow_inf_nan=False)
    mean_dwell_s: float = Field(gt=0, allow_inf_nan=False)
    zone_multiplier: float = Field(ge=0, allow_inf_nan=False)
    caution_density: float = Field(ge=0, allow_inf_nan=False)
    avoid_density: float = Field(gt=0, allow_inf_nan=False)
    avoid_during_peak: bool
    detour_extra_s: float = Field(gt=0, allow_inf_nan=False)
    detour_ratio: float = Field(ge=0, le=1, allow_inf_nan=False)
    avoid_penalty_s_per_m: float = Field(gt=0, allow_inf_nan=False)
    penalty_s_per_density_m: float = Field(ge=0, allow_inf_nan=False)


class CrowdModelConfig(BaseModel):
    model_config = ConfigDict(extra="forbid", frozen=True)

    schema_version: Literal[1]
    profile_status: Literal["SYNTHETIC_ASSUMPTION"]
    timezone: Literal["Asia/Seoul"]
    start_time: time
    transition_times: list[time] = Field(min_length=1)
    window_before_s: float = Field(gt=0, allow_inf_nan=False)
    window_after_s: float = Field(gt=0, allow_inf_nan=False)
    pre_peak_offset_s: float = Field(allow_inf_nan=False)
    post_peak_offset_s: float = Field(allow_inf_nan=False)
    sigma_s: float = Field(gt=0, allow_inf_nan=False)
    pre_peak_weight: float = Field(ge=0, le=1, allow_inf_nan=False)
    post_peak_weight: float = Field(ge=0, le=1, allow_inf_nan=False)
    zones: dict[str, CrowdZoneProfile] = Field(min_length=1)

    @field_validator("transition_times")
    @classmethod
    def require_unique_transition_times(cls, value: list[time]) -> list[time]:
        if len(value) != len(set(value)):
            raise ValueError("transition_times must be unique")
        return value

    @model_validator(mode="after")
    def require_normalized_peak_weights(self) -> CrowdModelConfig:
        if not math.isclose(self.pre_peak_weight + self.post_peak_weight, 1.0, abs_tol=1e-9):
            raise ValueError("pre_peak_weight and post_peak_weight must sum to 1")
        for zone_id, zone in self.zones.items():
            if zone.avoid_density <= zone.caution_density:
                raise ValueError(f"zone {zone_id!r} avoid_density must exceed caution_density")
        return self

    @classmethod
    def from_default_config(cls) -> CrowdModelConfig:
        path = Path(__file__).resolve().parents[3] / "configs" / "crowd.json"
        try:
            return cls.model_validate_json(path.read_text(encoding="utf-8"))
        except (OSError, ValidationError, ValueError) as error:
            raise ValueError(f"invalid crowd config: {path}") from error

    @staticmethod
    def _seconds(value: time) -> float:
        return value.hour * 3600 + value.minute * 60 + value.second + value.microsecond / 1_000_000

    def seconds_since_midnight(self, simulation_time_s: float) -> float:
        if not math.isfinite(simulation_time_s) or simulation_time_s < 0:
            raise ValueError("simulation_time_s must be finite and non-negative")
        return self._seconds(self.start_time) + simulation_time_s

    def _pulse(self, time_s: float, peak_s: float) -> float:
        offset = time_s - peak_s
        if offset < -self.window_before_s or offset > self.window_after_s:
            return 0.0
        pre = math.exp(-0.5 * ((offset - self.pre_peak_offset_s) / self.sigma_s) ** 2)
        post = math.exp(-0.5 * ((offset - self.post_peak_offset_s) / self.sigma_s) ** 2)
        return self.pre_peak_weight * pre + self.post_peak_weight * post

    def _pulse_integral(self, start_s: float, end_s: float, peak_s: float) -> float:
        clipped_start = max(start_s, peak_s - self.window_before_s)
        clipped_end = min(end_s, peak_s + self.window_after_s)
        if clipped_start >= clipped_end:
            return 0.0

        def gaussian_integral(mean_s: float) -> float:
            scale = self.sigma_s * math.sqrt(math.pi / 2.0)
            denominator = self.sigma_s * math.sqrt(2.0)
            return scale * (
                math.erf((clipped_end - mean_s) / denominator)
                - math.erf((clipped_start - mean_s) / denominator)
            )

        return (
            self.pre_peak_weight * gaussian_integral(peak_s + self.pre_peak_offset_s)
            + self.post_peak_weight * gaussian_integral(peak_s + self.post_peak_offset_s)
        )

    def expected_count(self, zone_id: str, simulation_time_s: float) -> float:
        try:
            zone = self.zones[zone_id]
        except KeyError as error:
            raise ValueError(f"crowd profile missing zone: {zone_id}") from error
        interval_end_s = self.seconds_since_midnight(simulation_time_s)
        interval_start_s = interval_end_s - zone.mean_dwell_s
        expected = zone.base_rate_persons_per_s * zone.mean_dwell_s

        first_day = math.floor(interval_start_s / SECONDS_PER_DAY) - 1
        last_day = math.floor(interval_end_s / SECONDS_PER_DAY) + 1
        for day in range(first_day, last_day + 1):
            day_offset_s = day * SECONDS_PER_DAY
            for transition in self.transition_times:
                peak_s = day_offset_s + self._seconds(transition)
                expected += (
                    zone.peak_rate_persons_per_s
                    * zone.zone_multiplier
                    * self._pulse_integral(interval_start_s, interval_end_s, peak_s)
                )
        return expected

    def density_prior(self, zone_id: str, simulation_time_s: float) -> float:
        return self.expected_count(zone_id, simulation_time_s) / self.zones[zone_id].usable_area_m2

    def _is_peak_window(self, simulation_time_s: float) -> bool:
        current_s = self.seconds_since_midnight(simulation_time_s)
        first_day = math.floor(current_s / SECONDS_PER_DAY) - 1
        last_day = math.floor(current_s / SECONDS_PER_DAY) + 1
        for day in range(first_day, last_day + 1):
            day_offset_s = day * SECONDS_PER_DAY
            for transition in self.transition_times:
                peak_s = day_offset_s + self._seconds(transition)
                if peak_s - self.window_before_s <= current_s <= peak_s + self.window_after_s:
                    return True
        return False

    def zone_state(self, zone_id: str, simulation_time_s: float) -> str:
        zone = self.zones[zone_id]
        if zone.avoid_during_peak and self._is_peak_window(simulation_time_s):
            return "AVOID"
        density = self.density_prior(zone_id, simulation_time_s)
        if density >= zone.avoid_density:
            return "AVOID"
        if density >= zone.caution_density:
            return "CAUTION"
        return "NORMAL"

    def avoided_edge_ids(
        self, graph: RoadGraphDocument, simulation_time_s: float
    ) -> set[str]:
        avoided = {
            zone_id
            for zone_id in self.zones
            if self.zone_state(zone_id, simulation_time_s) == "AVOID"
        }
        if not avoided:
            return set()
        return {edge.id for edge in graph.edges if avoided.intersection(edge.zone_ids)}

    def avoidance_zone_ids(
        self, graph: RoadGraphDocument, edge_ids: set[str]
    ) -> set[str]:
        edge_by_id = {edge.id: edge for edge in graph.edges}
        missing = edge_ids.difference(edge_by_id)
        if missing:
            raise ValueError(f"unknown edges in avoidance policy: {sorted(missing)}")
        return {
            zone_id
            for edge_id in edge_ids
            for zone_id in edge_by_id[edge_id].zone_ids
            if zone_id in self.zones
        }

    def detour_limit_s(self, zone_ids: set[str], reference_cost_s: float) -> float:
        if not zone_ids:
            return 0.0
        return min(
            max(self.zones[zone_id].detour_extra_s, reference_cost_s * self.zones[zone_id].detour_ratio)
            for zone_id in zone_ids
        )

    def avoidance_penalties_s(
        self, graph: RoadGraphDocument, edge_ids: set[str]
    ) -> dict[str, float]:
        edge_by_id = {edge.id: edge for edge in graph.edges}
        penalties: dict[str, float] = {}
        for edge_id in edge_ids:
            edge = edge_by_id[edge_id]
            coefficients = [
                self.zones[zone_id].avoid_penalty_s_per_m
                for zone_id in edge.zone_ids
                if zone_id in self.zones
            ]
            if coefficients:
                penalties[edge_id] = edge.length_m * max(coefficients)
        return penalties

    def edge_penalties_s(
        self, graph: RoadGraphDocument, simulation_time_s: float
    ) -> dict[str, float]:
        penalties: dict[str, float] = {}
        for edge in graph.edges:
            zone_ids = tuple(dict.fromkeys(edge.zone_ids))
            if not zone_ids:
                continue
            missing = set(zone_ids).difference(self.zones)
            if missing:
                raise ValueError(f"crowd config missing graph zones: {sorted(missing)}")
            penalty_s = 0.0
            for zone_id in zone_ids:
                zone = self.zones[zone_id]
                excess_density = max(
                    0.0, self.density_prior(zone_id, simulation_time_s) - zone.caution_density
                )
                penalty_s += edge.length_m * zone.penalty_s_per_density_m * excess_density
            penalties[edge.id] = penalty_s
        return penalties

    def edge_cost_snapshot_s(
        self, graph: RoadGraphDocument, simulation_time_s: float
    ) -> tuple[dict[str, float], dict[str, float]]:
        penalties = self.edge_penalties_s(graph, simulation_time_s)
        edge_by_id = {edge.id: edge for edge in graph.edges}
        costs = {
            edge_id: edge_by_id[edge_id].cost_s() + penalty_s
            for edge_id, penalty_s in penalties.items()
        }
        return costs, penalties
