"""Build a restrained photo-inspired facade; dimensions remain synthetic."""
import csv,json,math
from pathlib import Path
import numpy as np

ROOT=Path(__file__).resolve().parents[1]
OUT=ROOT/'Docs/MapResearch/Iterations/AnniversaryMassing'
data=json.loads((OUT/'proposal.json').read_text())
faces=[]
def triangle(a,b,c,normal,material):
    a,b,c=map(np.array,(a,b,c))
    if np.dot(np.cross(b-a,c-a),normal)<0:b,c=c,b
    faces.append([material,*a,*b,*c])
def quad(a,b,c,d,normal,material):
    triangle(a,b,c,normal,material);triangle(a,c,d,normal,material)
for part in data['parts']:
    points=[np.array([v['x'],0,v['z']]) for v in part['points']]
    height=part['height']
    area=sum(a[0]*b[2]-b[0]*a[2] for a,b in zip(points,points[1:]+points[:1]))
    center=np.mean(points,axis=0);center[1]=height
    for a,b in zip(points,points[1:]+points[:1]):
        ah=a+np.array([0,height,0]);bh=b+np.array([0,height,0])
        triangle(center,ah,bh,[0,1,0],1)
        direction=b-a;length=np.linalg.norm(direction);direction/=length
        normal=np.array([direction[2],0,-direction[0]])*(1 if area>0 else -1)
        # Internal wall only above the low-wing roof.
        internal=abs(a[2]-35)<.001 and abs(b[2]-35)<.001
        bottom=19 if internal else 0
        if height<=bottom:continue
        quad(a+[0,bottom,0],ah,bh,b+[0,bottom,0],normal,0)
        offset=normal*.035
        def band(y,width):
            quad(a+offset+[0,y-width/2,0],a+offset+[0,y+width/2,0],
                 b+offset+[0,y+width/2,0],b+offset+[0,y-width/2,0],normal,2)
        band(height-.45,.9)
        if not internal:band(.25,.5)
        # Broad gray perimeter framing is a salient photo feature, represented
        # with planar strips rather than costly rounded facade geometry.
        if not internal:
            width=2.2 if height>30 else .8
            for start,end in ((a,a+direction*width),(b-direction*width,b)):
                quad(start+offset+[0,0,0],start+offset+[0,height,0],
                     end+offset+[0,height,0],end+offset+[0,0,0],normal,2)
        floors=round(height/3.8)
        for floor in range(1,floors):
            y=height*floor/floors
            if y>bottom:band(y,.10)
        for bay in range(1,max(1,int(length/4))):
            p=a+direction*(bay*length/max(1,int(length/4)))+offset
            side=direction*.045
            quad(p-side+[0,bottom,0],p-side+[0,height,0],
                 p+side+[0,height,0],p+side+[0,bottom,0],normal,2)
with (OUT/'triangles.csv').open('w',newline='') as f:
    csv.writer(f).writerows(faces)
print(f'{len(faces)} triangles; 3 material groups; original footprint retained')
