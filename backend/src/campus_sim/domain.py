from __future__ import annotations

import math
from enum import StrEnum
from itertools import pairwise
from typing import Literal

from pydantic import BaseModel, ConfigDict, Field, field_validator, model_validator


class ServiceType(StrEnum):
    PASSENGER = "PASSENGER"
    CARGO = "CARGO"


class SensorType(StrEnum):
    LIDAR_2D = "LIDAR_2D"
    RADAR = "RADAR"


class SensorEntityClass(StrEnum):
    PEDESTRIAN = "PEDESTRIAN"
    VEHICLE = "VEHICLE"
    STATIC_OBSTACLE = "STATIC_OBSTACLE"
    UNKNOWN = "UNKNOWN"


class RequestStatus(StrEnum):
    CREATED = "CREATED"
    VALIDATED = "VALIDATED"
    QUEUED = "QUEUED"
    ASSIGNED = "ASSIGNED"
    PICKUP_SERVICE = "PICKUP_SERVICE"
    IN_TRANSIT = "IN_TRANSIT"
    DROPOFF_SERVICE = "DROPOFF_SERVICE"
    COMPLETED = "COMPLETED"
    REJECTED = "REJECTED"
    CANCELLED = "CANCELLED"
    EXPIRED = "EXPIRED"
    FAILED = "FAILED"


class ReasonCode(StrEnum):
    UNKNOWN = "UNKNOWN"
    CROWD_AVOIDANCE = "CROWD_AVOIDANCE"
    ZONE_CLOSED = "ZONE_CLOSED"
    NO_ACCESSIBLE_ALTERNATIVE = "NO_ACCESSIBLE_ALTERNATIVE"
    VEHICLE_FAILURE = "VEHICLE_FAILURE"
    STALE_LOCALIZATION = "STALE_LOCALIZATION"
    SENSOR_DATA_STALE = "SENSOR_DATA_STALE"
    SENSOR_INVALID = "SENSOR_INVALID"
    OBSTACLE_STOP = "OBSTACLE_STOP"
    SAFETY_RESUME_HOLD = "SAFETY_RESUME_HOLD"


def _to_camel_case(name: str) -> str:
    first, *rest = name.split("_")
    return first + "".join(part.capitalize() for part in rest)


class ServiceNeeds(BaseModel):
    requires_step_free: bool = False
    wheelchair_slots: int = Field(default=0, ge=0)
    boarding_assistance: bool = False

    @model_validator(mode="after")
    def wheelchair_requires_step_free(self) -> ServiceNeeds:
        if self.wheelchair_slots and not self.requires_step_free:
            raise ValueError("wheelchair_slots requires requires_step_free=true")
        return self


class CreateRequest(BaseModel):
    command_id: str = Field(min_length=1, max_length=128)
    owner_id: str = Field(min_length=1, max_length=128)
    service_type: ServiceType
    pickup_landmark_id: str = Field(min_length=1)
    dropoff_landmark_id: str = Field(min_length=1)
    service_needs: ServiceNeeds = Field(default_factory=ServiceNeeds)
    party_size: int = Field(default=1, ge=0)
    cargo_kg: float = Field(default=0, ge=0)
    latest_arrival_s: float | None = Field(default=None, ge=0)

    @model_validator(mode="after")
    def validate_service_payload(self) -> CreateRequest:
        if self.pickup_landmark_id == self.dropoff_landmark_id:
            raise ValueError("pickup and dropoff landmarks must differ")
        if self.service_type is ServiceType.PASSENGER:
            if self.party_size < 1 or self.cargo_kg != 0:
                raise ValueError("a passenger request needs party_size >= 1 and cargo_kg = 0")
            if self.service_needs.wheelchair_slots > self.party_size:
                raise ValueError("wheelchair_slots cannot exceed party_size")
        elif self.party_size != 0 or self.cargo_kg <= 0:
            raise ValueError("a cargo request needs party_size = 0 and cargo_kg > 0")
        return self


class RequestView(BaseModel):
    id: str
    owner_id: str
    service_type: ServiceType
    status: RequestStatus
    created_s: float
    pickup_landmark_id: str
    dropoff_landmark_id: str
    pickup_stop_id: str | None = None
    dropoff_stop_id: str | None = None
    service_needs: ServiceNeeds
    party_size: int
    cargo_kg: float
    latest_arrival_s: float | None = None
    eta_s: float | None = Field(
        default=None,
        ge=0,
        description="Estimated remaining seconds until arrival at the requested dropoff Stop.",
    )
    vehicle_id: str | None = None
    reason: ReasonCode = ReasonCode.UNKNOWN


class CommandAck(BaseModel):
    command_id: str
    accepted: bool
    request: RequestView


class Landmark(BaseModel):
    id: str
    name: str
    stop_ids: list[str]


class Stop(BaseModel):
    id: str
    landmark_id: str
    step_free_access: bool


class MapPosition(BaseModel):
    model_config = ConfigDict(extra="forbid")

    x: float = Field(allow_inf_nan=False)
    y: float = Field(allow_inf_nan=False)
    z: float = Field(allow_inf_nan=False)


class EgoLocalization(BaseModel):
    vehicle_id: str = Field(min_length=1, max_length=128)
    session_id: str = Field(min_length=1, max_length=64)
    observed_tick: int = Field(ge=0)
    map_version: str = Field(min_length=1, max_length=128)
    position: MapPosition
    heading_rad: float = Field(allow_inf_nan=False)
    speed_mps: float = Field(ge=0, allow_inf_nan=False)


