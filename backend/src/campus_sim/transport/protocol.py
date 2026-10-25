"""Strict, bounded JSON subset for the week-7 STOP-only transport experiment."""

import json
from typing import Annotated, Literal

from pydantic import BaseModel, ConfigDict, Field, model_validator

MAX_MESSAGE_BYTES = 65536
Identifier = Annotated[str, Field(min_length=1, max_length=128)]
Counter = Annotated[int, Field(ge=0)]
Finite = Annotated[float, Field(allow_inf_nan=False)]
Time = Annotated[float, Field(ge=0, allow_inf_nan=False)]


class WireModel(BaseModel):
    model_config = ConfigDict(extra="forbid", strict=True, allow_inf_nan=False)


class Vector(WireModel):
    east: Finite
    north: Finite
    height: Finite


class Ego(WireModel):
    vehicle_id: Identifier
    pose_version: Counter
    position: Vector
    heading_rad: Finite
    speed_mps: Time
    velocity: Vector
    last_applied_command_id: Identifier | None


class LocalPosition(WireModel):
    right: Finite
    forward: Finite
    up: Finite


class Hit(WireModel):
    beam_id: Counter
    range_m: Time
    bearing_rad: Finite
    local_position: LocalPosition
    radial_relative_speed_mps: Finite | None = None


class Observation(WireModel):
    vehicle_id: Identifier
    sensor_id: Identifier
    sensor_type: Literal["LIDAR", "RADAR"]
    sensor_config_version: Identifier
    scan_id: Identifier
    observed_tick: Counter
    observed_time_s: Time
    ego_pose_version: Counter
    validity: Literal["VALID", "INVALID"]
    hits: list[Hit] = Field(max_length=64)

    @model_validator(mode="after")
    def check_hits(self):
        if len({h.beam_id for h in self.hits}) != len(self.hits):
            raise ValueError("duplicate beam_id")
        for hit in self.hits:
            if (self.sensor_type == "RADAR") != (hit.radial_relative_speed_mps is not None):
                raise ValueError("radial speed is required only for RADAR")
        return self


class Result(WireModel):
    vehicle_id: Identifier
    command_id: Identifier
    status: Literal["VALIDATED", "APPLIED", "REJECTED"]
    applied_tick: Counter | None
    rejection_code: Identifier | None

    @model_validator(mode="after")
    def check_status(self):
        if (self.status == "APPLIED") != (self.applied_tick is not None):
            raise ValueError("applied_tick is required only for APPLIED")
        if (self.status == "REJECTED") != (self.rejection_code is not None):
            raise ValueError("rejection_code is required only for REJECTED")
        return self


class Hello(WireModel):
    role: Literal["SIMULATION", "PC", "MOBILE"]
    vehicle_id: Identifier | None


class Envelope(WireModel):
    type: Literal["hello", "ego.state", "sensor.observation", "control.result"]
    schema_version: int = Field(ge=4, le=4)
    project_version: str = Field(pattern=r"^\d+(\.\d+){3}$")
    run_id: Identifier | None
    session_id: Identifier | None = None
    message_id: Identifier
    seq: Counter
    simulation_tick: Counter
    simulation_time_s: Time
    payload: dict


def _unique_object(pairs):
    result = {}
    for key, value in pairs:
        if key in result:
            raise ValueError("duplicate JSON field")
        result[key] = value
    return result


def _reject_constant(value):
    raise ValueError("nonfinite JSON number")


class RuntimeCodec:
    @staticmethod
    def parse_message(raw: str):
        if len(raw.encode("utf-8")) > MAX_MESSAGE_BYTES:
            raise ValueError("message too large")
        data = json.loads(raw, object_pairs_hook=_unique_object, parse_constant=_reject_constant)
        envelope = Envelope.model_validate(data)
        payload_type = {"hello": Hello, "ego.state": Ego,
                        "sensor.observation": Observation, "control.result": Result}[envelope.type]
        return envelope, payload_type.model_validate(envelope.payload)

    @staticmethod
    def encode_message(message: dict) -> str:
        return json.dumps(message, allow_nan=False, separators=(",", ":"))
