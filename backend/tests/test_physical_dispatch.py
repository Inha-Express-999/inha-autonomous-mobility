from campus_sim.domain import CreateRequest,RequestStatus
from campus_sim.web_demo import DemoSession
from test_lanes import lane_ego
import pytest


def setup():
    wall=[0.]
    s=DemoSession(wall_clock=lambda:wall[0]); p=s.physical; p.clock=lambda:wall[0]
    token=p.connect('V01')
    p.observation('V01',token,lane_ego(s,schema_version=4,rear_axle_offset_m=-.65,
        half_length_m=1.55,half_width_m=.46,obstacle_detection_enabled=False,rays=[]))
    return s,p,token,wall


def request(s,**changes):
    data=dict(command_id='passenger',owner_id='test',service_type='PASSENGER',
              pickup_landmark_id='library',dropoff_landmark_id='hitech')
    data.update(changes)
    return s.submit(CreateRequest(**data)).request


def observe(s,p,token,wall,*,arrive=False):
    wall[0]+=.1
    vehicle=p.vehicles['V01']; ego=vehicle.ego
    updates=dict(tick=ego.tick+1,speed_mps=0.)
    if arrive:
        # Transport-state fixture only: acknowledge completion of any turn.
        # TurnController/Physics tracking is exercised separately; this test
        # supplies synthetic arrival observations rather than a driving trace.
        vehicle.turn=None
        import math
        a,b=vehicle.route_unity[-2:]
        updates.update(x=b[0],z=b[1],heading_rad=math.atan2(b[0]-a[0],b[1]-a[1]))
    p.observation('V01',token,ego.model_copy(update=updates))
    s.advance()


def test_recorded_main_gate_spawn_can_dispatch_and_keeps_its_home():
    s,p,token,wall=setup()
    p.observation('V01',token,p.vehicles['V01'].ego.model_copy(update=dict(
        tick=2,x=-41.65241241455078,z=-320.0533142089844,
        heading_rad=-2.5147006511688232,speed_mps=0.)))
    r=request(s,pickup_landmark_id='anniversary_60',dropoff_landmark_id='biryong_plaza')
    assert r.status==RequestStatus.ASSIGNED
    assert s.physical_dispatch.jobs[r.id].home_stop=='main_gate_road_candidate'
    assert (p.vehicles['V01'].ego.x,p.vehicles['V01'].ego.z)==(-41.65241241455078,-320.0533142089844)


@pytest.mark.parametrize('policy,expected',[('fifo','V01'),('greedy','V02')])
def test_physical_policy_selects_fifo_or_lower_feasible_cost(monkeypatch,policy,expected):
    s,p,token,wall=setup()
    second=p.connect('V02')
    p.observation('V02',second,p.vehicles['V01'].ego.model_copy())
    s.service.dispatch_algorithm=policy
    # Selection fixture: both real mission planners start at the same valid
    # localization, while controlled estimates distinguish FIFO from cost.
    estimates=[]
    def estimate(vehicle,r):
        estimates.append(vehicle)
        return 'main_gate_road_candidate',10.,[100. if vehicle=='V01' else 10.,20.,20.]
    monkeypatch.setattr(s.physical_dispatch,'_estimate',estimate)
    r=request(s)
    assert len(estimates)<=1 and r.vehicle_id is None
    for _ in range(4):
        before=len(estimates)
        wall[0]+=.11
        for identifier in ('V01','V02'):
            v=p.vehicles[identifier]
            p.observation(identifier,v.token,v.ego.model_copy(update={'tick':v.ego.tick+1}))
        s.physical_dispatch.step()
        assert len(estimates)-before<=1
    assert r.vehicle_id==expected
    assert s.state()['physical_dispatch']==policy


def test_policy_change_does_not_replace_active_physical_mission():
    from campus_sim.web_demo import DemoControl
    s,p,token,wall=setup(); r=request(s)
    mission=p.vehicles['V01'].mission_id
    s.control(DemoControl(dispatch='greedy'))
    assert s.physical_dispatch.jobs[r.id].mission_id==mission
    assert p.vehicles['V01'].mission_id==mission
    assert r.vehicle_id=='V01'


def test_vehicle_home_goal_is_bound_to_own_existing_road_node():
    from campus_sim.unity_drive import AutoMission
    s,p,token,wall=setup()
    with pytest.raises(ValueError,match='Unknown Stop'):
        p.preview_mission('V01',AutoMission(run_id=s.run_id,command_id='wrong',stop_id='home:V03'))
    plan=p.preview_mission('V01',AutoMission(run_id=s.run_id,command_id='own',stop_id='home:V01'))
    assert plan.destination=='main_gate'


