import math

import pytest

from campus_sim.collision import circle_time_to_collision, predict_circle_contact


@pytest.mark.parametrize("position,velocity,radius,expected", [
    ((0, 0), (0, 0), 1, 0),
    ((1, 0), (1, 0), 1, 0),  # Contact already exists, even when separating.
    ((5, 0), (-2, 0), 1, 2),
    ((5, 0), (2, 0), 1, math.inf),
    ((5, 0), (0, 0), 1, math.inf),
    ((3, 4), (-3, -4), 1, 0.8),
    ((2, 1), (-1, 0), 1, 2),  # Tangential contact.
    ((2, 1.001), (-1, 0), 1, math.inf),
    ((2, 0), (-1, 0), 0, 2),  # Point envelope.
    ((0, 2), (0, -1), 0.5, 1.5),  # Crossing from the side.
])
def test_analytic_contact_cases(position, velocity, radius, expected):
    assert circle_time_to_collision(position, velocity, radius) == pytest.approx(expected)


def test_rotation_and_common_spatial_scale_preserve_contact_time():
    position, velocity, radius = (4.0, 1.0), (-2.0, -0.5), 0.6
    expected = circle_time_to_collision(position, velocity, radius)
    for angle in (0.4, 1.3, -2.4):
        def rotate(vector, angle=angle):
            x, y = vector
            return x * math.cos(angle) - y * math.sin(angle), \
                x * math.sin(angle) + y * math.cos(angle)
        for scale in (1e-100, 1.0, 1e100):
            p = tuple(value * scale for value in rotate(position))
            u = tuple(value * scale for value in rotate(velocity))
            assert circle_time_to_collision(p, u, radius * scale) == pytest.approx(expected)


def test_contact_root_lies_on_envelope_and_earlier_point_is_outside():
    r, u, radius = (3.0, 1.0), (-2.0, -0.25), 1.0
    ttc = circle_time_to_collision(r, u, radius)
    assert math.isfinite(ttc)
    assert math.hypot(*(r[i] + u[i] * ttc for i in range(2))) == pytest.approx(radius)
    assert math.hypot(*(r[i] + u[i] * (ttc - 0.001) for i in range(2))) > radius


def test_horizon_is_inclusive_but_not_extended_to_later_collisions():
    assert predict_circle_contact((5, 0), (-2, 0), 1, horizon_s=2).within_horizon
    later = predict_circle_contact((6, 0), (-2, 0), 1, horizon_s=2)
    assert later.time_to_contact_s == 2.5
    assert not later.within_horizon


@pytest.mark.parametrize("horizon", [0, -1, 2.01, math.inf, math.nan])
def test_invalid_horizons_are_rejected(horizon):
    with pytest.raises(ValueError):
        predict_circle_contact((2, 0), (-1, 0), 1, horizon_s=horizon)


@pytest.mark.parametrize("position,velocity,radius", [
    ((math.nan, 0), (0, 0), 1),
    ((1, 0), (math.inf, 0), 1),
    ((1, 0), (0, 0), -1),
    ((1, 0), (0, 0), math.inf),
])
def test_invalid_inputs_cannot_report_clear(position, velocity, radius):
    with pytest.raises(ValueError):
        circle_time_to_collision(position, velocity, radius)
