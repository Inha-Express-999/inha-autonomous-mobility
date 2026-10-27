import math

from campus_sim.turnaround import TurnController


def controller():
    return TurnController(wheelbase_m=1.3,max_steering_rad=math.pi/6,
        lane_offset_m=1.5,road_half_width_m=3,forward_space_m=5,rear_space_m=3,
        minimum_radius_m=2.6,half_width_m=.46,half_length_m=1.55)


import pytest


@pytest.mark.parametrize('initial_heading', [0., -.04, .04])
@pytest.mark.parametrize('recorded_geometry', [False, True])
def test_closed_loop_three_point_turn_stops_before_each_direction_change(initial_heading, recorded_geometry):
    turn=(TurnController(wheelbase_m=1.3,max_steering_rad=math.pi/6,
        lane_offset_m=1.81567418,road_half_width_m=3.45,forward_space_m=5.90615,rear_space_m=37.6751,
        minimum_radius_m=2.58941582,half_width_m=.46,half_length_m=1.55) if recorded_geometry else controller())
    offset=turn.geometry['lane_offset_m']
    x,z,heading,speed=offset,0.,initial_heading,0.
    dt=.02
    last_direction=1
    actual_steering=0.
    transitions=[]
    for tick in range(6000):
        command=turn.update(x,z,heading,speed)
        assert command['status'] != 'OFF_ROUTE', (tick,command,x,z,heading)
        target=command['target_speed_kmh']/3.6
        if target:
            direction=1 if target>0 else -1
            if direction != last_direction:
                transitions.append((direction,abs(speed)))
                assert abs(speed)<.03
            last_direction=direction
        acceleration=1.5 if command['emergency_stop'] else .8
        speed+=max(-acceleration*dt,min(acceleration*dt,target-speed))
        actual_steering+=max(-math.radians(120)*dt,min(math.radians(120)*dt,
                            command['steering_angle_rad']-actual_steering))
        yaw_rate=speed/1.3*math.tan(actual_steering)
        x+=speed*math.sin(heading+yaw_rate*dt/2)*dt
        z+=speed*math.cos(heading+yaw_rate*dt/2)*dt
        heading+=yaw_rate*dt
        if command['status']=='DONE': break
    assert turn.status=='DONE'
    assert [d for d,_ in transitions]==[-1,1]
    assert math.hypot(x+offset,z)<.1
    assert abs(math.atan2(math.sin(heading+math.pi),math.cos(heading+math.pi)))<.15


def test_cusp_requires_three_consecutive_stopped_observations():
    turn=controller()
    end=turn.arcs[0][-1]
    turn.sample_index=len(turn.arcs[0])-1
    for speed in (.2,0.,.04,0.,0.):
        assert turn.update(end.x,end.z,end.heading_rad,speed)['emergency_stop']
        assert turn.arc_index==0
    assert turn.update(end.x,end.z,end.heading_rad,0.)['emergency_stop']
    assert turn.arc_index==1


def test_turn_error_latches_stop_instead_of_recovering_by_teleport():
    turn=controller()
    result=turn.update(10,0,0,0)
    assert result['status']=='OFF_ROUTE' and result['emergency_stop']
    assert turn.update(1.5,0,0,0)['status']=='OFF_ROUTE'


def test_measured_cusp_replans_curve_with_steering_and_footprint_bounds():
    turn=TurnController(wheelbase_m=1.3,max_steering_rad=math.pi/6,
        lane_offset_m=1.81447,road_half_width_m=3.45,forward_space_m=5.88479,rear_space_m=37.69648,
        minimum_radius_m=3.3775,half_width_m=.46,half_length_m=1.55)
    original=turn.arcs[2]
    path=turn.measured_cusp_arc(.44735,3.50477,4.52790,original)
    assert path is not None
    assert path[0].x==pytest.approx(.44735) and path[0].z==pytest.approx(3.50477)
    assert path[-1].x==pytest.approx(original[-1].x) and path[-1].z==pytest.approx(original[-1].z)
    for point in path:
        assert abs(point.curvature)<=math.tan(math.pi/6)/1.3*.95
        for right in (-.46,.46):
            for front in (-1.55,1.55):
                x=point.x+right*math.cos(point.heading_rad)+front*math.sin(point.heading_rad)
                z=point.z-right*math.sin(point.heading_rad)+front*math.cos(point.heading_rad)
                assert abs(x)+.2<3.45 and -.0-37.69648+.2<z<5.88479-.2
    assert turn.arcs[2] is original
    assert turn.measured_cusp_arc(20,20,0,original) is None
