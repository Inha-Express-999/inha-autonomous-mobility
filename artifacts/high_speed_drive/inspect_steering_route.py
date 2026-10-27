import math
from campus_sim.web_demo import DemoSession
from campus_sim.unity_drive import AutoMission, EgoObservation, projection

s = DemoSession()
p = s.physical
t = p.connect('V01')
ego = EgoObservation(schema_version=3, run_id=s.run_id, geometry_hash=p.geometry_hash,
    tick=1, x=70.0667, z=-159.6858, heading_rad=math.radians(-60), speed_mps=0,
    wheelbase_m=1.299999, max_steering_rad=.5235988, half_width_m=.4,
    sensor_valid=True, sensor_age_s=0, obstacle_detection_enabled=False, rays=[])
p.observation('V01', t, ego)
p.mission('V01', AutoMission(run_id=s.run_id, command_id='diagnostic',
    stop_id='hitech_road_candidate', driving_profile='campus_fast_no_obstacles', cruise_speed_kmh=50))
v = p.vehicles['V01']
walked = 0
for a,b in zip(v.route_unity,v.route_unity[1:]):
    d,ratio,point = projection((12.574,-59.745),a,b)
    if d < 12:
        print(round(walked,3),a,b,'distance',round(d,3),'along',round(walked+math.dist(a,b)*ratio,3))
    walked += math.dist(a,b)