def test_preview_can_use_snapshot_but_start_still_requires_fresh_pose():
    from campus_sim.unity_drive import AutoMission
    s,p,token,wall=setup();wall[0]+=1
    command=AutoMission(run_id=s.run_id,command_id='snapshot',stop_id='home:V01')
    p.preview_mission('V01',command)
    assert p.vehicles['V01'].mission_id is None
    with pytest.raises(ValueError,match='Fresh Unity localization'):
        p.mission('V01',command)


def test_observed_transport_requires_arrival_dwell_and_return():
    s,p,token,wall=setup(); r=request(s)
    assert r.status==RequestStatus.ASSIGNED and r.vehicle_id=='V01'
    assert r.id not in s.service.trips
    job=s.physical_dispatch.jobs[r.id]
    assert job.phase=='TO_PICKUP' and job.budget_wh>0
    wall[0]+=1000; s.advance()
    assert r.status==RequestStatus.ASSIGNED
    observe(s,p,token,wall,arrive=True)
    assert r.status==RequestStatus.PICKUP_SERVICE
    assert job.reason==''
    wall[0]+=1000; s.advance()
    assert r.status==RequestStatus.PICKUP_SERVICE
    for _ in range(33): observe(s,p,token,wall)
    assert r.status==RequestStatus.IN_TRANSIT and job.phase=='TO_DROPOFF'
    observe(s,p,token,wall,arrive=True)
    assert r.status==RequestStatus.DROPOFF_SERVICE
    for _ in range(33): observe(s,p,token,wall)
    assert r.status==RequestStatus.COMPLETED and job.phase=='RETURNING'
    assert job.home_stop=='main_gate_road_candidate'
    assert p.vehicles['V01'].destination=='main_gate'
    assert s.physical_dispatch.busy('V01')
    observe(s,p,token,wall,arrive=True)
    assert job.returned and not s.physical_dispatch.busy('V01')
    assert job.consumed_wh>0 and s.service.batteries['V01'].remaining_wh>0
    assert r.id not in s.service.trips


def test_transport_reservation_is_reported_until_return_and_blocks_manual_mission():
    from fastapi.testclient import TestClient
    from campus_sim.api import create_app

    s,p,token,wall=setup(); r=request(s)
    job=s.physical_dispatch.jobs[r.id]
    original_mission=p.vehicles['V01'].mission_id
    app=create_app(); app.state.demo=s
    with TestClient(app) as client:
        response=client.post('/v1/demo/unity/vehicles/V01/mission',json=dict(
            run_id=s.run_id,command_id='manual-conflict',stop_id='main_gate_road_candidate'))
        assert response.status_code==409
        assert response.json()['detail']=='Vehicle is reserved for an observed transport request'
        assert p.vehicles['V01'].mission_id==original_mission
        for phase in ('TO_PICKUP','PICKUP_SERVICE','TO_DROPOFF','DROPOFF_SERVICE','RETURN_PENDING','RETURNING'):
            job.phase=phase
            ego=p.vehicles['V01'].ego
            command=p.observation('V01',token,ego.model_copy(update={'tick':ego.tick+1}))
            assert command['transport_phase']==phase
        job.returned=True
        ego=p.vehicles['V01'].ego
        assert p.observation('V01',token,ego.model_copy(update={'tick':ego.tick+1}))['transport_phase']==''


def test_capacity_and_battery_failure_leave_external_request_queued():
    s,p,token,wall=setup(); r=request(s,party_size=99)
    assert r.status==RequestStatus.QUEUED and not s.physical_dispatch.jobs
    s,p,token,wall=setup()
    s.service.batteries['V01'].remaining_wh=s.service.battery_policy.reserve_wh
    r=request(s)
    assert r.status==RequestStatus.QUEUED
    assert s.service.waiting_reasons[r.id]=='WAITING_PHYSICAL_BATTERY'


def test_cancel_before_pickup_returns_vehicle_without_transporting():
    s,p,token,wall=setup(); r=request(s)
    s.service.cancel_request(r.id,'cancel','test')
    observe(s,p,token,wall,arrive=True)
    assert r.status==RequestStatus.CANCELLED
    job=s.physical_dispatch.jobs[r.id]
    assert job.phase=='RETURNING' and not job.returned


