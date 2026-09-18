"""Restore selected landmark corridors south of the obsolete visual crop.
Widths remain visual assumptions; these are not approved vehicle routes.
"""
import json
import math
import runpy
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
s = runpy.run_path(str(ROOT/'AgentScripts/scope_campus_connections.py'))
roads = []
for wid, kind, refs in s['ways']:
    selected = s['selected'].get(wid, set())
    pieces, piece = [], []
    for u, v in zip(refs, refs[1:]):
        if tuple(sorted((u,v))) not in selected:
            if piece: pieces.append(piece); piece=[]
            continue
        a,b = s['nodes'][u],s['nodes'][v]
        if min(a[1],b[1]) >= -580:
            if piece: pieces.append(piece); piece=[]
            continue
        if max(a[1],b[1]) > -580:
            t=(-580-a[1])/(b[1]-a[1])
            cut=(a[0]+t*(b[0]-a[0]),-580)
            if a[1]>-580: a=cut
            else: b=cut
        if piece and math.dist(piece[-1],a)>.00001:
            pieces.append(piece); piece=[]
        if not piece: piece=[a]
        count=max(1,math.ceil(math.dist(a,b)/1.5))
        piece.extend((a[0]+(b[0]-a[0])*i/count,a[1]+(b[1]-a[1])*i/count) for i in range(1,count+1))
    if piece: pieces.append(piece)
    for i,points in enumerate(pieces):
        pedestrian=kind in {'footway','pedestrian','path'}
        roads.append(dict(id=f'{wid}_restored_{i}',osmId=wid,kind=kind,
                          width=3 if pedestrian else 10 if kind in {'secondary','tertiary'} else 6,
                          pedestrian=pedestrian,accessVerified=False,
                          points=[dict(x=x,y=0,z=z) for x,z in points]))
output=dict(source='cached expanded_campus.osm',status='visual_geometry_only_not_routable',roads=roads)
(ROOT/'Assets/CampusSim/Data/road_connections_restored.json').write_text(json.dumps(output,indent=2),encoding='utf-8')
(ROOT/'Assets/CampusSim/Data/road_connections_restored.txt').write_text('\n'.join(r['id']+'|'+r['osmId']+'|'+str(r['width'])+'|'+';'.join(str(p['x'])+','+str(p['z']) for p in r['points']) for r in roads),encoding='utf-8')
print('Restored pieces:',len(roads),'length:',sum(math.dist((a['x'],a['z']),(b['x'],b['z'])) for r in roads for a,b in zip(r['points'],r['points'][1:])))
