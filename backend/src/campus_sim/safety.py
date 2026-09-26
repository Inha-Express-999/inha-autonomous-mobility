"""Sensor-only safety decisions, independent of service state and network transport.

Range, radar-approach and explicitly bounded crossing gates are synthetic prototypes.
"""
from __future__ import annotations

import json
import math
from collections.abc import Sequence
from dataclasses import dataclass
from pathlib import Path

from campus_sim.crossing import CrossingPolicy, crossing_hazard
from campus_sim.domain import ReasonCode, SensorObservation, SensorType
from campus_sim.tracking import ObservedMotion


@dataclass(frozen=True)
class SafetyPolicy:
    sensor_stale_after_s: float
    resume_clear_s: float
    reaction_time_s: float
    emergency_decel_mps2: float
    margin_m: float
    radar_approach_horizon_s: float = 2.0

    @classmethod
    def from_default_config(cls) -> SafetyPolicy:
        path = Path(__file__).resolve().parents[3] / "configs" / "safety.json"
        try:
            values = json.loads(path.read_text(encoding="utf-8"))

            def number(name: str) -> float:
                value = values[name]
                if isinstance(value, bool) or not isinstance(value, (int, float)):
                    raise TypeError(f"safety config value must be numeric: {name}")
                return float(value)

            policy = cls(
                sensor_stale_after_s=number("sensor_stale_after_s"),
                resume_clear_s=number("resume_clear_s"),
                reaction_time_s=number("reaction_time_s"),
                emergency_decel_mps2=number("emergency_decel_mps2"),
                margin_m=number("margin_m"),
                radar_approach_horizon_s=number("radar_approach_horizon_s"),
            )
        except (OSError, json.JSONDecodeError, KeyError, TypeError, ValueError) as error:
            raise ValueError(f"invalid safety config: {path}") from error
        values_to_check = (
            policy.sensor_stale_after_s,
            policy.resume_clear_s,
            policy.reaction_time_s,
            policy.emergency_decel_mps2,
            policy.margin_m,
            policy.radar_approach_horizon_s,
        )
        if any(not math.isfinite(value) or value < 0 for value in values_to_check):
            raise ValueError(f"safety config values must be finite and non-negative: {path}")
        if policy.sensor_stale_after_s == 0 or policy.emergency_decel_mps2 == 0:
            raise ValueError(f"safety freshness and deceleration must be positive: {path}")
        if not 0 < policy.radar_approach_horizon_s <= 2.0:
            raise ValueError(f"radar approach horizon must be in (0, 2] seconds: {path}")
        return policy


@dataclass(frozen=True)
class SensorSample:
    observation: SensorObservation
    received_at_s: float | None
    motions: tuple[ObservedMotion, ...] = ()


@dataclass(frozen=True)
class SafetyDecision:
    motion_state: str | None
    reason: ReasonCode
    clear_since_s: float | None = None


def evaluate_sensor_safety(
    samples: Sequence[SensorSample],
    *,
    expected_pose_tick: int | None,
    speed_mps: float,
    now_s: float,
    clear_since_s: float | None,
    policy: SafetyPolicy,
    crossing_policy: CrossingPolicy | None = None,
) -> SafetyDecision:
    """Return motion authority and next recovery state without mutating observations.

    A fresh pose/frame handoff revokes authority but preserves the clear interval.
    Resetting it at every pose message would prevent recovery with 10 Hz telemetry.
    Missing/expired/invalid frames and hazards reset it. Service ingress selects
    the vehicle/session and validates observation geometry before this evaluation.
    """
    stale = SafetyDecision("REPLANNING", ReasonCode.SENSOR_DATA_STALE)
    if not samples or expected_pose_tick is None:
        return stale

    # Inspect all streams before pose handoff: insertion order must never hide an
    # expired or invalid stream behind another stream's temporary tick mismatch.
    for sample in samples:
        received_at = sample.received_at_s
        if (
            received_at is None
            or not math.isfinite(received_at)
            or not 0 <= now_s - received_at <= policy.sensor_stale_after_s
        ):
            return stale
    if any(not sample.observation.valid for sample in samples):
        return SafetyDecision("EMERGENCY_STOP", ReasonCode.SENSOR_INVALID)
    if any(sample.observation.ego_pose_tick != expected_pose_tick for sample in samples):
        return SafetyDecision("REPLANNING", ReasonCode.SENSOR_DATA_STALE, clear_since_s)

    speed = max(0.0, speed_mps)
    stopping_distance_m = (
        speed * policy.reaction_time_s
        + speed * speed / (2.0 * policy.emergency_decel_mps2)
        + policy.margin_m
    )
    for sample in samples:
        for detection in sample.observation.detections:
            local = detection.local_position_m
            if local.x > 0.0 and abs(local.y) <= local.x and local.x <= stopping_distance_m:
                return SafetyDecision("EMERGENCY_STOP", ReasonCode.OBSTACLE_STOP)
            rate = detection.relative_speed_mps
            if (
                sample.observation.sensor_type is SensorType.RADAR
                and rate is not None and rate < 0.0
                and local.x > 0.0 and abs(local.y) <= local.x
            ):
                # This is observed radial approach to the configured margin, not
                # a reconstructed 2D velocity or proof of a crossing collision.
                # Include receipt age conservatively within the freshness window.
                age_s = now_s - sample.received_at_s
                time_to_margin_s = max(0.0, (detection.range_m - policy.margin_m) / -rate)
                if time_to_margin_s <= policy.radar_approach_horizon_s + age_s:
                    return SafetyDecision("EMERGENCY_STOP", ReasonCode.OBSTACLE_STOP)

    if crossing_policy is not None:
        for sample in samples:
            hazard = crossing_hazard(
                sample.observation, sample.motions,
                receipt_age_s=now_s - sample.received_at_s,
                margin_m=policy.margin_m, policy=crossing_policy,
            )
            if hazard is None:
                return stale
            if hazard:
                return SafetyDecision("EMERGENCY_STOP", ReasonCode.OBSTACLE_STOP)

    if clear_since_s is None:
        clear_since_s = now_s
    if now_s - clear_since_s < policy.resume_clear_s:
        return SafetyDecision("EMERGENCY_STOP", ReasonCode.SAFETY_RESUME_HOLD, clear_since_s)
    return SafetyDecision(None, ReasonCode.UNKNOWN, clear_since_s)
