import math
from dataclasses import replace

import pytest

from campus_sim.domain import EgoLocalization, MapPosition
from campus_sim.reservations import ResourceReservations
from campus_sim.resource_occupancy import (
    OccupancyPolicy,
    ResourceOccupancyTracker,
    ResourceRegion,
    footprint_overlaps_region,
)
from campus_sim.service import MobilityService
from campus_sim.swept_geometry import BoxFootprint, Pose2

REGION = ResourceRegion("corridor", 1, -1, 2, 1, "Synthetic axis-aligned test rectangle")
POLICY = OccupancyPolicy(BoxFootprint(1, 0.4), 0, 5, 0.5, 0.2,
                         "Synthetic centered body, receipt-clock speed and exit confirmation")


def observation(x, tick, *, session="s", version="synthetic-test", heading=math.pi / 2):
    return EgoLocalization(vehicle_id="V01", session_id=session, observed_tick=tick,
                           map_version=version, position=MapPosition(x=x, y=0, z=0),
                           heading_rad=heading, speed_mps=0)


def tracker():
    book = ResourceReservations({"corridor"}, map_version="synthetic-test")
    return ResourceOccupancyTracker(book, (REGION,), {"V01": POLICY})


def grant(tracker):
    tracker.book.enqueue("r1", "V01", {"corridor"}, now_s=0, lease_s=10)
    return tracker.book.advance(0)[0]


def test_front_and_rear_not_center_determine_occupancy():
    assert footprint_overlaps_region(Pose2(0.6, 0, math.pi / 2), POLICY.footprint, REGION)
    assert footprint_overlaps_region(Pose2(2.4, 0, math.pi / 2), POLICY.footprint, REGION)
    assert not footprint_overlaps_region(Pose2(2.51, 0, math.pi / 2), POLICY.footprint, REGION)
    assert footprint_overlaps_region(Pose2(2.51, 0, math.pi / 2), POLICY.footprint, REGION, 0.02)


def test_rotation_changes_long_body_support_and_corner_projection_is_checked():
    box = BoxFootprint(4, 0.2)
    assert not footprint_overlaps_region(Pose2(0, 0, 0), box, REGION)
    assert footprint_overlaps_region(Pose2(0, 0, math.pi / 2), box, REGION)
    assert footprint_overlaps_region(Pose2(0, 0, math.pi / 4), box, REGION)
    assert not footprint_overlaps_region(Pose2(0, -4, math.pi / 4), box, REGION)


def test_exit_requires_whole_body_clear_and_repeated_clear_reports():
    monitor = tracker()
    permit = grant(monitor)
    assert monitor.observe(observation(0, 0), 0)
    assert monitor.observe(observation(1, 1), 0.2)
    assert monitor.observe(observation(2.4, 2), 0.5)
    assert monitor.book.leases[permit.token].occupied == {"corridor"}
    assert monitor.observe(observation(3.5, 3), 0.8)
    assert permit.token in monitor.book.leases
    assert monitor.observe(observation(3.5, 4), 0.9)
    assert monitor.observe(observation(3.5, 5), 1.1)
    assert permit.token not in monitor.book.leases


@pytest.mark.parametrize("failure", ["stale", "session", "jump", "map"])
def test_discontinuous_context_never_releases_known_occupancy(failure):
    monitor = tracker()
    permit = grant(monitor)
    monitor.observe(observation(1.5, 0), 0)
    if failure == "stale":
        monitor.check_freshness(0.6)
    elif failure == "session":
        assert not monitor.observe(observation(3.5, 1, session="new"), 0.4)
    elif failure == "jump":
        assert not monitor.observe(observation(100, 1), 0.1)
    else:
        assert not monitor.observe(observation(3.5, 1, version="other"), 0.4)
    assert monitor.book.leases[permit.token].occupied == {"corridor"}
    assert monitor.book.closed == {"corridor"}
    assert monitor.faults
    assert not monitor.observe(observation(4, 100), 1)


def test_replayed_pose_neither_releases_nor_refreshes_freshness():
    monitor = tracker()
    permit = grant(monitor)
    monitor.observe(observation(1.5, 1), 0)
    assert not monitor.observe(observation(3.5, 1), 0.4)
    monitor.check_freshness(0.6)
    assert monitor.book.leases[permit.token].occupied == {"corridor"}
    assert monitor.faults["V01"] == "stale_localization"


def test_crossing_between_clear_endpoints_is_conservatively_recorded():
    book = ResourceReservations({"thin"}, map_version="synthetic-test")
    thin = ResourceRegion("thin", 1, -1, 1.1, 1, "Synthetic narrow strip")
    policy = replace(POLICY, footprint=BoxFootprint(0.1, 0.1))
    monitor = ResourceOccupancyTracker(book, (thin,), {"V01": policy})
    monitor.observe(observation(0.8, 0), 0)
    monitor.observe(observation(1.3, 1), 0.1)
    assert monitor.held["V01"] == {"thin"}
    assert book.unplanned_occupancy["V01"] == {"thin"}
    assert book.closed == {"thin"}


def test_production_service_accepted_ego_ingress_updates_reservations():
    service = MobilityService.synthetic_fixture()
    book = ResourceReservations({"corridor"}, map_version=service.graph.map_version)
    monitor = ResourceOccupancyTracker(book, (REGION,), {"V01": POLICY})
    service.resource_occupancy = monitor
    permit = grant(monitor)
    assert service.record_ego_localization(observation(1.5, 1, version=service.graph.map_version))
    assert book.leases[permit.token].occupied == {"corridor"}
    assert not service.record_ego_localization(observation(5, 1, version=service.graph.map_version))
    assert book.leases[permit.token].occupied == {"corridor"}


def test_new_tracker_cannot_silently_discard_an_existing_occupant():
    monitor = tracker()
    grant(monitor)
    monitor.observe(observation(1.5, 1), 0)
    with pytest.raises(ValueError, match="recovery"):
        ResourceOccupancyTracker(monitor.book, (REGION,), {"V01": POLICY})
