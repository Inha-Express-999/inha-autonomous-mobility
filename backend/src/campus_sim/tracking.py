"""Consecutive visible-surface motion estimates from sensor captures only."""
import math
from dataclasses import dataclass

from campus_sim.domain import SensorEntityClass, SensorObservation


@dataclass(frozen=True)
class ObservedMotion:
    entity_id: str
    observed_time_s: float
    world_position_m: tuple[float, float]
    world_velocity_mps: tuple[float, float]
    relative_position_m: tuple[float, float]
    relative_velocity_mps: tuple[float, float]


class PedestrianMotionTracker:
    """One vehicle/sensor stream, bounded by the latest frame's visible returns.

    Velocities describe observed surface points, not guaranteed object centers.
    Missing metadata, invalid frames, gaps and context changes reset the baseline.
    No extrapolation through occlusion or queries of hidden transforms are used.
    """

    def __init__(self, *, min_interval_s: float = 0.05, max_interval_s: float = 0.3):
        if not 0 < min_interval_s <= max_interval_s or not math.isfinite(max_interval_s):
            raise ValueError("Tracking intervals must be finite, positive and ordered")
        self.min_interval_s = min_interval_s
        self.max_interval_s = max_interval_s
        self._previous: SensorObservation | None = None

    @staticmethod
    def _world_returns(frame: SensorObservation) -> dict[str, tuple[float, float]]:
        nearest = {}
        for detection in frame.detections:
            if detection.entity_class is not SensorEntityClass.PEDESTRIAN or detection.entity_id is None:
                continue
            old = nearest.get(detection.entity_id)
            if old is None or detection.range_m < old.range_m:
                nearest[detection.entity_id] = detection
        sine, cosine = math.sin(frame.sensor_heading_rad), math.cos(frame.sensor_heading_rad)
        origin = frame.sensor_position_m
        return {
            identity: (origin.x + hit.local_position_m.x * sine - hit.local_position_m.y * cosine,
                       origin.y + hit.local_position_m.x * cosine + hit.local_position_m.y * sine)
            for identity, hit in nearest.items()
        }

    def update(self, frame: SensorObservation) -> tuple[ObservedMotion, ...]:
        previous = self._previous
        context = lambda item: (item.session_id, item.map_version, item.vehicle_id, item.sensor_id)
        if (previous is not None and context(frame) == context(previous)
                and (frame.observed_tick <= previous.observed_tick or
                     frame.observed_time_s is not None and
                     frame.observed_time_s <= previous.observed_time_s)):
            # A delayed/replayed frame must not replace a newer baseline,
            # including when the delayed frame is marked invalid.
            return ()
        if not frame.valid or frame.observed_time_s is None:
            self._previous = None
            return ()
        self._previous = frame.model_copy(deep=True)
        if previous is None:
            return ()
        dt = frame.observed_time_s - previous.observed_time_s
        if (context(frame) != context(previous) or frame.observed_tick <= previous.observed_tick
                or not self.min_interval_s <= dt <= self.max_interval_s):
            return ()
        before, after = self._world_returns(previous), self._world_returns(frame)
        sensor = frame.sensor_position_m
        previous_sensor = previous.sensor_position_m
        sensor_velocity = ((sensor.x - previous_sensor.x) / dt, (sensor.y - previous_sensor.y) / dt)
        if not all(math.isfinite(value) for value in sensor_velocity):
            return ()
        result = []
        for identity, position in after.items():
            if identity not in before:
                continue
            velocity = tuple((position[i] - before[identity][i]) / dt for i in range(2))
            relative_velocity = tuple(velocity[i] - sensor_velocity[i] for i in range(2))
            relative_position = (position[0] - sensor.x, position[1] - sensor.y)
            if not all(math.isfinite(value) for value in (*position, *velocity,
                                                         *relative_velocity, *relative_position)):
                continue
            result.append(ObservedMotion(identity, frame.observed_time_s, position, velocity,
                                         relative_position, relative_velocity))
        return tuple(result)
