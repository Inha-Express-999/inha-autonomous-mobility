import math

from campus_sim.unity_drive import AutoMission, DriveCommand
from campus_sim.web_demo import DemoSession
from test_lanes import lane_ego


def test_anniversary_to_plaza_compares_turn_with_long_forward_loop():
    s=DemoSession(); p=s.physical; token=p.connect('V01')
    pose=lane_ego(s,schema_version=4,rear_axle_offset_m=-.65,half_length_m=1.55,
        half_width_m=.46001,obstacle_detection_enabled=False,rays=[],
        x=90.70784823566991,z=99.11709365655904,heading_rad=-1.2741242662751333)
    p.observation('V01',token,pose)
    command=AutoMission(run_id=s.run_id,command_id='compare',stop_id='biryong_plaza_road_candidate',
                        driving_profile='campus_fast_no_obstacles')
    p.mission('V01',command,_allow_turn=False)
    vehicle=p.vehicles['V01']
    length=lambda:sum(math.dist(a,b) for a,b in zip(vehicle.route_unity,vehicle.route_unity[1:]))
    forward_length = length()
    assert vehicle.turn is None
    vehicle.last_command_id=None
    p.mission('V01',command)
    # Compare the alternatives on the current edited map, not old Stop distances.
    assert length()<forward_length/2 and vehicle.turn is not None
    assert vehicle.ego is pose and vehicle.token==token
    assert vehicle.status=='MANEUVER'


def test_hitech_departure_plans_turn_without_mutating_localization():
    s=DemoSession(); p=s.physical; token=p.connect('V01')
    pose=lane_ego(s,schema_version=4,rear_axle_offset_m=-.65,half_length_m=1.55,
                  half_width_m=.46,obstacle_detection_enabled=False,rays=[])
    p.observation('V01',token,pose)
    p.mission('V01',AutoMission(run_id=s.run_id,command_id='arrive',
        stop_id='hitech_road_candidate',driving_profile='campus_fast_no_obstacles'))
    vehicle=p.vehicles['V01']; a,b=vehicle.route_unity[-2:]
    arrival=pose.model_copy(update=dict(tick=2,x=b[0],z=b[1],heading_rad=math.atan2(b[0]-a[0],b[1]-a[1])))
    p.observation('V01',token,arrival)
    p.mission('V01',AutoMission(run_id=s.run_id,command_id='depart',
        stop_id='library_road_candidate',driving_profile='campus_fast_no_obstacles'))
    assert vehicle.turn is not None
    assert vehicle.ego == arrival
    assert vehicle.destination=='library'
    display=next(item for item in s.state()['vehicles'] if item['id']=='V01')
    assert display['phase']=='MANEUVER'
    response=p.observation('V01',token,arrival.model_copy(update=dict(tick=3)))
    assert response['status']=='MANEUVER' and not response['emergency_stop']
    assert 0<response['target_speed_kmh']<=2
    DriveCommand(**response)
    invalid=arrival.model_copy(update=dict(tick=4,sensor_valid=False))
    response=p.observation('V01',token,invalid)
    assert response['emergency_stop']
    # Integrate only the commanded bicycle motion. No measured pose is replaced
    # by the planner's predicted endpoint and no time-only arrival is accepted.
    heading=arrival.heading_rad
    rear_x=arrival.x+arrival.rear_axle_offset_m*math.sin(heading)
    rear_z=arrival.z+arrival.rear_axle_offset_m*math.cos(heading)
    speed=0.
    for tick in range(5,6005):
        measured=arrival.model_copy(update=dict(tick=tick,
            x=rear_x-arrival.rear_axle_offset_m*math.sin(heading),
            z=rear_z-arrival.rear_axle_offset_m*math.cos(heading),
            heading_rad=heading,speed_mps=speed))
        response=p.observation('V01',token,measured)
        DriveCommand(**response)
        assert response['status'] != 'OFF_ROUTE',response
        if vehicle.turn is None:
            assert abs(speed)<.03 and response['emergency_stop']
            assert response['status']=='DRIVING'
            break
        target=response['target_speed_kmh']/3.6
        step=(1.5 if response['emergency_stop'] else .8)*.02
        speed+=max(-step,min(step,target-speed))
        yaw=speed/arrival.wheelbase_m*math.tan(response['steering_angle_rad'])*.02
        rear_x+=speed*math.sin(heading+yaw/2)*.02
        rear_z+=speed*math.cos(heading+yaw/2)*.02
        heading+=yaw
    assert vehicle.turn is None
    response=p.observation('V01',token,measured.model_copy(update=dict(tick=tick+1)))
    assert response['status']=='DRIVING' and not response['emergency_stop']
