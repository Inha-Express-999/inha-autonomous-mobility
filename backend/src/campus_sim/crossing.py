"""Conservative, explicitly configured sensor-surface crossing gate.

Envelopes are inputs with recorded provenance, never inferred vehicle dimensions.
This constant-velocity prototype cannot replace swept-body/turning validation.
"""
import math
from dataclasses import dataclass

from campus_sim.collision import circle_time_to_collision
from campus_sim.domain import SensorEntityClass, SensorObservation
from campus_sim.tracking import ObservedMotion


@dataclass(frozen=True)
class CrossingPolicy:
    # Must contain the whole ego body about this sensor origin, including offsets.
    ego_envelope_radius_m: float
    # Full diameter: a return is on a surface, not at the pedestrian center.
    pedestrian_diameter_m: float
    position_uncertainty_m: float
    velocity_uncertainty_mps: float
    horizon_s: float
    provenance: str

    def __post_init__(self) -> None:
        values = (self.ego_envelope_radius_m, self.pedestrian_diameter_m,
                  self.position_uncertainty_m, self.velocity_uncertainty_mps, self.horizon_s)
        if any(isinstance(value, bool) or not math.isfinite(value) or value < 0
               for value in values):
            raise ValueError("Crossing bounds must be finite and non-negative")
        if (self.ego_envelope_radius_m <= 0 or self.pedestrian_diameter_m <= 0
                or not 0 < self.horizon_s <= 2 or not self.provenance.strip()):
            raise ValueError("Crossing envelopes, short horizon and provenance are required")


def crossing_hazard(
    frame: SensorObservation, motions: tuple[ObservedMotion, ...], *,
    receipt_age_s: float, margin_m: float, policy: CrossingPolicy,
) -> bool | None:
    """True=predicted contact, None=insufficient observation, False=no prediction.

    The caller must validate freshness, validity and ego pose tick first. No
    forward-sector filter: lateral/rear crossings are relevant to this gate.
    Unknown motion does not authorize driving; unseen objects are not fabricated.
    """
    pedestrians = [hit for hit in frame.detections
                   if hit.entity_class is SensorEntityClass.PEDESTRIAN]
    if not pedestrians:
        return False
    if (frame.observed_time_s is None or any(hit.entity_id is None for hit in pedestrians)
            or not math.isfinite(receipt_age_s) or receipt_age_s < 0
            or not math.isfinite(margin_m) or margin_m < 0):
        return None
    current = {motion.entity_id: motion for motion in motions
               if motion.observed_time_s == frame.observed_time_s}
    identities = {hit.entity_id for hit in pedestrians}
    if not identities.issubset(current):
        return None
    # Never extend the constant-velocity prediction beyond two capture seconds.
    horizon = min(2.0, policy.horizon_s + receipt_age_s)
    radius = (policy.ego_envelope_radius_m + policy.pedestrian_diameter_m + margin_m
              + policy.position_uncertainty_m + policy.velocity_uncertainty_mps * horizon)
    for identity in identities:
        motion = current[identity]
        try:
            contact = circle_time_to_collision(
                motion.relative_position_m, motion.relative_velocity_mps, radius,
            )
        except ValueError:
            return None
        if contact <= horizon:
            return True
    return False
