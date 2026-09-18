"""Joint quadratic terrain design; constraints are enforced inside the solve.
Editor research tool, not runtime planning or measured accessibility data.
"""
import csv,json,runpy
from pathlib import Path
import numpy as np
import scipy.sparse as sp
import osqp

ROOT=Path(__file__).resolve().parents[1]
s={'__file__':str(ROOT/'AgentScripts/smooth_sports_roads.py')};exec((ROOT/'AgentScripts/smooth_sports_roads.py').read_text().split('protected=')[0],s)
s['n']=1025;s['dx']=s['dz']=2;s['transform']=s['Affine'](2,0,-901,0,2,-901)
s['before']=np.fromfile(s['OUT']/'FineTerrain/eastern-proposal.raw','<f4').reshape(1025,1025)
s['x'],s['z']=np.meshgrid(-900+np.arange(1025)*2,-900+np.arange(1025)*2)
n=s['n'];out=s['OUT'];before=s['before'].astype(float);height=before*s['sy']+s['oy']
x,z=s['x'],s['z'];dx,dz=s['dx'],s['dz']
region=(x>=-500)&(x<=450)&(z>=-450)&(z<=300)
indices=np.full((n,n),-1,int);indices[region]=np.arange(region.sum())
N=int(region.sum());base=height[region]
def dilate(mask):
    p=np.pad(mask,1)
    return np.logical_or.reduce([p[j:j+n,i:i+n] for j in range(3) for i in range(3)])
water=dilate(s['mask'](out/'water-triangles.raw'))
polygons={}
for row in csv.DictReader((out/'pedestrian-boundaries.csv').open(encoding='utf-8-sig')):
    if row['id']!='inkyung_roadside_pavement':continue
    polygons.setdefault(row['id'],[]).append((float(row['x']),float(row['z'])))
walk=s['rasterize']((({'type':'Polygon','coordinates':[[*v,v[0]]]},1) for v in polygons.values()),out_shape=(n,n),transform=s['transform'],all_touched=True).astype(bool)
fixed=water|dilate(walk)
pad=s['mask'](out/'sports-triangles.raw')&(abs(height+6.923518)<.003)
fixed|=pad
interior=region&np.roll(region,1,0)&np.roll(region,-1,0)&np.roll(region,1,1)&np.roll(region,-1,1)
fixed|=region&~interior
rows=[];cols=[];data=[];lo=[];hi=[]
def constraint(coeffs,lower,upper):
    row=len(lo)
    for index,value in coeffs:
        rows.append(row);cols.append(int(index));data.append(value)
    lo.append(lower);hi.append(upper)
for j,i in np.argwhere(fixed&region):constraint([(indices[j,i],1)],height[j,i],height[j,i])
groups=[]
def approach_mask(prefix):
    polys={}
    for row in csv.DictReader((out/'pedestrian-boundaries.csv').open(encoding='utf-8-sig')):
        if row['id'].startswith(prefix):polys.setdefault(row['id'],[]).append((float(row['x']),float(row['z'])))
    return dilate(s['rasterize']((({'type':'Polygon','coordinates':[[*v,v[0]]]},1) for v in polys.values()),out_shape=(n,n),transform=s['transform'],all_touched=True).astype(bool))
north=approach_mask('building_2_')
for path in (out/'FineTerrain/MissingFootprints').glob('north_*.raw'):north|=dilate(s['mask'](path))
assert not np.any(north&~region)
cells=indices[north];groups.append(('Building 2 north and entrance forecourts',north))
for index in cells[1:]:constraint([(index,1),(cells[0],-1)],0,0)
for line in (out/'Footprints/manifest.txt').read_text(encoding='utf-8-sig').splitlines():
    file,name=line.split('|',1);mask=dilate(s['mask'](out/'Footprints'/file))
    if file=='building_2.raw':mask|=approach_mask('anniversary_60_')
    if np.any(mask&~region):raise ValueError('Footprint extends outside design domain: '+name)
    cells=indices[mask];groups.append((name,mask))
    for index in cells[1:]:constraint([(index,1),(cells[0],-1)],0,0)
