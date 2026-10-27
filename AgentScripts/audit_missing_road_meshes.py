"""Find source-backed vehicle corridors missing from the Unity asphalt export."""
import sys
import json
from pathlib import Path

ROOT=Path(__file__).resolve().parents[1]
sys.path.insert(0,str(ROOT/'tmp/road-repair-python'))
sys.path.insert(0,str(ROOT/'.utmp/fifo-dijkstra-deps'))
sys.path.insert(0,str(ROOT/'backend/src'))
from shapely.geometry import LineString
from shapely.ops import unary_union
from campus_sim.map_coordinates import MapCoordinates
from repair_road_connections import footprint

graph=json.loads((ROOT/'maps/inha_osm_runtime/graph.json').read_text(encoding='utf-8'))
snapshot=json.loads((ROOT/'artifacts/road_connections/before-meshes.json').read_text(encoding='utf-8'))
union=unary_union([footprint(m) for m in snapshot['meshes'] if m['name'].startswith('Road ') or m['name']=='Continuous asphalt network'])
coordinates=MapCoordinates()
seen=set();rows=[]
for e in graph['edges']:
    points=tuple(coordinates.to_unity(*p) for p in e['polyline'])
    key=min(points,tuple(reversed(points)))
    if key in seen:continue
    seen.add(key)
    line=LineString(points)
    gap=line.difference(union.buffer(.05))
    if gap.length>.25:
        rows.append(dict(key=e['key'],length_m=line.length,missing_m=gap.length,points=points))
(ROOT/'artifacts/road_connections/missing-corridors.json').write_text(json.dumps(rows,indent=2),encoding='utf-8')
print(json.dumps([dict(key=r['key'],length_m=r['length_m'],missing_m=r['missing_m']) for r in rows],indent=2))
