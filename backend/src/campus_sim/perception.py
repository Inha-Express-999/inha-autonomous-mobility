"""Bounded visible-return summaries, never ground-truth population counts."""
import math
from collections.abc import Sequence
from dataclasses import dataclass

from campus_sim.domain import SensorEntityClass
from campus_sim.safety import SensorSample


@dataclass(frozen=True)
class PedestrianReturnSummary:
    entity_ids: frozenset[str]
    anonymous_return_count: int
    usable_frame_count: int


def summarize_pedestrian_returns(
    samples: Sequence[SensorSample], *, now_s: float, max_age_s: float,
) -> PedestrianReturnSummary:
    """Deduplicate fresh visible identities within one simulation session/map.

    Anonymous ray returns cannot be interpreted as people. Even an empty usable
    frame says nothing about unobserved/occluded regions or zone coverage.
    """
    if not math.isfinite(now_s) or not math.isfinite(max_age_s) or now_s < 0 or max_age_s <= 0:
        raise ValueError("Observation time/maximum age must be finite and valid")
    contexts = set()
    ids = set()
    anonymous = usable = 0
    for sample in samples:
        frame, received_at = sample.observation, sample.received_at_s
        if (not frame.valid or received_at is None or not math.isfinite(received_at)
                or not 0 <= now_s - received_at <= max_age_s):
            continue
        contexts.add((frame.session_id, frame.map_version))
        if len(contexts) > 1:
            raise ValueError("Cannot merge identities across simulation sessions/maps")
        usable += 1
        for detection in frame.detections:
            if detection.entity_class is not SensorEntityClass.PEDESTRIAN:
                continue
            if detection.entity_id is None:
                anonymous += 1
            else:
                ids.add(detection.entity_id)
    return PedestrianReturnSummary(frozenset(ids), anonymous, usable)