from scipy.spatial import ConvexHull
for prefix,anchor in [('rear_gate_',(258.68,60.72)),('main_gate_',(-27.86,-321.58))]:
    pts=[anchor]
    for row in csv.DictReader((out/'pedestrian-boundaries.csv').open(encoding='utf-8-sig')):
        if row['id'].startswith(prefix):pts.append((float(row['x']),float(row['z'])))
    pts=np.array(pts);hull=pts[ConvexHull(pts).vertices].tolist()
    m=dilate(s['rasterize']([({'type':'Polygon','coordinates':[[*hull,hull[0]]]},1)],out_shape=(n,n),transform=s['transform'],all_touched=True).astype(bool))
    assert not np.any(m&~region),prefix
    cells=indices[m];groups.append((prefix+'forecourt',m))
    for index in cells[1:]:constraint([(index,1),(cells[0],-1)],0,0)
for line in (out/'FineTerrain/EasternFootprints/manifest.txt').read_text(encoding='utf-8-sig').splitlines():
    file,name=line.split('|',1);m=dilate(s['mask'](out/'FineTerrain/EasternFootprints'/file))
    if '218081857_0' in name:m|=approach_mask('hitech_')
    assert not np.any(m&~region),name
    cells=indices[m];groups.append((name,m))
    for index in cells[1:]:constraint([(index,1),(cells[0],-1)],0,0)
road=dilate(s['mask'](out/'FineTerrain/current-road-footprint.raw'))&interior&~water
for j,i in np.argwhere(road):
    for dj,di,limit in ((0,1,.045*dx),(1,0,.045*dz)):
        if indices[j+dj,i+di]>=0:
            core=(-350<=x[j,i]<=-60 and -260<=z[j,i]<=110)
            if not core:
                continue
            constraint([(indices[j,i],1),(indices[j+dj,i+di],-1)],-limit,limit)
# Curvature regularization avoids abrupt changes between independently bounded
# slopes. It is secondary to the hard water, building and road constraints.
lr=[];lc=[];ld=[];row=0
for j,i in np.argwhere(interior):
    lr.append(row);lc.append(indices[j,i]);ld.append(4)
    for dj,di in ((1,0),(-1,0),(0,1),(0,-1)):
        lr.append(row);lc.append(indices[j+dj,i+di]);ld.append(-1)
    row+=1
L=sp.coo_matrix((ld,(lr,lc)),shape=(row,N)).tocsc()
P=(sp.eye(N,format='csc')+4*(L.T@L)).tocsc()
er=[];ec=[];ev=[];edge=0
for j,i in np.argwhere(road):
    for dj,di in ((0,1),(1,0)):
        if indices[j+dj,i+di]<0:continue
        er.extend([edge,edge]);ec.extend([indices[j,i],indices[j+dj,i+di]]);ev.extend([1,-1]);edge+=1
E=sp.coo_matrix((ev,(er,ec)),shape=(edge,N)).tocsc()
P=(P+10000*(E.T@E)).tocsc()
A=sp.coo_matrix((data,(rows,cols)),shape=(len(lo),N)).tocsc()
solver=osqp.OSQP();solver.setup(P=P,q=-base,A=A,l=np.array(lo),u=np.array(hi),verbose=False,eps_abs=1e-5,eps_rel=1e-6,max_iter=30000,polishing=True)
result=solver.solve()
report=dict(status=result.info.status,variables=N,constraints=len(lo),iterations=result.info.iter,applied=False,dependencies=dict(osqp=osqp.__version__))
if result.info.status=='solved':
    after=before.copy();after[region]=(result.x-s['oy'])/s['sy'];after=after.astype('<f4')
    values=after.astype(float)*s['sy']+s['oy']
    allroad=s['mask'](out/'FineTerrain/current-road-footprint.raw')&interior
    def metric(h):
        gz,gx=np.gradient(h*s['sy'],dz,dx);g=np.hypot(gx,gz)[allroad]
        return dict(samples=int(allroad.sum()),p95=float(np.percentile(g,95)),max=float(g.max()),above10=int((g>.1).sum()))
    report.update(before=metric(before),after=metric(after.astype(float)),maximum_change_m=float(np.max(abs(after-before))*s['sy']),fixed_error_m=float(np.max(abs(values-height)[fixed&region])),building_ranges={name:float(np.ptp(values[mask])) for name,mask in groups})
    after.tofile(out/'FineTerrain/hitech-proposal.raw')
(out/'FineTerrain/hitech-report.json').write_text(json.dumps(report,indent=2),encoding='utf-8')
print(json.dumps(report,indent=2))
