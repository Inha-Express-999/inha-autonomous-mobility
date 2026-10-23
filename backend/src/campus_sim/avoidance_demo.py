"""Bounded synthetic sensor preview, isolated from dispatch and vehicle state."""

from dataclasses import asdict
from math import hypot, isfinite
from typing import Literal

from pydantic import BaseModel, ConfigDict, Field

from campus_sim.local_planning import ConvexCorridor, plan_rrt
from campus_sim.safety import assess_safety
from campus_sim.sensors import Detection, ObservationContext, SensorObservation


class AvoidancePreview(BaseModel):
    model_config = ConfigDict(extra="forbid", allow_inf_nan=False)
    mode: Literal["static", "crossing"] = "static"
    x: float = Field(default=0, ge=-3, le=3)
    y: float = Field(default=6, ge=1, le=10)
    radius: float = Field(default=0.7, ge=0.2, le=2)
    speed_mps: float = Field(default=2, ge=0, le=30 / 3.6)
    stale: bool = False
    seed: int = Field(default=12, ge=0, le=10000)


def avoidance_preview(command: AvoidancePreview) -> dict:
    context = ObservationContext(
        vehicle_id="SYNTHETIC",
        sensors={"lidar": "LIDAR", "radar": "RADAR"},
        required_sensor_ids=frozenset({"lidar", "radar"}),
        current_tick=100,
        pose_version=1,
        tick_duration_s=0.02,
    )
    obstacle = Detection(
        position_m=(command.x, command.y),
        range_m=hypot(command.x, command.y),
        radius_m=command.radius,
        entity_class="static" if command.mode == "static" else "pedestrian",
        relative_velocity_mps=None if command.mode == "static" else (-2, -command.speed_mps),
    )
    frames = tuple(
        SensorObservation(
            vehicle_id="SYNTHETIC",
            sensor_id=sensor,
            sensor_type=kind,
            observed_tick=80 if command.stale else 100,
            pose_version=1,
            valid=True,
            detections=(obstacle,)
            if sensor == ("lidar" if command.mode == "static" else "radar")
            else (),
        )
        for sensor, kind in (("lidar", "LIDAR"), ("radar", "RADAR"))
    )
    safety = asdict(
        assess_safety(
            frames,
            context,
            speed_mps=command.speed_mps,
            ego_radius_m=0.5,
        )
    )
    if not isfinite(safety["minimum_ttc_s"]):
        safety["minimum_ttc_s"] = None
    corridor = ConvexCorridor(((-4, -2), (4, -2), (4, 14), (-4, 14)))
    rrt = plan_rrt((0, 0), (0, 12), corridor, frames, context, ego_radius_m=0.5, seed=command.seed)
    return {
        "simulation_only": True,
        "source": "synthetic_sensor_fixture",
        "corridor": corridor.vertices,
        "start": (0, 0),
        "goal": (0, 12),
        "obstacle": obstacle.model_dump(mode="json"),
        "rrt": asdict(rrt),
        "safety": safety,
        "ego_radius_m": 0.5,
        "rrt_margin_m": 0.3,
        "speed_mps": command.speed_mps,
    }
