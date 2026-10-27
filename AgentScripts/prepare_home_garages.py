"""Derive simulation garage bays from existing home road nodes and lane profiles.

No new graph edges or real-world access claims. Editor authoring projects these
horizontal poses onto existing asphalt before saving any vehicle placement.
"""
import json
import math
from pathlib import Path
from campus_sim.web_demo import DemoSession
from campus_sim.unity_drive import EgoObservation, AutoMission

root=Path(__file__).resolve().parents[1]
session=DemoSession()
p=session.physical
p.clock=lambda:0.
# Same measured Annyoung prefab geometry for all three instantiated vehicles;
# Unity still measures and transmits its actual geometry at every connection.
template=dict(schema_version=4,run_id=session.run_id,geometry_hash=p.geometry_hash,
    speed_mps=0,tick=1,wheelbase_m=1.299996,max_steering_rad=math.pi/6,
    half_width_m=.46001,half_length_m=1.550003,rear_axle_offset_m=-.649999,
    sensor_valid=True,sensor_age_s=0,obstacle_detection_enabled=False,rays=[])
result=[]
for vehicle_id,node in session.service.home_nodes.items():
    center=p.coordinates.to_unity(*session.service.graph.nodes[node])
    candidates=[]
    for edge in session.service.graph.edges:
        if edge.u!=node or edge.id not in p.lanes.by_edge: continue
        points=[p.coordinates.to_unity(*point) for point in edge.polyline]
        a,b=points[:2]; length=math.dist(a,b)
        if length<3: continue
        direction=((b[0]-a[0])/length,(b[1]-a[1])/length)
        lane=p.lanes.by_edge[edge.id]
        offset=lane['right_offset_m']
        x=a[0]+2*direction[0]+offset*direction[1]
        z=a[1]+2*direction[1]-offset*direction[0]
        heading=math.atan2(*direction)
        ego=EgoObservation(**dict(template,x=x,z=z,heading_rad=heading))
        if vehicle_id in p.vehicles: p.disconnect(vehicle_id,p.vehicles[vehicle_id].token)
        token=p.connect(vehicle_id)
        p.observation(vehicle_id,token,ego)
        reachable=0; distance=0
        for stop in session.service.stops:
            try:
                plan=p.preview_mission(vehicle_id,AutoMission(run_id=session.run_id,command_id=stop,
                    stop_id=stop,driving_profile='campus_fast_no_obstacles',cruise_speed_kmh=40))
                reachable+=1
                distance+=sum(math.dist(a,b) for a,b in zip(plan.route_unity,plan.route_unity[1:]))
            except ValueError: pass
        candidates.append((-reachable,distance,edge.id,dict(vehicle_id=vehicle_id,
            hub_id=session.service.home_hubs[vehicle_id]['id'],name=session.service.home_hubs[vehicle_id]['name'],
            node_id=node,x=x,z=z,yaw=math.degrees(heading),center_x=center[0],center_z=center[1],
            road_width=lane['authored_road_width_m'],reachable_stops=reachable)))
    if not candidates: raise ValueError('No authored departure road for '+vehicle_id)
    result.append(min(candidates,key=lambda v:v[:3])[3])
output=root/'Assets/CampusSim/Data/home_garages.json'
output.write_text(json.dumps(dict(geometry_hash=p.geometry_hash,simulation_only=True,homes=result),
                             ensure_ascii=False,indent=2),encoding='utf-8')
print(json.dumps(result,ensure_ascii=True))
