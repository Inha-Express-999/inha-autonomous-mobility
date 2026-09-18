"""Limit exterior visual roads to OSM-connected landmark approach corridors.

This is geometry selection, not a routable/accessible vehicle graph. OSM access,
one-way constraints, Stop positions and service eligibility remain unverified.
"""
import heapq
import json
import math
from pathlib import Path
import xml.etree.ElementTree as ET

ROOT=Path(__file__).resolve().parents[1]
DATA=ROOT/'Assets/CampusSim/Data'
campus=json.loads((ROOT/'Assets/InhaCampus/Source/campus.json').read_text(encoding='utf-8'))
boundary=[(p['x'],p['z']) for p in campus['boundary']]
osm=ET.parse(ROOT/'Docs/MapResearch/2026-09-17/expanded_campus.osm').getroot()
def project(lon,lat):return ((lon-126.6535)*111320*math.cos(math.radians(37.4506)),(lat-37.4506)*110980)
nodes={n.attrib['id']:project(float(n.attrib['lon']),float(n.attrib['lat'])) for n in osm.findall('node')}
def inside(p):
    return sum((a[1]>p[1])!=(b[1]>p[1]) and p[0]<(b[0]-a[0])*(p[1]-a[1])/(b[1]-a[1])+a[0] for a,b in zip(boundary,boundary[1:]+boundary[:1]))%2==1
ways=[]
for way in osm.findall('way'):
    tags={t.attrib['k']:t.attrib['v'] for t in way.findall('tag')}
    if tags.get('highway') not in {'service','residential','unclassified','tertiary','secondary','footway','pedestrian','path','living_street'}:continue
    refs=[n.attrib['ref'] for n in way.findall('nd')]
    if all(n in nodes for n in refs):ways.append((way.attrib['id'],tags['highway'],refs))
targets=[]
for node in osm.findall('node'):
    tags={t.attrib['k']:t.attrib['v'] for t in node.findall('tag')}
    if tags.get('railway')=='subway_entrance' and any('인하대역' in v for v in tags.values()):
        targets.append(('station_exit_'+tags['ref'],nodes[node.attrib['id']]))
for building in json.loads((DATA/'dormitory_drafts.json').read_text(encoding='utf-8'))['buildings']:
    p=building['points'];targets.append((building['id'],(sum(v['x'] for v in p)/len(p),sum(v['z'] for v in p)/len(p))))
# This source location is a guest-house arrival candidate, not a verified footprint.
targets.append(('dorm_3_location_candidate',project(126.659725,37.446848)))
selected={};report=[]
for mode in ('road','pedestrian'):
    graph={}
    for wid,kind,refs in ways:
        if mode=='road' and kind in {'footway','pedestrian','path'}:continue
        for a,b in zip(refs,refs[1:]):
            distance=math.dist(nodes[a],nodes[b])
            graph.setdefault(a,[]).append((b,distance,wid));graph.setdefault(b,[]).append((a,distance,wid))
    distance={n:0 for n in graph if inside(nodes[n])};previous={};queue=[(0,n) for n in distance];heapq.heapify(queue)
    while queue:
        cost,n=heapq.heappop(queue)
        if cost!=distance[n]:continue
        for other,length,wid in graph[n]:
            candidate=cost+length
            if candidate<distance.get(other,math.inf):
                distance[other]=candidate;previous[other]=(n,wid);heapq.heappush(queue,(candidate,other))
    for name,p in targets:
        nearest=min(graph,key=lambda n:math.dist(p,nodes[n]))
        record={'target':name,'mode':mode,'nearest_node':nearest,'target_gap_m':math.dist(p,nodes[nearest]),'osm_connected':nearest in distance,'route_verified':False}
        if nearest in distance:
            record['corridor_length_m']=distance[nearest]
            n=nearest
            while n in previous:
                parent,wid=previous[n];selected.setdefault(wid,set()).add(tuple(sorted((n,parent))));n=parent
        report.append(record)
def segment_distance(p,a,b):
    d=(b[0]-a[0],b[1]-a[1]);den=d[0]**2+d[1]**2
    t=max(0,min(1,((p[0]-a[0])*d[0]+(p[1]-a[1])*d[1])/den)) if den else 0
    return math.dist(p,(a[0]+t*d[0],a[1]+t*d[1]))
source=json.loads((DATA/'road_extensions.json').read_text(encoding='utf-8'));kept=[]
for road in source['roads']:
    edges=selected.get(road['osmId'],set());parts=[];part=[]
    for a,b in zip(road['points'],road['points'][1:]):
        midpoint=((a['x']+b['x'])/2,(a['z']+b['z'])/2)
        retain=any(segment_distance(midpoint,nodes[u],nodes[v])<.1 for u,v in edges)
        if retain:
            if not part:part=[a]
            part.append(b)
        elif part:parts.append(part);part=[]
    if part:parts.append(part)
    for i,p in enumerate(parts):kept.append(dict(road,id=road['id']+'_scoped_'+str(i),points=p))
def length(roads):return sum(math.dist((a['x'],a['z']),(b['x'],b['z'])) for r in roads for a,b in zip(r['points'],r['points'][1:]))
result=dict(source,roads=kept,scope='Campus landmark approach corridors only; all route and access eligibility unverified')
(DATA/'road_connections_scoped.json').write_text(json.dumps(result,ensure_ascii=False,indent=2),encoding='utf-8')
audit={'original_pieces':len(source['roads']),'scoped_pieces':len(kept),'original_length_m':length(source['roads']),'retained_length_m':length(kept),'targets':report,'note':'OSM node connectivity only. Target gaps are not bridged by invented lines. Dorm 3 location remains a candidate.'}
(ROOT/'Docs/MapResearch/Iterations/exterior-road-scope.json').write_text(json.dumps(audit,ensure_ascii=False,indent=2),encoding='utf-8')
print(json.dumps({k:v for k,v in audit.items() if k!='targets'},indent=2))
print('Disconnected target modes:',[(r['target'],r['mode']) for r in report if not r['osm_connected']])
