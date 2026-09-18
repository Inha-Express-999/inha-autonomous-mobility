"""Evaluate constrained terrain relaxation around sports-area roads.
Keeps existing structural footprints, anchors and sports platforms fixed.
All reported grid metrics use the same road cells before and after.
"""
import json
from pathlib import Path
import numpy as np
from rasterio.features import rasterize
from affine import Affine

ROOT=Path(__file__).resolve().parents[1]
OLD=ROOT/'Docs/MapResearch/Iterations/RoadGrading'
OUT=ROOT/'Docs/MapResearch/Iterations/SportsRoadGrading'
grid=json.loads((OLD/'grid.json').read_text())
n=grid['resolution'];ox,oy,oz=grid['origin'];sx,sy,sz=grid['size']
dx,dz=sx/(n-1),sz/(n-1)
before=np.fromfile(OUT/'before.raw','<f4').reshape(n,n)
transform=Affine(dx,0,ox-dx/2,0,dz,oz-dz/2)
def mask(path):
    triangles=np.fromfile(path,'<f4').reshape(-1,3,2)
    return rasterize((({'type':'Polygon','coordinates':[[*t.tolist(),t[0].tolist()]]},1) for t in triangles),out_shape=(n,n),transform=transform,all_touched=True).astype(bool)
protected=mask(OLD/'protected-triangles.raw')|mask(OUT/'sports-triangles.raw')
# Include every bilinear support sample around a protected footprint.
p=np.pad(protected,1)
protected=np.logical_or.reduce([p[j:j+n,i:i+n] for j in range(3) for i in range(3)])
for ax,az in np.fromfile(OLD/'anchors.raw','<f4').reshape(-1,2):
    ix,iz=int(np.floor((ax-ox)/dx)),int(np.floor((az-oz)/dz))
    protected[max(0,iz):min(n,iz+2),max(0,ix):min(n,ix+2)]=True
x,z=np.meshgrid(ox+np.arange(n)*dx,oz+np.arange(n)*dz)
region=(x>-410)&(x<20)&(z>-320)&(z<190)
free=region&~protected
after=before.astype(float).copy()
# Harmonic relaxation has no per-cell height overshoot. Fixed plateaus and
# exterior boundary are Dirichlet constraints, not blended back after solving.
for _ in range(1200):
    mean=(np.roll(after,1,0)+np.roll(after,-1,0)+np.roll(after,1,1)+np.roll(after,-1,1))/4
    delta=np.max(np.abs(mean[free]-after[free]))
    after[free]=mean[free]
    if delta*sy<.00001:break
after=after.astype('<f4')
assert np.array_equal(after[~free],before[~free])
road=mask(OLD/'roads-xz.raw')&(x>=-350)&(x<=-60)&(z>=-260)&(z<=110)
def metrics(h):
    gz,gx=np.gradient(h.astype(float)*sy,dz,dx)
    slopes=np.hypot(gx,gz)[road]
    return dict(grid_samples=int(road.sum()),p95=float(np.percentile(slopes,95)),over_10percent=int((slopes>.1).sum()),max=float(slopes.max()))
report=dict(method='Constrained harmonic terrain relaxation; fixed footprints, sports platforms and anchors',before=metrics(before),after=metrics(after),changed_samples=int(np.sum(abs(after-before)>.00001)),maximum_height_change_m=float(np.max(abs(after-before))*sy),fixed_sample_error_m=float(np.max(abs(after-before)[~free])*sy),status='proposal_not_applied')
after.tofile(OUT/'proposal.raw')
(OUT/'report.json').write_text(json.dumps(report,indent=2),encoding='utf-8')
print(json.dumps(report,indent=2))
