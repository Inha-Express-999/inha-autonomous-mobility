"""Joint terrain proposal: preserve level sports pad, constrain nearby gradients.
Synthetic design study. Never applies heights to Unity on its own.
"""
import heapq,json,math,runpy
import csv
import numpy as np
from pathlib import Path

ROOT=Path(__file__).resolve().parents[1]
s=runpy.run_path(str(ROOT/'AgentScripts/smooth_sports_roads.py'))
before=s['before'].astype(float);n=s['n'];sy=s['sy'];dx=s['dx'];dz=s['dz']
x,z=s['x'],s['z'];out=s['OUT']
height=before*sy+s['oy'];target=-6.923518
pad=s['mask'](out/'sports-triangles.raw') & (abs(height-target)<.003)
region=(x>-490)&(x<100)&(z>-400)&(z<270)
steps=[(j,i,.06*math.hypot(i*dx,j*dz)) for j in (-1,0,1) for i in (-1,0,1) if i or j]
groups=[];group_names=[];membership={}
for line in (out/'Footprints/manifest.txt').read_text(encoding='utf-8-sig').splitlines():
    file,name=line.split('|',1)
    footprint=s['mask'](out/'Footprints'/file)
    # Reserve the four-cell interpolation stencil across every footprint edge.
    p=np.pad(footprint,1)
    footprint=np.logical_or.reduce([p[j:j+n,i:i+n] for j in range(3) for i in range(3)])
    cells=np.argwhere(footprint)
    if not len(cells):continue
    groups.append(cells);group_names.append(name)
    for j,i in cells:membership.setdefault((int(j),int(i)),[]).append(len(groups)-1)
building_core=np.zeros_like(region)
for cells in groups:building_core[cells[:,0],cells[:,1]]=True
# The transition domain must contain entire buildings plus a full shoulder.
padding=int(math.ceil(88/min(dx,dz)))
bp=np.pad(building_core,padding)
for j in range(-padding,padding+1):
    for i in range(-padding,padding+1):
        if math.hypot(i*dx,j*dz)<=88:
            region|=bp[padding+j:padding+j+n,padding+i:padding+i+n]
def envelope(initial,seeds):
    values=initial.copy();queue=[(float(values[j,i]),int(j),int(i)) for j,i in np.argwhere(seeds)]
    group_best=[math.inf]*len(groups)
    heapq.heapify(queue)
    while queue:
        value,j,i=heapq.heappop(queue)
        if value!=values[j,i]:continue
        for group in membership.get((j,i),[]):
            if value>=group_best[group]:continue
            group_best[group]=value
            for jj,ii in groups[group]:
                if value<values[jj,ii]:
                    values[jj,ii]=value;heapq.heappush(queue,(value,int(jj),int(ii)))
        for dj,di,cost in steps:
            jj,ii=j+dj,i+di
            if not (0<=jj<n and 0<=ii<n and region[jj,ii]):continue
            candidate=value+cost
            if candidate<values[jj,ii]:
                values[jj,ii]=candidate;heapq.heappush(queue,(candidate,jj,ii))
    return values
distance=np.full_like(height,np.inf);distance[pad]=0
distance=envelope(distance,pad)
# Preserve the sports plane without forcing an impossible downward step into
# existing nearby depressions. This may raise low cells and lower high cells.
initial=np.maximum(height,target-distance);initial[pad]=target
limited=envelope(initial,region)
edge=np.minimum.reduce([x+490,100-x,z+400,270-z])
weight=np.clip(edge/80,0,1);weight=weight*weight*(3-2*weight)
building_distance=np.full_like(height,np.inf);building_distance[building_core]=0
building_distance=envelope(building_distance,building_core)/.06
building_weight=np.clip(1-building_distance/80,0,1)
building_weight=building_weight*building_weight*(3-2*building_weight)
weight=np.maximum(weight,building_weight)
weight[~region]=0
water=s['mask'](out/'water-triangles.raw')
polygons={}
for row in csv.DictReader((out/'pedestrian-boundaries.csv').open(encoding='utf-8-sig')):
    polygons.setdefault(row['id'],[]).append((float(row['x']),float(row['z'])))
walk=s['rasterize']((({'type':'Polygon','coordinates':[[*points,points[0]]]},1) for points in polygons.values() if len(points)>=3),out_shape=(n,n),transform=s['transform'],all_touched=True).astype(bool)
water_core=water|walk
wp=np.pad(water_core,1)
water_core=np.logical_or.reduce([wp[j:j+n,i:i+n] for j in range(3) for i in range(3)])
preserve_weight=np.zeros_like(height)
for j in range(-5,6):
    for i in range(-5,6):
        distance=math.hypot(i*dx,j*dz)
        if distance>32:continue
        value=max(0,1-distance/32);value=value*value*(3-2*value)
        shifted=np.roll(np.roll(water_core,j,axis=0),i,axis=1)
        preserve_weight[shifted]=np.maximum(preserve_weight[shifted],value)
weight*=1-preserve_weight
after=(height+(limited-height)*weight-s['oy'])/sy
after=after.astype('<f4')
report=dict(status='proposal_not_applied',synthetic_grid_gradient_limit=.06,
 before=s['metrics'](before),after=s['metrics'](after),
 changed_samples=int(np.sum(abs(after-before)*sy>.001)),
 maximum_change_m=float(np.max(abs(after-before))*sy),
 sports_pad_maximum_error_m=float(np.max(abs(after.astype(float)*sy+s['oy']-target)[pad])),
 changed_structural_samples=int(np.sum((abs(after-before)*sy>.001)&s['protected'])),
 caveat='Buildings and approaches require refitting; 80m outer feather is not slope constrained. Eight-neighbor limits are not continuous road-grade guarantees.')
report['building_pad_checks']=[dict(name=name,solver_height_range_m=float(np.ptp(limited[cells[:,0],cells[:,1]])),final_height_range_m=float(np.ptp(after[cells[:,0],cells[:,1]]))*sy) for name,cells in zip(group_names,groups)]
report['application_gate_passed']=all(r['final_height_range_m']<=.01 for r in report['building_pad_checks']) and report['sports_pad_maximum_error_m']<=.01 and report['after']['over_10percent']==0
report['failed_building_pads']=[r['name'] for r in report['building_pad_checks'] if r['final_height_range_m']>.01]
report['water_and_walk_fixed_error_m']=float(np.max(abs(after-before)[water_core])*sy)
report['application_gate_passed'] &= report['water_and_walk_fixed_error_m']<=.0001
after.tofile(out/'building-joint-proposal.raw')
(out/'building-joint-report.json').write_text(json.dumps(report,indent=2),encoding='utf-8')
print(json.dumps(report,indent=2))
