import math
from itertools import pairwise

import pytest

from campus_sim.local_rrt import DiscObstacle, RectCorridor, plan_rrt, transition_clear
from campus_sim.swept_geometry import BoxFootprint, Pose2, swept_box_hits_disc


@pytest.mark.parametrize("moving_obstacle", [False, True])
def test_sweep_detects_between_endpoint_collision(moving_obstacle):
    box = BoxFootprint(1, 0.5)
    start, end = Pose2(-5, 0, math.pi / 2), Pose2(5, 0, math.pi / 2)
    a, b = ((5, 0), (-5, 0)) if moving_obstacle else ((0, 0), (0, 0))
    assert not swept_box_hits_disc(start, start, box, a, a, 0.2)
    assert not swept_box_hits_disc(end, end, box, b, b, 0.2)
    assert swept_box_hits_disc(start, end, box, a, b, 0.2)


def test_round_corner_does_not_use_square_expansion():
    pose, box = Pose2(0, 0, 0), BoxFootprint(1, 1)
    assert not swept_box_hits_disc(pose, pose, box, (0.65, 0.65), (0.65, 0.65), 0.2)
    assert swept_box_hits_disc(pose, pose, box, (0.64, 0.64), (0.64, 0.64), 0.2)


def test_rotating_long_body_hits_between_clear_endpoint_orientations():
    box = BoxFootprint(4, 0.4)
    start, end = Pose2(0, 0, 0), Pose2(0, 0, math.pi / 2)
    obstacle = (1.2, 1.2)
    assert not swept_box_hits_disc(start, start, box, obstacle, obstacle, 0.1)
    assert not swept_box_hits_disc(end, end, box, obstacle, obstacle, 0.1)
    assert swept_box_hits_disc(start, end, box, obstacle, obstacle, 0.1)
    assert not swept_box_hits_disc(start, end, box, (3, 3), (3, 3), 0.1)


def test_yaw_wrap_uses_shortest_arc():
    box = BoxFootprint(4, 0.4)
    a, b = Pose2(0, 0, math.pi - 0.01), Pose2(0, 0, -math.pi + 0.01)
    assert not swept_box_hits_disc(a, b, box, (1.5, 0), (1.5, 0), 0.1)


@pytest.mark.parametrize("offset,hit", [(0.7, True), (0.7001, False)])
def test_grazing_translation_contact_is_inclusive(offset, hit):
    assert swept_box_hits_disc(Pose2(-5, 0, 0), Pose2(5, 0, 0), BoxFootprint(1, 1),
                               (0, offset), (0, offset), 0.2) is hit


def test_rrt_detours_reproducibly_with_checked_translation_and_turns():
    start, goal = Pose2(-4, 0, math.pi / 2), Pose2(4, 0, math.pi / 2)
    footprint, corridor = BoxFootprint(0.6, 0.4), RectCorridor(-5, -3, 5, 3)
    obstacles = (DiscObstacle(0, 0, 0.8),)
    args = {"footprint": footprint, "corridor": corridor, "obstacles": obstacles, "seed": 17}
    assert not transition_clear(start, goal, footprint, corridor, obstacles, 0.1)
    path = plan_rrt(start, goal, **args)
    assert path is not None
    assert path == plan_rrt(start, goal, **args)
    assert (path[0], path[-1]) == (start, goal)
    assert any(abs(p.y) > 1 for p in path)
    for a, b in pairwise(path):
        assert transition_clear(a, b, footprint, corridor, obstacles, 0.1)
        assert (a.x, a.y) == (b.x, b.y) or a.heading_rad == b.heading_rad


def test_blocked_corridor_and_invalid_start_never_return_unchecked_fallback():
    box, corridor = BoxFootprint(0.6, 0.4), RectCorridor(-3, -2, 3, 2)
    wall = tuple(DiscObstacle(0, y / 2, 0.4) for y in range(-4, 5))
    assert plan_rrt(Pose2(-2, 0, 0), Pose2(2, 0, 0), footprint=box,
                    corridor=corridor, obstacles=wall, seed=1, iterations=200) is None
    assert plan_rrt(Pose2(0, 0, 0), Pose2(2, 0, 0), footprint=box,
                    corridor=corridor, obstacles=wall, seed=1) is None


def test_checked_shortcuts_remove_open_space_stop_turn_zigzags():
    start, goal = Pose2(0, 0, math.pi / 2), Pose2(6, 0, math.pi / 2)
    path = plan_rrt(start, goal, footprint=BoxFootprint(0.4, 0.4),
                    corridor=RectCorridor(-1, -3, 7, 3), obstacles=(), seed=17)
    assert path == (start, goal)


@pytest.mark.parametrize("bad", [-1, math.nan, math.inf])
def test_invalid_geometry_is_rejected(bad):
    with pytest.raises(ValueError):
        BoxFootprint(bad, 1)
    with pytest.raises(ValueError):
        swept_box_hits_disc(Pose2(0, 0, 0), Pose2(1, 0, 0), BoxFootprint(1, 1),
                            (0, 0), (0, 0), bad)
