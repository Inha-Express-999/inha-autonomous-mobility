from pathlib import Path
import numpy as np
from scipy.sparse import coo_matrix
from scipy.sparse.linalg import lsqr
from rasterio.features import rasterize
from affine import Affine
import json
out=Path('Docs/MapResearch/Iterations/DormitoryRoadGrade');n=1025
before=np.fromfile(out/'before.raw','<f4').reshape(n,n);h=before.astype(float)*100-16
x,z=np.meshgrid(-900+np.arange(n)*2,-900+np.arange(n)*2)
tris=np.fromfile(out/'building-xz.raw','<f4').reshape(-1,3,2)
polys=[]
for t in tris:
 a=t[1]-t[0];b=t[2]-t[0]
 if abs(a[0]*b[1]-a[1]*b[0])>.001:polys.append(({'type':'Polygon','coordinates':[[*t.tolist(),t[0].tolist()]]},1))
protected=rasterize(polys,out_shape=(n,n),transform=Affine(2,0,-901,0,2,-901),all_touched=True).astype(bool)
free=(x>556)&(x<594)&(z>-352)&(z<-304)&~protected
idx=np.full((n,n),-1);idx[free]=np.arange(free.sum())
r=[];c=[];v=[];rhs=[]
def eq(terms,target,weight):
 row=len(rhs)
 for j,i,a in terms:
  if idx[j,i]>=0:r.append(row);c.append(idx[j,i]);v.append(a*weight)
  else:target-=a*h[j,i]
 rhs.append(target*weight)
for j,i in np.argwhere(free):
 eq([(j,i,1)],h[j,i],.12)
 eq([(j,i,4),(j-1,i,-1),(j+1,i,-1),(j,i-1,-1),(j,i+1,-1)],0,1)
 for dj,di in [(1,0),(0,1)]:
  road=559<=x[j,i]<=569 and -334<=z[j,i]<=-308
  eq([(j,i,1),(j+dj,i+di,-1)],0,4 if road else .15)
a=coo_matrix((v,(r,c)),shape=(len(rhs),free.sum())).tocsr()
solution=lsqr(a,np.array(rhs),atol=1e-10,btol=1e-10,iter_lim=10000)[0]
after=before.copy();after[free]=((solution+16)/100).astype('<f4');after.tofile(out/'coupled-proposal.raw')
report={'free_samples':int(free.sum()),'max_change_m':float(abs(after-before).max()*100),'building_protected':True,'applied':False}
(out/'coupled-report.json').write_text(json.dumps(report,indent=2));print(report)
