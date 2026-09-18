"""Constrain exported road face gradients through terrain interpolation weights.

Offline scene authoring only; the 7% component bound is a visual design target,
not a validated vehicle or accessibility limit. Re-baked geometry must be audited.
"""
from pathlib import Path
import json
import numpy as np
from affine import Affine
from rasterio.features import rasterize
from scipy.ndimage import binary_dilation
import scipy.sparse as sp
import osqp

out = Path('Docs/MapResearch/Iterations/EastRoadConstrained')
n = 1025
before = np.fromfile(out/'before.raw', '<f4').reshape(n,n)
h = before.astype(float)*100-16
x,z = np.meshgrid(-900+np.arange(n)*2, -900+np.arange(n)*2)
triangles = {k:np.fromfile(out/(k+'.raw'), '<f4').reshape(-1,3,3).astype(float)
             for k in ('buildings','water','roads')}
def mask(key):
    polygons=[]
    for triangle in triangles[key]:
        p=triangle[:,[0,2]].tolist()
        polygons.append(({'type':'Polygon','coordinates':[[*p,p[0]]]},1))
    return rasterize(polygons,out_shape=(n,n),transform=Affine(2,0,-901,0,2,-901),
                     all_touched=True).astype(bool)
water=mask('water'); road=mask('roads')
protected=binary_dilation(mask('buildings')) | (water & ~binary_dilation(road,iterations=3))
protected |= np.fromfile(out/'pedestrian-fixed.raw', np.uint8).reshape(n,n).astype(bool)
region=(x>195)&(x<295)&(z>-170)&(z<-25)
free=region&~protected
index=np.full((n,n),-1,int); index[free]=np.arange(free.sum()); size=int(free.sum())
rows=[];cols=[];data=[];lo=[];hi=[]
def constrain(terms,lower,upper):
    row=len(lo)
    for (j,i),v in terms.items():
        if index[j,i]>=0:
            rows.append(row);cols.append(index[j,i]);data.append(v)
        else:
            lower-=v*h[j,i];upper-=v*h[j,i]
    lo.append(lower);hi.append(upper)
for j,i in np.argwhere(free):
    cap=min(h[j,i]+2.5,1.95) if water[j,i] else h[j,i]+2.5
    constrain({(j,i):1},h[j,i]-2.5,cap)

def weights(p):
    fx=(p[0]+900)/2;fz=(p[2]+900)/2;i=int(np.floor(fx));j=int(np.floor(fz))
    u=fx-i;v=fz-j
    return {(j,i):(1-u)*(1-v),(j,i+1):u*(1-v),(j+1,i):(1-u)*v,(j+1,i+1):u*v}
face_count=0
for tri in triangles['roads']:
    center=tri.mean(axis=0)
    if not (205<center[0]<280 and -155<center[2]<-40):continue
    basis=tri[1:,[0,2]]-tri[0,[0,2]]
    if abs(np.linalg.det(basis))<.0002:continue
    inv=np.linalg.inv(basis)
    ws=[weights(p) for p in tri]
    if not any(free[j,i] for w in ws for j,i in w):continue
    offsets=np.array([p[1]-sum(h[j,i]*v for (j,i),v in w.items()) for p,w in zip(tri,ws)])
    for axis in range(2):
        factors=np.array([-inv[axis].sum(),*inv[axis]])
        terms={}
        for f,w in zip(factors,ws):
            for k,v in w.items():terms[k]=terms.get(k,0)+f*v
        offset=float(factors@offsets)
        constrain(terms,-.07-offset,.07-offset)
    face_count+=1

lr=[];lc=[];lv=[];row=0;targets=[]
for j,i in np.argwhere(free):
    fixed=0
    for jj,ii,v in ((j,i,4),(j-1,i,-1),(j+1,i,-1),(j,i-1,-1),(j,i+1,-1)):
        if index[jj,ii]>=0:lr.append(row);lc.append(index[jj,ii]);lv.append(v)
        else:fixed+=v*h[jj,ii]
    targets.append(-fixed);row+=1
L=sp.coo_matrix((lv,(lr,lc)),shape=(row,size)).tocsc()
P=sp.eye(size,format='csc')+2*(L.T@L)
q=-h[free]-2*(L.T@np.array(targets))
A=sp.coo_matrix((data,(rows,cols)),shape=(len(lo),size)).tocsc()
solver=osqp.OSQP()
solver.setup(P=P,q=q,A=A,l=np.array(lo),u=np.array(hi),verbose=False,
             eps_abs=1e-5,eps_rel=1e-6,max_iter=50000,polishing=True)
result=solver.solve()
strict_status=result.info.status
slack_count=2*face_count
if strict_status=='primal infeasible':
    # Keep water/building constraints hard. Quantify unavoidable road residuals
    # instead of silently weakening protected geometry or declaring success.
    slack=sp.coo_matrix((np.ones(slack_count),
        (np.arange(size,size+slack_count),np.arange(slack_count))),
        shape=(A.shape[0],slack_count)).tocsc()
    relaxed=sp.hstack([A,slack],format='csc')
    objective=sp.block_diag((P,sp.eye(slack_count)*100000),format='csc')
    solver=osqp.OSQP()
    solver.setup(P=objective,q=np.r_[q,np.zeros(slack_count)],A=relaxed,
                 l=np.array(lo),u=np.array(hi),verbose=False,eps_abs=1e-5,
                 eps_rel=1e-6,max_iter=50000,polishing=True)
    result=solver.solve()
report=dict(status=result.info.status,variables=size,road_faces=face_count,
            strict_status=strict_status,iterations=int(result.info.iter),applied=False)
if result.info.status=='solved':
    after=before.copy();after[free]=((result.x[:size]+16)/100).astype('<f4')
    after.tofile(out/'proposal.raw')
    report['max_change_m']=float(abs(after-before).max()*100)
    report['fixed_change_m']=float(abs(after-before)[~free].max()*100)
    report['maximum_gradient_constraint_slack']=float(np.max(abs(result.x[size:]))) if len(result.x)>size else 0.0
    def predict(values):
        area_over=0.;maximum=0.;count=0
        for tri in triangles['roads']:
            updated=tri.copy()
            for k,p in enumerate(tri):
                updated[k,1]+=sum((values[j,i]-h[j,i])*v for (j,i),v in weights(p).items())
            normal=np.cross(updated[1]-updated[0],updated[2]-updated[0])
            area=abs(normal[1])*.5
            if area<.0001:continue
            grade=np.hypot(normal[0],normal[2])/abs(normal[1])
            maximum=max(maximum,float(grade));count+=1
            if grade>.1:area_over+=area
        return dict(faces=count,max_gradient=maximum,area_over_10_percent=float(area_over))
    report['exported_mesh_before']=predict(h)
    report['exported_mesh_prediction']=predict(after.astype(float)*100-16)
(out/'proposal.json').write_text(json.dumps(report,indent=2))
print(json.dumps(report,indent=2))
