import math

import pytest
from test_controller import ready_service

from campus_sim.realtime import make_snapshot
from campus_sim.reservations import ResourceReservations
from campus_sim.resource_admission import AdmissionPolicy, ResourceAdmission
from campus_sim.resource_occupancy import OccupancyPolicy, ResourceOccupancyTracker, ResourceRegion
from campus_sim.swept_geometry import BoxFootprint


def waiting_service():
    service = ready_service()
    book = ResourceReservations({"corridor"}, map_version=service.graph.map_version)
    monitor = ResourceOccupancyTracker(book, (ResourceRegion("corridor", 1, -0.6, 2, 0.6, "synthetic"),),
        {"V01": OccupancyPolicy(BoxFootprint(0.4, 0.4), 0.02, 2, 0.5, 0.2, "synthetic")})
    monitor.observe(service.ego_localizations["V01"], service.now_s())
    book.enqueue("blocker", "other", {"corridor"}, now_s=service.now_s(), lease_s=30)
    lease, = book.advance(service.now_s())
    service.resource_occupancy = monitor
    service.resource_admission = ResourceAdmission(monitor, {"corridor": frozenset({"corridor"})},
        AdmissionPolicy(0.4, 2, 0.1, 10, 30, "synthetic"))
    service.resource_admission.sync(service)
    return service, lease


@pytest.mark.parametrize("speed, motion", [(0, "WAITING_RESOURCE"), (0.4, "YIELDING")])
def test_both_roles_show_same_wait_and_unknown_eta_without_mobile_issuing_control(speed, motion):
    service, _lease = waiting_service()
    runtime = service.vehicle_runtime["V01"]
    runtime.x = runtime.route_progress_m = 0.1
    runtime.speed_mps = speed
    # Mobile can be the first subscriber to evaluate this tick's presentation.
    mobile = make_snapshot(service, "Mobile_Passenger", "test", 1)
    assert not service.control_sequences
    pc = make_snapshot(service, "PC_Operator", None, 1)
    assert pc["vehicles"][0] == mobile["vehicles"][0]
    assert mobile["vehicles"][0]["reason"] == "RESOURCE_WAIT"
    assert mobile["vehicles"][0]["motionState"] == motion
    assert mobile["vehicles"][0]["speedMps"] == speed  # Never overwrite measured motion.
    assert mobile["requests"][0]["etaS"] is None
    assert pc["requests"][0]["etaS"] is None
    assert pc["controlCommands"][0]["targetSpeedMps"] == 0
    assert not mobile["controlCommands"]
    sequences = dict(service.control_sequences)
    assert not make_snapshot(service, "Mobile_Passenger", "unrelated-owner", 2)["vehicles"]
    assert service.control_sequences == sequences


def test_approach_and_alignment_keep_actuator_authority_until_hold_then_release_restores_eta():
    service, lease = waiting_service()
    runtime = service.vehicle_runtime["V01"]
    runtime.heading_rad = 0  # Rotate toward the eastbound path at rest.
    pc = make_snapshot(service, "PC_Operator", None, 1)
    assert pc["controlCommands"][0]["yawRateRadps"] > 0
    assert pc["vehicles"][0]["motionState"] == "DRIVING"
    assert pc["vehicles"][0]["reason"] == "RESOURCE_WAIT"
    runtime.heading_rad = math.pi / 2
    pc = make_snapshot(service, "PC_Operator", None, 2)
    assert pc["controlCommands"][0]["targetSpeedMps"] > 0
    assert pc["vehicles"][0]["motionState"] == "DRIVING"
    book = service.resource_occupancy.book
    book.cancel(lease.token, "other", now_s=service.now_s())
    service.resource_admission.sync(service)
    mobile = make_snapshot(service, "Mobile_Passenger", "test", 3)
    assert mobile["vehicles"][0]["reason"] == "UNKNOWN"
    assert mobile["requests"][0]["etaS"] is not None


def test_resource_fault_is_visible_but_sensor_failure_keeps_priority():
    service, _lease = waiting_service()
    service.resource_occupancy.faults["V01"] = "discontinuous_localization"
    mobile = make_snapshot(service, "Mobile_Passenger", "test", 1)
    assert mobile["vehicles"][0]["reason"] == "RESOURCE_STATE_UNAVAILABLE"
    assert mobile["vehicles"][0]["motionState"] == "WAITING_RESOURCE"
    assert mobile["requests"][0]["etaS"] is None
    frame = service.sensor_observations[("V01", "lidar")].model_copy(update={"valid": False, "observed_tick": 2})
    service.record_sensor_observation(frame)
    mobile = make_snapshot(service, "Mobile_Passenger", "test", 2)
    assert mobile["vehicles"][0]["reason"] == "SENSOR_INVALID"
    assert mobile["vehicles"][0]["motionState"] == "EMERGENCY_STOP"
    assert mobile["requests"][0]["etaS"] is None
