import math

import pytest

from campus_sim.zone_policy import ZoneDecision, ZonePolicy, ZoneState, update_zone_state


def update(previous, time, density, *, prior=0.0, peak=False, closed=False):
    return update_zone_state(previous, now_s=time, observed_density=density,
                             prior_density=prior, in_peak_window=peak,
                             explicitly_closed=closed, policy=ZonePolicy())


@pytest.mark.parametrize("density,state", [(0.19, "NORMAL"), (0.2, "CAUTION"),
                                          (0.5, "AVOID"), (1.0, "CLOSED")])
def test_immediate_observed_thresholds(density, state):
    assert update(ZoneDecision(), 0, density).state == state


def test_closure_reopens_only_after_ten_seconds_of_continuous_low_observations():
    state = update(ZoneDecision(), 0, 1.0)
    for t in range(1, 11):
        state = update(state, t, 0.6)
        assert state.state == ZoneState.CLOSED
    state = update(state, 11, 0.6)
    assert state.state == ZoneState.AVOID  # Reopening does not mean uncongested.


@pytest.mark.parametrize("interruption", [None, 0.7, 1.0])
def test_unknown_boundary_or_renewed_hazard_resets_clear_interval(interruption):
    state = update(ZoneDecision(), 0, 1.0)
    for t in range(1, 10):
        state = update(state, t, 0.1)
    state = update(state, 10, interruption)
    assert state.state == ZoneState.CLOSED
    assert state.reopen_since_s is None
    assert update(state, 11, 0.1).state == ZoneState.CLOSED


def test_long_observation_gap_cannot_be_counted_as_clear_time():
    state = update(ZoneDecision(), 0, 1.0)
    state = update(state, 1, 0.1)
    state = update(state, 20, 0.1)
    assert state.state == ZoneState.CLOSED
    assert state.reopen_since_s == 20


def test_prior_and_peak_do_not_fabricate_observed_closure():
    state = update(ZoneDecision(), 0, None, prior=2)
    assert state.state == ZoneState.AVOID
    assert state.observed_density is None
    assert update(state, 1, None, peak=True).state == ZoneState.AVOID
    state = update(state, 2, None, closed=True)
    assert state.state == ZoneState.CLOSED
    assert update(state, 20, None).state == ZoneState.CLOSED


@pytest.mark.parametrize("time,density", [(-1, 0), (0, -1), (math.nan, 0), (0, math.inf)])
def test_invalid_inputs_are_rejected(time, density):
    with pytest.raises(ValueError):
        update(ZoneDecision(), time, density)


def test_replayed_time_does_not_advance_hold():
    state = update(ZoneDecision(), 1, 1)
    with pytest.raises(ValueError):
        update(state, 1, 0)
