import math
import campus_sim.unity_drive as drive
from campus_sim.web_demo import DemoSession
from campus_sim.unity_drive import AutoMission, EgoObservation

s=DemoSession(); p=s.physical; t=p.connect('V01'); v=p.vehicles['V01']
v.ego=EgoObservation(schema_version=3,run_id=s.run_id,geometry_hash=p.geometry_hash,
 tick=1,x=70.0667,z=-159.6858,heading_rad=math.radians(-60),speed_mps=0,
 wheelbase_m=1.299999,max_steering_rad=.5235988,half_width_m=.4,
 sensor_valid=True,sensor_age_s=0,obstacle_detection_enabled=False,rays=[])
def mission(stop):
 v.received_s=p.clock()
 return p.mission('V01',AutoMission(run_id=s.run_id,command_id=stop,
 stop_id=stop,driving_profile='campus_fast_no_obstacles'))
mission('hitech_road_candidate')
a,b=v.route_unity[-2:]
v.ego=v.ego.model_copy(update=dict(x=b[0],z=b[1],heading_rad=math.atan2(b[0]-a[0],b[1]-a[1])))
print('ARRIVAL',b,v.ego.heading_rad)
original=drive.lane_path
def traced(parts,radius):
 try: return original(parts,radius)
 except ValueError as e:
  print('CONNECTOR',str(e),'first parts',parts[:2]); raise
drive.lane_path=traced
original_route=drive.directed_lane_route
def traced_route(graph,costs,incoming,goal,profiles,coordinates,radius):
 result=original_route(graph,costs,incoming,goal,profiles,coordinates,radius)
 print('ROUTE',incoming.id,'goal',goal,'found',result is not None)
 for e in graph._outgoing[incoming.v]:
  parts=[([coordinates.to_unity(*q) for q in edge.polyline],profiles.by_edge[edge.id]['right_offset_m']) for edge in (incoming,e)]
  try: original(parts,radius); reason='OK'
  except ValueError as error: reason=str(error)
  print('EXIT',e.id,reason)
 return result
drive.directed_lane_route=traced_route
try: mission('library_road_candidate')
except ValueError as e: print('FAIL',e)
