import math

import pytest

from campus_sim.turnaround import three_point_turn


def geometry(**changes):
    values = dict(lane_offset_m=1.5,road_half_width_m=3.,forward_space_m=5.,
        rear_space_m=3.,minimum_radius_m=2.3,half_width_m=.4,half_length_m=1.1)
    values.update(changes)
    return three_point_turn(**values)


def test_three_point_turn_reaches_opposite_lane_with_continuous_cusps():
    arcs = geometry()
    assert [a[0].direction for a in arcs] == [1,-1,1]
    for a,b in zip(arcs,arcs[1:]):
        assert (a[-1].x,a[-1].z,a[-1].heading_rad) == pytest.approx(
            (b[0].x,b[0].z,b[0].heading_rad))
    end=arcs[-1][-1]
    assert (end.x,end.z,end.heading_rad) == pytest.approx((-1.5,0,-math.pi))
    assert max(abs(p.curvature) for a in arcs for p in a) <= 1/2.3


def test_samples_follow_signed_bicycle_motion_and_body_stays_in_bounds():
    for arc in geometry():
        for a,b in zip(arc,arc[1:]):
            middle_heading=(a.heading_rad+b.heading_rad)/2
            dx,dz=b.x-a.x,b.z-a.z
            forward=dx*math.sin(middle_heading)+dz*math.cos(middle_heading)
            lateral=dx*math.cos(middle_heading)-dz*math.sin(middle_heading)
            assert forward*a.direction > 0
            assert abs(lateral) < 1e-10
            assert math.hypot(dx,dz) <= .05
        for p in arc:
            for right in (-.4,.4):
                for front in (-1.1,1.1):
                    x=p.x+right*math.cos(p.heading_rad)+front*math.sin(p.heading_rad)
                    z=p.z-right*math.sin(p.heading_rad)+front*math.cos(p.heading_rad)
                    assert -2.8 <= x <= 2.8
                    assert -2.8 <= z <= 4.8


def test_measured_v01_body_fits_six_metre_road_with_explicit_space():
    # MCP measurement: body z +/-0.9, rear axle z -0.65; bounding half length
    # about the bicycle reference must therefore be 1.55, not 0.9.
    arcs = geometry(minimum_radius_m=1.299996/math.tan(.5235988),
                    half_width_m=.46001, half_length_m=1.55)
    assert arcs[-1][-1].x == pytest.approx(-1.5)


@pytest.mark.parametrize('changes', [dict(road_half_width_m=1.9),
    dict(forward_space_m=1.), dict(rear_space_m=.5), dict(half_length_m=4.)])
def test_insufficient_swept_body_space_is_rejected(changes):
    with pytest.raises(ValueError,match='footprint'):
        geometry(**changes)


@pytest.mark.parametrize('changes', [dict(minimum_radius_m=0),dict(half_width_m=float('nan')),
    dict(sample_step_m=-1),dict(lane_offset_m=3)])
def test_invalid_dimensions_are_rejected(changes):
    with pytest.raises(ValueError):
        geometry(**changes)
