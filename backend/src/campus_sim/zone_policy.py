"""Observed-density zone policy; independent of crowd prior and map geometry.

Call once per new density estimate. Missing/low-coverage observations are None,
not zero. Density must originate from deduplicated visible detections and a
validated coverage/area estimate, never Unity ground-truth population counts.
"""
from __future__ import annotations

import math
from dataclasses import dataclass
from enum import StrEnum

from pydantic import BaseModel, ConfigDict, Field, model_validator


class ZoneState(StrEnum):
    NORMAL = "NORMAL"
    CAUTION = "CAUTION"
    AVOID = "AVOID"
    CLOSED = "CLOSED"


class ZonePolicy(BaseModel):
    model_config = ConfigDict(frozen=True, extra="forbid")

    # Synthetic section 8 policy assumptions, not real crowd safety standards.
    caution_density: float = Field(default=0.2, ge=0, allow_inf_nan=False)
    avoid_density: float = Field(default=0.5, gt=0, allow_inf_nan=False)
    close_density: float = Field(default=1.0, gt=0, allow_inf_nan=False)
    reopen_density: float = Field(default=0.7, ge=0, allow_inf_nan=False)
    reopen_hold_s: float = Field(default=10.0, gt=0, allow_inf_nan=False)
    max_observation_gap_s: float = Field(default=1.0, gt=0, allow_inf_nan=False)

    @model_validator(mode="after")
    def thresholds_are_ordered(self) -> ZonePolicy:
        if not self.caution_density < self.avoid_density < self.close_density:
            raise ValueError("Zone thresholds must increase")
        if not self.caution_density <= self.reopen_density < self.close_density:
            raise ValueError("Reopen threshold must be below closure and above caution")
        return self


@dataclass(frozen=True)
class ZoneDecision:
    state: ZoneState = ZoneState.NORMAL
    updated_at_s: float | None = None
    last_observed_at_s: float | None = None
    reopen_since_s: float | None = None
    observed_density: float | None = None
    prior_density: float = 0.0


def update_zone_state(
    previous: ZoneDecision,
    *,
    now_s: float,
    observed_density: float | None,
    prior_density: float,
    in_peak_window: bool,
    explicitly_closed: bool,
    policy: ZonePolicy,
) -> ZoneDecision:
    """Immediate closure, continuous fresh-observation hold before reopening.

    Prior/peak can request avoidance but cannot claim measured closure. Unknown
    observations retain an existing closure and reset its recovery interval.
    Repeated or reversed update timestamps are rejected rather than advancing hold.
    """
    numbers = (now_s, prior_density) + (() if observed_density is None else (observed_density,))
    if any(not math.isfinite(value) or value < 0 for value in numbers):
        raise ValueError("Zone time and densities must be finite and non-negative")
    if previous.updated_at_s is not None and now_s <= previous.updated_at_s:
        raise ValueError("Zone updates must advance time")
    observed_at = now_s if observed_density is not None else previous.last_observed_at_s
    if explicitly_closed or observed_density is not None and observed_density >= policy.close_density:
        return ZoneDecision(ZoneState.CLOSED, now_s, observed_at, None,
                            observed_density, prior_density)
    reopen_since = None
    if previous.state is ZoneState.CLOSED:
        if observed_density is not None and observed_density < policy.reopen_density:
            continuous = (
                previous.last_observed_at_s is not None
                and now_s - previous.last_observed_at_s <= policy.max_observation_gap_s
            )
            reopen_since = previous.reopen_since_s if continuous else None
            if reopen_since is None:
                reopen_since = now_s
        if reopen_since is None or now_s - reopen_since < policy.reopen_hold_s:
            return ZoneDecision(ZoneState.CLOSED, now_s, observed_at, reopen_since,
                                observed_density, prior_density)

    # Prior is a conservative route-policy input, never substituted for observed density.
    effective = max(prior_density, observed_density or 0.0)
    state = ZoneState.AVOID if in_peak_window or effective >= policy.avoid_density else \
        ZoneState.CAUTION if effective >= policy.caution_density else ZoneState.NORMAL
    return ZoneDecision(state, now_s, observed_at, None, observed_density, prior_density)