def test_reconnect_resumes_same_request_without_duplicate_assignment():
    s,p,token,wall=setup(); r=request(s)
    old_mission=p.vehicles['V01'].mission_id
    p.disconnect('V01',token); wall[0]+=2; s.advance()
    assert r.status==RequestStatus.ASSIGNED
    pose=p.vehicles['V01'].ego
    token=p.connect('V01')
    p.observation('V01',token,pose.model_copy(update=dict(tick=1)))
    s.advance()
    job=s.physical_dispatch.jobs[r.id]
    assert job.mission_id!=old_mission and job.mission_id==p.vehicles['V01'].mission_id
    assert len(s.physical_dispatch.jobs)==1 and r.status==RequestStatus.ASSIGNED

def test_preflight_cache_survives_temporary_stale_observations(monkeypatch):
    s,p,token,wall=setup()
    for identifier in ('V02','V03'):
        other=p.connect(identifier)
        p.observation(identifier,other,p.vehicles['V01'].ego.model_copy())
    calls=[]
    def estimate(identifier,r):
        calls.append(identifier)
        wall[0]+=.6  # Route preview holds the session lock beyond freshness TTL.
        return 'main_gate_road_candidate',10.,[10.,20.,20.]
    monkeypatch.setattr(s.physical_dispatch,'_estimate',estimate)
    r=request(s)
    for _ in range(4):
        wall[0]+=.11
        s.physical_dispatch.step()  # HTTP polling arrives before the next WS observation.
        assert r.status==RequestStatus.QUEUED
        assert p.vehicles['V01'].mission_id is None
        for identifier,vehicle in p.vehicles.items():
            p.observation(identifier,vehicle.token,
                          vehicle.ego.model_copy(update={'tick':vehicle.ego.tick+1}))
        wall[0]+=.11
        s.physical_dispatch.retry_at=0
        s.physical_dispatch.step()
        if r.status==RequestStatus.ASSIGNED:
            break
    assert r.status==RequestStatus.ASSIGNED
    assert calls==['V01','V02','V03']

@pytest.mark.parametrize('delta,expected_calls', [(1e-5, ['V01','V02']), (.01, ['V01','V01'])])
def test_geometry_roundoff_reuses_estimate_but_real_changes_replan(monkeypatch,delta,expected_calls):
    s,p,token,wall=setup()
    second=p.connect('V02')
    p.observation('V02',second,p.vehicles['V01'].ego.model_copy())
    calls=[]
    def estimate(identifier,r):
        calls.append(identifier)
        return 'main_gate_road_candidate',10.,[10.,20.,20.]
    monkeypatch.setattr(s.physical_dispatch,'_estimate',estimate)
    request(s)
    wall[0]+=.11
    for identifier,vehicle in p.vehicles.items():
        p.observation(identifier,vehicle.token,vehicle.ego.model_copy(update={
            'tick':vehicle.ego.tick+1,'wheelbase_m':vehicle.ego.wheelbase_m+delta}))
    s.physical_dispatch.step()
    assert calls==expected_calls


@pytest.mark.parametrize('policy', ['fifo', 'greedy', 'hungarian'])
@pytest.mark.parametrize('already_driving', [False, True])
def test_failed_candidate_cannot_starve_other_requests_and_vehicles(monkeypatch, policy, already_driving):
    s,p,_token,wall=setup()
    s.service.dispatch_algorithm=policy
    if already_driving:
        initial=request(s,command_id='already-driving')
        assert initial.vehicle_id=='V01'
    for identifier in ('V02','V03'):
        other=p.connect(identifier)
        p.observation(identifier,other,p.vehicles['V01'].ego.model_copy())
    calls=[]
    def estimate(identifier,r):
        calls.append((identifier,r.id))
        wall[0]+=.6  # Several route previews exceed the failed-candidate retry interval.
        if identifier=='V02':
            raise ValueError('No directed return route')
        return 'main_gate_road_candidate',10.,[10.,20.,20.]
    monkeypatch.setattr(s.physical_dispatch,'_estimate',estimate)
    requests=[request(s,command_id=f'multi-{i}') for i in range(3)]
    for _ in range(20):
        wall[0]+=.11
        for identifier,vehicle in p.vehicles.items():
            p.observation(identifier,vehicle.token,
                vehicle.ego.model_copy(update={'tick':vehicle.ego.tick+1}))
        s.physical_dispatch.step()
        if len(s.physical_dispatch.jobs)==2:
            break
    assert {job.vehicle_id for job in s.physical_dispatch.jobs.values()}=={'V01','V03'}
    assert sum(r.status==RequestStatus.ASSIGNED for r in requests)==(1 if already_driving else 2)
    # Both feasible vehicles must start without either completing its first trip.
    assert all(not job.returned for job in s.physical_dispatch.jobs.values())
    failures=s.physical_dispatch.diagnostics['candidate_failures']
    assert failures and all(row['V02']=='No directed return route' for row in failures.values())
