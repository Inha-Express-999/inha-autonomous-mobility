import json, math
from campus_sim.web_demo import DemoSession
from campus_sim.unity_drive import AutoMission, EgoObservation
from campus_sim.physical_turn import prepare_turn
s=DemoSession(); p=s.physical; token=p.connect('V01')
ego=EgoObservation(schema_version=4,run_id=s.run_id,geometry_hash=p.geometry_hash,tick=1,
    x=70.0667,z=-159.6858,heading_rad=math.radians(-60),speed_mps=0,wheelbase_m=1.3,
    max_steering_rad=.5235988,half_width_m=.46001,rear_axle_offset_m=-.65,half_length_m=1.55,
    sensor_valid=True,sensor_age_s=0,obstacle_detection_enabled=False,rays=[])
p.observation('V01',token,ego)
def cmd(stop): return AutoMission(run_id=s.run_id,command_id=stop,stop_id=stop+'_road_candidate',driving_profile='campus_fast_no_obstacles')
p.mission('V01',cmd('anniversary_60'))
v=p.vehicles['V01']; a,b=v.route_unity[-2:]
v.ego=ego.model_copy(update=dict(x=b[0],z=b[1],heading_rad=math.atan2(b[0]-a[0],b[1]-a[1])))
v.received_s=p.clock()
p.mission('V01',cmd('biryong_plaza'))
length=lambda:sum(math.dist(a,b) for a,b in zip(v.route_unity,v.route_unity[1:]))
result=dict(forward_m=length(),origin=v.ego.model_dump())
v.last_command_id=None
result['reverse_available']=prepare_turn(p,'V01',cmd('biryong_plaza'))
if v.turn:
    result['reverse_onward_m']=length()
    result['turn_m']=sum(sum(math.hypot(b.x-a.x,b.z-a.z) for a,b in zip(arc,arc[1:])) for arc in v.turn.controller.arcs)
print(json.dumps(result,indent=2))
