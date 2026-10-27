import pytest
from test_physical_dispatch import setup, request


def park_home(s,p):
    v=p.vehicles['V01'];x,z=p.coordinates.to_unity(*s.service.graph.nodes[s.service.home_nodes['V01']])
    v.ego=v.ego.model_copy(update=dict(x=x,z=z,speed_mps=0.))
    v.status='PARKED';s.clock.paused=False
    return v


def tick(s,p,wall,**changes):
    wall[0]+=.1;v=p.vehicles['V01']
    v.ego=v.ego.model_copy(update=dict(tick=v.ego.tick+1,**changes));v.received_s=wall[0]
    s.physical_dispatch._energy_step(wall[0])


def test_home_charging_uses_web_rate_and_fresh_observations_only():
    s,p,token,wall=setup();v=park_home(s,p);b=s.service.batteries['V01'];b.remaining_wh=1000
    d=s.physical_dispatch;d._energy_step(0.)
    tick(s,p,wall)
    assert b.remaining_wh==pytest.approx(1000+s.service.battery_policy.charging_power_w*.1/3600)
    before=b.remaining_wh;d._energy_step(wall[0])
    assert b.remaining_wh==before  # Repeated HTTP/state reads do not charge twice.
    s.clock.paused=True;tick(s,p,wall);assert b.remaining_wh==before
    s.clock.paused=False;wall[0]+=5;d._energy_step(wall[0]);assert b.remaining_wh==before
    assert 'V01' not in d.charging
    v.received_s=wall[0];tick(s,p,wall)
    assert b.remaining_wh==before  # Reconnect/freshness gap is not charged retroactively.
    tick(s,p,wall);assert b.remaining_wh>before


def test_charging_requires_own_home_and_caps_at_capacity():
    s,p,token,wall=setup();v=park_home(s,p);b=s.service.batteries['V01'];b.remaining_wh=1999.99
    s.physical_dispatch._energy_step(0.);tick(s,p,wall)
    assert b.remaining_wh==2000 and 'V01' not in s.physical_dispatch.charging
    b.remaining_wh=1000
    v.ego=v.ego.model_copy(update={'x':v.ego.x+20})
    tick(s,p,wall)
    assert b.remaining_wh==1000 and 'V01' not in s.physical_dispatch.charging


def test_direct_physics_mission_also_consumes_web_distance_and_auxiliary_energy():
    s,p,token,wall=setup();v=park_home(s,p);v.status='DRIVING'
    b=s.service.batteries['V01'];policy=s.service.battery_policy
    s.physical_dispatch._energy_step(0.)
    tick(s,p,wall,x=v.ego.x+10,speed_mps=2.)
    assert b.consumed_wh==pytest.approx(10*policy.wh_per_m+.1*policy.auxiliary_power_w/3600)
    assert b.remaining_wh==pytest.approx(policy.capacity_wh-b.consumed_wh)


def test_selected_energy_feasible_vehicle_interrupts_physical_charging():
    s,p,token,wall=setup();v=park_home(s,p)
    # Use the recorded departure pose so real preflight and mission generation run.
    v.ego=v.ego.model_copy(update=dict(x=-38.398,z=-320.777,heading_rad=.656696))
    s.service.batteries['V01'].remaining_wh=1500
    s.physical_dispatch._energy_step(0.)
    assert 'V01' in s.physical_dispatch.charging
    r=request(s,pickup_landmark_id='anniversary_60',dropoff_landmark_id='biryong_plaza')
    assert r.vehicle_id=='V01'
    assert 'V01' not in s.physical_dispatch.charging
    assert s.physical_dispatch.charges[-1].interrupted


def test_reserve_stops_driving_but_low_home_battery_can_charge():
    s,p,token,wall=setup();v=park_home(s,p);b=s.service.batteries['V01']
    b.remaining_wh=s.service.battery_policy.reserve_wh-.1
    s.physical_dispatch._energy_step(0.);tick(s,p,wall)
    assert v.status=='PARKED' and 'V01' in s.physical_dispatch.charging
    v.status='DRIVING';tick(s,p,wall,speed_mps=1.)
    assert v.status=='OFF_ROUTE' and 'V01' not in s.physical_dispatch.charging
