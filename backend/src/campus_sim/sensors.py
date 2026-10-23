"""Proposed local perception boundary, independent of Unity Ground Truth.

Coordinates are ego-local metres: x right, y forward. Relative velocities
are Cartesian observations in the same instantaneous frame, not Doppler.
This contract is not yet connected to the Unity/network schema.
"""

from math import hypot
from typing import Annotated, Literal

from pydantic import BaseModel, ConfigDict, Field, model_validator

Finite = Annotated[float, Field(allow_inf_nan=False)]
Point = tuple[Finite, Finite]


class ObservationModel(BaseModel):
    model_config = ConfigDict(extra="forbid", frozen=True)


class Detection(ObservationModel):
    position_m: Point
    range_m: Annotated[Finite, Field(ge=0)]
    # Conservative enclosing radius is a perception/config assumption, not a hit size.
    radius_m: Annotated[Finite, Field(gt=0)]
    relative_velocity_mps: Point | None = None
    entity_class: Literal["unknown", "static", "pedestrian", "vehicle"] = "unknown"

    @model_validator(mode="after")
    def consistent_range(self) -> "Detection":
        if abs(hypot(*self.position_m) - self.range_m) > 0.05:
            raise ValueError("range and local position disagree")
        return self


class SensorObservation(ObservationModel):
    vehicle_id: str = Field(min_length=1)
    sensor_id: str = Field(min_length=1)
    sensor_type: Literal["LIDAR", "RADAR"]
    observed_tick: int = Field(ge=0, strict=True)
    pose_version: int = Field(ge=0, strict=True)
    valid: bool
    detections: tuple[Detection, ...]


class ObservationContext(ObservationModel):
    vehicle_id: str = Field(min_length=1)
    sensors: dict[str, Literal["LIDAR", "RADAR"]] = Field(min_length=1)
    required_sensor_ids: frozenset[str] = Field(min_length=1)
    current_tick: int = Field(ge=0, strict=True)
    pose_version: int = Field(ge=0, strict=True)
    tick_duration_s: Annotated[Finite, Field(gt=0)]
    max_age_s: Annotated[Finite, Field(ge=0)] = 0.2

    @model_validator(mode="after")
    def registered_required_sensors(self) -> "ObservationContext":
        if not self.required_sensor_ids.issubset(self.sensors):
            raise ValueError("required sensor not registered")
        return self


def observation_error(
    frames: tuple[SensorObservation, ...], context: ObservationContext
) -> str | None:
    """Fail closed on incomplete, mismatched, stale or future snapshots."""
    seen = set()
    for frame in frames:
        if frame.sensor_id in seen:
            return "DUPLICATE_SENSOR"
        seen.add(frame.sensor_id)
        if frame.vehicle_id != context.vehicle_id:
            return "WRONG_VEHICLE"
        if context.sensors.get(frame.sensor_id) != frame.sensor_type:
            return "UNKNOWN_SENSOR"
        if not frame.valid:
            return "INVALID_SENSOR"
        age = (context.current_tick - frame.observed_tick) * context.tick_duration_s
        if age < 0:
            return "FUTURE_FRAME"
        if age > context.max_age_s + 1e-12:
            return "STALE_FRAME"
        if frame.pose_version != context.pose_version:
            return "POSE_MISMATCH"
    if not context.required_sensor_ids.issubset(seen):
        return "MISSING_SENSOR"
    return None
