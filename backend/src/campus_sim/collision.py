"""Planar constant-relative-velocity collision prediction (AGENTS section 9).

Inputs must come from a perception estimate in one common, non-rotating frame.
This module never queries simulator actors and does not infer a velocity vector
from a radar range rate. Circular envelopes do not replace swept-body validation.
"""
from __future__ import annotations

import math
from dataclasses import dataclass


def circle_time_to_collision(
    relative_position_m: tuple[float, float],
    relative_velocity_mps: tuple[float, float],
    combined_radius_m: float,
) -> float:
    """First t >= 0 with |r + u*t| <= R; overlap=0 and no contact=inf.

    R is the sum of both collision-envelope radii and a caller-chosen margin.
    Velocity is obstacle minus ego velocity. No future acceleration is assumed.
    """
    rx, ry = relative_position_m
    ux, uy = relative_velocity_mps
    if not all(math.isfinite(v) for v in (rx, ry, ux, uy, combined_radius_m)):
        raise ValueError("Collision inputs must be finite")
    if combined_radius_m < 0:
        raise ValueError("Combined radius must be non-negative")
    distance = math.hypot(rx, ry)
    speed = math.hypot(ux, uy)
    if not math.isfinite(distance) or not math.isfinite(speed):
        raise ValueError("Collision input magnitude is not representable")
    if distance <= combined_radius_m:
        return 0.0
    if speed == 0:
        return math.inf

    # Scale position by its norm to avoid squaring large physical coordinates.
    px, py = rx / distance, ry / distance
    vx, vy = ux / speed, uy / speed
    along = px * vx + py * vy
    if along >= 0:
        return math.inf
    perpendicular = px * vy - py * vx
    radius = combined_radius_m / distance
    discriminant = radius * radius - perpendicular * perpendicular
    if discriminant < 0:
        return math.inf
    half_chord = math.sqrt(discriminant)
    # Rationalized entry root avoids cancellation close to current contact.
    entry_fraction = ((1.0 - radius) * (1.0 + radius)) / (-along + half_chord)
    return entry_fraction * (distance / speed)


@dataclass(frozen=True)
class CollisionPrediction:
    time_to_contact_s: float
    within_horizon: bool


def predict_circle_contact(
    relative_position_m: tuple[float, float],
    relative_velocity_mps: tuple[float, float],
    combined_radius_m: float,
    *,
    horizon_s: float,
) -> CollisionPrediction:
    """Explicit short-horizon result; absence of predicted contact is not safety.

    The project's constant-velocity approximation is limited to two seconds.
    A later analytic intersection is retained for diagnostics, not execution.
    """
    if not math.isfinite(horizon_s) or not 0 < horizon_s <= 2.0:
        raise ValueError("Prediction horizon must be in (0, 2] seconds")
    ttc = circle_time_to_collision(
        relative_position_m, relative_velocity_mps, combined_radius_m,
    )
    return CollisionPrediction(ttc, ttc <= horizon_s)
