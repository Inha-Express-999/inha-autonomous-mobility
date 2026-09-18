"""Extract cached OSM road geometry beyond the original campus boundary.
Widths are visual assumptions; no vehicle access is certified by this export.
"""
import json
import math
import xml.etree.ElementTree as ET
from pathlib import Path

BASE = Path(__file__).resolve().parents[1]
campus = json.loads((BASE / 'Assets/InhaCampus/Source/campus.json').read_text(encoding='utf-8'))
boundary = [(p['x'], p['z']) for p in campus['boundary']]
osm = ET.parse(BASE / 'Docs/MapResearch/2026-09-17/expanded_campus.osm').getroot()
nodes = {n.attrib['id']: ((float(n.attrib['lon'])-126.6535)*111320*math.cos(math.radians(37.4506)), (float(n.attrib['lat'])-37.4506)*110980) for n in osm.findall('node')}

def inside(p):
    x,z=p
    result=False
    for a,b in zip(boundary,boundary[1:]+boundary[:1]):
        if (a[1]>z)!=(b[1]>z) and x<(b[0]-a[0])*(z-a[1])/(b[1]-a[1])+a[0]: result=not result
    return result

def cross(a,b): return a[0]*b[1]-a[1]*b[0]
def sub(a,b): return (a[0]-b[0],a[1]-b[1])
def lerp(a,b,t):return (a[0]+(b[0]-a[0])*t,a[1]+(b[1]-a[1])*t)

clip_box=[(-450,-580),(620,-580),(620,330),(-450,330)]
clip_edges=list(zip(boundary,boundary[1:]+boundary[:1]))+list(zip(clip_box,clip_box[1:]+clip_box[:1]))

def retained_intervals(a,b):
    """Exact intersections preserve endpoints instead of dropping sampled points."""
    direction=sub(b,a);cuts=[0.0,1.0]
    for c,d in clip_edges:
        edge=sub(d,c);den=cross(direction,edge)
        if abs(den)<1e-12:continue
        t=cross(sub(c,a),edge)/den;u=cross(sub(c,a),direction)/den
        if 0<t<1 and -1e-9<=u<=1+1e-9:cuts.append(t)
    cuts=sorted(set(round(t,12) for t in cuts))
    for lo,hi in zip(cuts,cuts[1:]):
        p=lerp(a,b,(lo+hi)/2)
        if -450<p[0]<620 and -580<p[1]<330 and not inside(p):
            yield lerp(a,b,lo),lerp(a,b,hi)

roads=[]
for way in osm.findall('way'):
    tags={t.attrib['k']:t.attrib['v'] for t in way.findall('tag')}
    kind=tags.get('highway')
    if kind not in {'service','residential','unclassified','tertiary','secondary','footway','pedestrian','path','living_street'}: continue
    points=[nodes[n.attrib['ref']] for n in way.findall('nd')]
    pieces=[];piece=[]
    for a,b in zip(points,points[1:]):
        for start,end in retained_intervals(a,b):
            if piece and math.dist(piece[-1],start)>1e-6:
                pieces.append(piece);piece=[]
            if not piece:piece.append(start)
            steps=max(1,math.ceil(math.dist(start,end)/1.5))
            piece.extend(lerp(start,end,i/steps) for i in range(1,steps+1))
    if len(piece)>1:pieces.append(piece)
    for index,part in enumerate(pieces):
        if sum(math.dist(a,b) for a,b in zip(part,part[1:]))<.01:continue
        pedestrian=kind in {'footway','pedestrian','path'}
        roads.append(dict(id=f"{way.attrib['id']}_{index}",osmId=way.attrib['id'],name=tags.get('name',''),kind=kind,width=3 if pedestrian else 10 if kind in {'secondary','tertiary'} else 6,pedestrian=pedestrian,accessVerified=False,widthSource='synthetic_visual_assumption',points=[dict(x=x,y=0,z=z) for x,z in part]))
out=BASE/'Assets/CampusSim/Data/road_extensions.json'
out.write_text(json.dumps(dict(source='cached expanded_campus.osm',coordinateSystem='legacy campus metres, transformed by INHA UNIVERSITY',status='visual_geometry_only_not_routable',roads=roads),ensure_ascii=False,indent=2),encoding='utf-8')
print(f'Extracted {len(roads)} road pieces; vehicle and pedestrian classifications preserved; access unverified.')