class SensorDetection(BaseModel):
    model_config = ConfigDict(
        alias_generator=_to_camel_case,
        populate_by_name=True,
        extra="forbid",
    )

    range_m: float = Field(gt=0, allow_inf_nan=False)
    bearing_rad: float = Field(ge=-math.pi, le=math.pi, allow_inf_nan=False)
    # Sensor-local frame: x forward, y left, z up; bearing is positive to the left.
    local_position_m: MapPosition
    entity_class: SensorEntityClass
    relative_speed_mps: float | None = Field(default=None, allow_inf_nan=False)
    entity_id: str | None = Field(default=None, min_length=1, max_length=128)

    @field_validator("entity_id")
    @classmethod
    def entity_id_is_nonblank(cls, value: str | None) -> str | None:
        if value is not None and not value.strip():
            raise ValueError("entity_id cannot be blank")
        return value

    @model_validator(mode="after")
    def detection_geometry_matches_polar_values(self) -> SensorDetection:
        horizontal_range = math.hypot(self.local_position_m.x, self.local_position_m.y)
        if not math.isclose(self.range_m, horizontal_range, rel_tol=0.01, abs_tol=0.05):
            raise ValueError("range_m does not match local_position_m")
        expected_bearing = math.atan2(self.local_position_m.y, self.local_position_m.x)
        bearing_error = math.atan2(
            math.sin(self.bearing_rad - expected_bearing),
            math.cos(self.bearing_rad - expected_bearing),
        )
        if abs(bearing_error) > 0.02:
            raise ValueError("bearing_rad does not match local_position_m")
        return self


class SensorRay(BaseModel):
    """One measured ray, never evidence about the space between rays."""
    model_config = ConfigDict(alias_generator=_to_camel_case, populate_by_name=True, extra="forbid")
    bearing_rad: float = Field(ge=-math.pi, le=math.pi, allow_inf_nan=False)
    outcome: Literal["HIT", "MISS", "INVALID"]
    range_m: float | None = Field(default=None, gt=0, allow_inf_nan=False)

    @model_validator(mode="after")
    def range_matches_outcome(self) -> SensorRay:
        if (self.outcome == "INVALID") != (self.range_m is None):
            raise ValueError("Only INVALID rays omit range")
        return self


class SensorObservation(BaseModel):
    model_config = ConfigDict(
        alias_generator=_to_camel_case,
        populate_by_name=True,
        extra="forbid",
    )

    vehicle_id: str = Field(min_length=1, max_length=128)
    sensor_id: str = Field(min_length=1, max_length=128)
    sensor_type: SensorType
    session_id: str = Field(min_length=1, max_length=64)
    map_version: str = Field(min_length=1, max_length=128)
    observed_tick: int = Field(ge=0)
    ego_pose_tick: int = Field(ge=0)
    valid: bool
    detections: list[SensorDetection] = Field(max_length=64)
    observed_time_s: float | None = Field(default=None, ge=0, allow_inf_nan=False)
    sensor_position_m: MapPosition | None = None
    sensor_heading_rad: float | None = Field(default=None, allow_inf_nan=False)
    ray_max_range_m: float | None = Field(default=None, gt=0, allow_inf_nan=False)
    rays: list[SensorRay] = Field(default_factory=list, max_length=64)

    @model_validator(mode="after")
    def radar_speed_is_sensor_specific(self) -> SensorObservation:
        metadata = (self.observed_time_s, self.sensor_position_m, self.sensor_heading_rad)
        if any(value is not None for value in metadata) and any(value is None for value in metadata):
            raise ValueError("Sensor capture time and world pose must be provided together")
        if self.sensor_type is SensorType.LIDAR_2D and any(
            detection.relative_speed_mps is not None for detection in self.detections
        ):
            raise ValueError("relative_speed_mps is only valid for radar detections")
        if bool(self.rays) != (self.ray_max_range_m is not None):
            raise ValueError("Ray samples and maximum range must be supplied together")
        if self.rays:
            if self.sensor_type is not SensorType.LIDAR_2D or self.observed_time_s is None:
                raise ValueError("Ray samples require LiDAR and capture metadata")
            bearings = [ray.bearing_rad for ray in self.rays]
            if any(a >= b for a, b in pairwise(bearings)):
                raise ValueError("Rays must have strictly increasing bearings")
            hits = []
            for ray in self.rays:
                if ray.outcome == "INVALID":
                    if self.valid:
                        raise ValueError("Invalid ray invalidates the frame")
                elif ray.range_m > self.ray_max_range_m + 1e-5:
                    raise ValueError("Ray exceeds scan range")
                elif ray.outcome == "MISS" and abs(ray.range_m - self.ray_max_range_m) > 1e-5:
                    raise ValueError("Miss must report the full tested range")
                if ray.outcome == "HIT":
                    hits.append(ray)
            if len(hits) != len(self.detections) or any(
                abs(ray.range_m - hit.range_m) > 1e-5
                or abs(ray.bearing_rad - hit.bearing_rad) > 1e-4
                for ray, hit in zip(hits, self.detections)
            ):
                raise ValueError("Each LiDAR HIT must match one ordered detection")
        return self


class Vehicle(BaseModel):
    id: str
    passenger_capacity: int
    wheelchair_slots: int
    cargo_capacity_kg: float
    available: bool = True
