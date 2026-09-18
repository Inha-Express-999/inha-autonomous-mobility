"""Local synthetic grading candidate; never modifies the Unity scene."""
import json
from pathlib import Path
import numpy as np
from affine import Affine
from rasterio.features import rasterize
from scipy.ndimage import binary_dilation
from scipy.sparse import coo_matrix
from scipy.sparse.linalg import spsolve

ROOT=Path(__file__).resolve().parents[1]
SOURCE=ROOT/'Docs/MapResearch/Iterations/SportsRoadGrading'
OUT=SOURCE/'FineTerrain'
n=1025; d=2
before=np.fromfile(OUT/'baseline-1025.raw','<f4').reshape(n,n)
height=before.astype(float)*100-16
x,z=np.meshgrid(-900+np.arange(n)*d,-900+np.arange(n)*d)
transform=Affine(d,0,-901,0,d,-901)
def mask(path):
    triangles=np.fromfile(path,'<f4').reshape(-1,3,2)
    return rasterize((({'type':'Polygon','coordinates':[[*t.tolist(),t[0].tolist()]]},1)
                      for t in triangles),out_shape=(n,n),transform=transform,
                     all_touched=True).astype(bool)
supports=np.load(OUT/'building-supports-1025.npz')
target=supports['building_5']
region=(x>0)&(x<280)&(z>-200)&(z<160)
fixed=binary_dilation(mask(SOURCE/'water-triangles.raw'),structure=np.ones((3,3)))
for key in supports.files:
    if key!='building_5':fixed|=supports[key]
assert not np.any(target&fixed), 'Independent building target intersects fixed objects'
assert not np.any(target&~region), 'Building extends outside design region'
free=region&~fixed&~target
after=height.copy();after[target]=1.5
index=np.full((n,n),-1,int);index[free]=np.arange(free.sum())
jj,ii=np.nonzero(free);N=len(jj)
rows=list(range(N));cols=list(range(N));values=[4.02]*N
rhs=.02*height[free]
for dj,di in ((-1,0),(1,0),(0,-1),(0,1)):
    neighbor=index[jj+dj,ii+di];valid=neighbor>=0
    rows.extend(np.arange(N)[valid]);cols.extend(neighbor[valid]);values.extend([-1]*int(valid.sum()))
    rhs[~valid]+=after[jj[~valid]+dj,ii[~valid]+di]
after[free]=spsolve(coo_matrix((values,(rows,cols)),shape=(N,N)).tocsr(),rhs)
road=mask(SOURCE/'active-roads.raw')&region
hotspot=road&(x>150)&(x<200)&(z>-130)&(z<-80)
def metrics(h,m):
    gz,gx=np.gradient(h,d,d);q=np.hypot(gx,gz)[m]
    return dict(samples=int(m.sum()),p95=float(np.percentile(q,95)),maximum=float(q.max()),above10=int((q>.1).sum()))
report=dict(status='candidate_not_applied',target_height_m=1.5,
            region_before=metrics(height,road),region_after=metrics(after,road),
            hotspot_before=metrics(height,hotspot),hotspot_after=metrics(after,hotspot),
            fixed_error_m=float(np.max(abs(after-height)[fixed])),
            outside_error_m=float(np.max(abs(after-height)[~region])),
            maximum_change_m=float(np.max(abs(after-height))))
((after+16)/100).astype('<f4').tofile(OUT/'lakeside-proposal.raw')
(OUT/'lakeside-report.json').write_text(json.dumps(report,indent=2))
print(json.dumps(report,indent=2))
