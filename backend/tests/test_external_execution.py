import math

import pytest

from campus_sim.domain import CreateRequest, RequestStatus
from campus_sim.service import MobilityService
from campus_sim.unity_drive import AutoMission
from campus_sim.web_demo import DemoSession
from test_lanes import lane_ego


@pytest.mark.parametrize('policy',['fifo','greedy','hungarian'])
def test_external_request_never_enters_virtual_trip_or_time_completion(policy):
    service=MobilityService.synthetic_fixture(clock=lambda:0)
    service.dispatch_algorithm=policy
    request=service.create_request(CreateRequest(command_id='external',owner_id='test',
        service_type='PASSENGER',pickup_landmark_id='main_gate',dropoff_landmark_id='library'),
        external_execution=True).request
    service.step(10000)
    assert request.status==RequestStatus.QUEUED and request.vehicle_id is None
    assert request.id not in service.trips
    assert service.waiting_reasons[request.id]=='WAITING_EXTERNAL_EXECUTOR'
    normal=service.create_request(CreateRequest(command_id='virtual',owner_id='test',
        service_type='PASSENGER',pickup_landmark_id='main_gate',dropoff_landmark_id='hitech')).request
    assert normal.status==RequestStatus.ASSIGNED
    assert normal.id in service.trips


def test_virtual_dispatch_preserves_observed_executor_waiting_reason():
    service=MobilityService.synthetic_fixture(clock=lambda:0)
    r=service.create_request(CreateRequest(command_id='reason',owner_id='test',
        service_type='PASSENGER',pickup_landmark_id='main_gate',dropoff_landmark_id='library'),
        external_execution=True).request
    service.waiting_reasons[r.id]='No directed return route'
    service.step(1)
    assert service.waiting_reasons[r.id]=='No directed return route'
    assert r.status==RequestStatus.QUEUED


def test_physics_preview_keeps_live_pose_mission_and_clock_even_with_turn():
    s=DemoSession(); p=s.physical; token=p.connect('V01')
    ego=lane_ego(s,schema_version=4,rear_axle_offset_m=-.65,half_length_m=1.55,
        half_width_m=.46,obstacle_detection_enabled=False,rays=[])
    p.observation('V01',token,ego)
    live=p.vehicles['V01']
    clock_before=dict(s.clock.__dict__)
    command=AutoMission(run_id=s.run_id,command_id='preview',stop_id='hitech_road_candidate',
        driving_profile='campus_fast_no_obstacles')
    predicted=p.preview_mission('V01',command)
    assert predicted is not live and predicted.route_unity
    a,b=predicted.route_unity[-2:]
    endpoint=ego.model_copy(update=dict(x=b[0],z=b[1],heading_rad=math.atan2(b[0]-a[0],b[1]-a[1])))
    onward=p.preview_mission('V01',command.model_copy(update=dict(stop_id='library_road_candidate')),
        origin=endpoint)
    assert onward.turn is not None
    assert p.vehicles['V01'] is live and live.ego is ego
    assert live.route_unity==[] and live.mission_id is None and live.turn is None
    assert s.clock.__dict__==clock_before
    with pytest.raises(ValueError):
        p.preview_mission('V01',command.model_copy(update=dict(stop_id='missing')))
    assert p.vehicles['V01'] is live and s.clock.__dict__==clock_before
