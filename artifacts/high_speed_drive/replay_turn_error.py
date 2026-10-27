import csv,json,math,sys
from campus_sim.web_demo import DemoSession
from campus_sim.unity_drive import EgoObservation,AutoMission
rows=list(csv.DictReader(open(sys.argv[1] if len(sys.argv)>1 else 'artifacts/high_speed_drive/smooth-turn-anniversary-biryong.csv',encoding='utf-8-sig')))
start=next(i for i,r in enumerate(rows) if r['status']=='저속 회차 중')
s=DemoSession(); p=s.physical; token=p.connect('V01'); r=rows[start]
ego=EgoObservation(schema_version=4,run_id=s.run_id,geometry_hash=p.geometry_hash,tick=1,
    x=float(r['x']),z=float(r['z']),heading_rad=math.radians(float(r['heading_deg'])),speed_mps=0,
    wheelbase_m=1.3,max_steering_rad=.5235988,half_width_m=.46,rear_axle_offset_m=-.65,half_length_m=1.55,
    sensor_valid=True,sensor_age_s=0,obstacle_detection_enabled=False,rays=[])
p.observation('V01',token,ego)
p.mission('V01',AutoMission(run_id=s.run_id,command_id='replay',stop_id='biryong_plaza_road_candidate',driving_profile='campus_fast_no_obstacles'))
turn=p.vehicles['V01'].turn
for row in rows[start:]:
    pose=ego.model_copy(update=dict(x=float(row['x']),z=float(row['z']),heading_rad=math.radians(float(row['heading_deg'])),speed_mps=float(row['signed_speed_kmh'])/3.6))
    result=turn.update(pose)
    if result['status']=='OFF_ROUTE':
        c=turn.controller; end=c.arcs[c.arc_index][-1]
        rear=(pose.x+pose.rear_axle_offset_m*math.sin(pose.heading_rad),pose.z+pose.rear_axle_offset_m*math.cos(pose.heading_rad))
        dx,dz=rear[0]-turn.center[0],rear[1]-turn.center[1]
        x=dx*math.cos(turn.heading)-dz*math.sin(turn.heading); z=dx*math.sin(turn.heading)+dz*math.cos(turn.heading)
        actual=pose.heading_rad-turn.heading
        print(json.dumps(dict(time=row['elapsed_s'],result=result,arc=c.arc_index,index=c.sample_index,
            local=[x,z,actual],goal=[end.x,end.z,end.heading_rad],distance=math.hypot(x-end.x,z-end.z),
            heading_error_deg=math.degrees(math.atan2(math.sin(end.heading_rad-actual),math.cos(end.heading_rad-actual))),
            initial_heading_error_deg=math.degrees(ego.heading_rad-turn.heading),geometry=c.geometry),indent=2))
        break
