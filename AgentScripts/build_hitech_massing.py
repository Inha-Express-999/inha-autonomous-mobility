"""Photo-guided simplified Hi-Tech mass; heights are illustrative, not surveyed."""
import csv
import json
from pathlib import Path
import numpy as np

root = Path(__file__).resolve().parents[1]
feature = next(f for f in json.loads((root/'Assets/InhaCampus/Source/campus.json').read_text(encoding='utf-8'))['features'] if f['id']=='218081857_0')
points = [np.array([p['x'], 0., p['z']]) for p in feature['points']]
out = root/'Docs/MapResearch/Iterations/HiTechMassing'
out.mkdir(parents=True, exist_ok=True)
faces = []
def tri(a,b,c,normal,material):
    a,b,c=map(np.asarray,(a,b,c))
    if np.dot(np.cross(b-a,c-a),normal)<0: b,c=c,b
    faces.append([material,*a,*b,*c])
def quad(a,b,c,d,normal,material):
    tri(a,b,c,normal,material);tri(a,c,d,normal,material)
def area(poly):
    return sum(a[0]*b[2]-b[0]*a[2] for a,b in zip(poly,poly[1:]+poly[:1]))/2
# The footprint's elbow separates the south-east tower from the north-west wing.
parts=[([0,1,2,7,8],58.,16),([2,3,4,5,6,7],22.,6)]
assert abs(sum(abs(area([points[i] for i in ids])) for ids,_,_ in parts)-abs(area(points)))<.001
for ids,height,floors in parts:
    poly=[points[i] for i in ids]
    winding=np.sign(area(poly))
    # Preserve the source triangulation: the footprint has a concave elbow.
    roof_area=0.
    for k in range(0,len(feature['triangles']),3):
        roof=[np.array([p['x'],0.,p['z']]) for p in feature['triangles'][k:k+3]]
        if not all(any(np.linalg.norm(p-v)<.001 for v in poly) for p in roof):continue
        roof_area+=abs(area(roof))
        tri(*(p+[0,height,0] for p in roof),[0,1,0],2)
    assert abs(roof_area-abs(area(poly)))<.001
    for k,(a,b) in enumerate(zip(poly,poly[1:]+poly[:1])):
        direction=b-a;length=np.linalg.norm(direction);direction/=length
        normal=np.array([direction[2],0,-direction[0]])*winding
        internal={ids[k],ids[(k+1)%len(ids)]}=={2,7}
        bottom=22 if internal else 0
        if height<=bottom:continue
        quad(a+[0,bottom,0],a+[0,height,0],b+[0,height,0],b+[0,bottom,0],normal,1)
        if height>30:
            # Restrained dark roof cap and the tall end-wall glazing are the
            # photo's salient accents; dimensions remain a visual approximation.
            offset=normal*.06
            quad(a+offset+[0,height-1.15,0],a+offset+[0,height,0],
                 b+offset+[0,height,0],b+offset+[0,height-1.15,0],normal,2)
        if height>30 and ids[k]==0 and ids[(k+1)%len(ids)]==1:
            start=a+direction*(length*.24)+normal*.03
            end=a+direction*(length*.47)+normal*.03
            quad(start+[0,.8,0],start+[0,height-.6,0],end+[0,height-.6,0],end+[0,.8,0],normal,0)
            for floor in range(1,floors):
                y=floor*height/floors
                quad(start+normal*.01+[0,y-.045,0],start+normal*.01+[0,y+.045,0],
                     end+normal*.01+[0,y+.045,0],end+normal*.01+[0,y-.045,0],normal,1)
            continue
        bays=max(1,int((length-2)/3.4));pitch=(length-2)/bays
        for floor in range(floors):
            low=floor*height/floors+.85;high=(floor+1)*height/floors-.3
            if low<bottom:continue
            for bay in range(bays):
                start=a+direction*(1+bay*pitch+.18)+normal*.025
                end=a+direction*(1+(bay+1)*pitch-.18)+normal*.025
                quad(start+[0,low,0],start+[0,high,0],end+[0,high,0],end+[0,low,0],normal,0)
with (out/'triangles.csv').open('w',newline='') as f:csv.writer(f).writerows(faces)
(out/'proposal.json').write_text(json.dumps(dict(source_osm_id=feature['id'],parts=[dict(point_indices=i,height_m=h,floors=n) for i,h,n in parts],footprint_area_m2=abs(area(points)),triangles=len(faces),status='photo_based_synthetic_dimensions',reference='https://www.dnews.co.kr/uhtml/view.jsp?idxno=202209211119048250511'),indent=2))
print(f'{len(faces)} triangles, original footprint preserved')
