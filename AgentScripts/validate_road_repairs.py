"""Validate the final MCP geometry export, independently of the Editor audit."""
import json
import sys
from pathlib import Path

ROOT=Path(__file__).resolve().parents[1]
sys.path.insert(0,str(ROOT/'tmp/road-repair-python'))
sys.path.insert(0,str(ROOT/'.utmp/fifo-dijkstra-deps'))
sys.path.insert(0,str(ROOT/'backend/src'))
import numpy as np
import shapely
from shapely.geometry import LineString, Point
from shapely.ops import unary_union
from campus_sim.map_coordinates import MapCoordinates
from repair_road_connections import footprint

data=json.loads((ROOT/'artifacts/road_connections/final-meshes.json').read_text(encoding='utf-8'))
roads=[m for m in data['meshes'] if m['name'].startswith('Road ') or m['name']=='Continuous asphalt network']
shapes=[footprint(m) for m in roads]
union=unary_union(shapes)
graph=json.loads((ROOT/'maps/inha_osm_runtime/graph.json').read_text(encoding='utf-8'))
coords=MapCoordinates();missing=[]
for edge in graph['edges']:
    line=LineString([coords.to_unity(*p) for p in edge['polyline']])
    length=line.difference(union.buffer(.05)).length
    if length>.1:missing.append(dict(edge=edge['key'],missing_m=length))

sources=[]
for road in roads:
    vertices=np.array([[p['x'],p['y'],p['z']] for p in road['baked']])
    triangles=vertices[np.asarray(road['bakedTriangles']).reshape(-1,3)]
    polygons=shapely.polygons(triangles[:,:,[0,2]])
    sources.append((triangles,shapely.STRtree(polygons)))

def height(index, point):
    triangles,tree=sources[index]
    hits=tree.query(point.buffer(.002))
    if not len(hits):return None
    t=triangles[hits];a=t[:,0];b=t[:,1];c=t[:,2]
    cross=(b[:,0]-a[:,0])*(c[:,2]-a[:,2])-(b[:,2]-a[:,2])*(c[:,0]-a[:,0])
    good=abs(cross)>1e-8
    cross=np.where(good,cross,1)
    u=((point.x-a[:,0])*(c[:,2]-a[:,2])-(point.y-a[:,2])*(c[:,0]-a[:,0]))/cross
    v=((b[:,0]-a[:,0])*(point.y-a[:,2])-(b[:,2]-a[:,2])*(point.x-a[:,0]))/cross
    good &= (u>=-.001)&(v>=-.001)&(u+v<=1.001)
    values=a[:,1]+u*(b[:,1]-a[:,1])+v*(c[:,1]-a[:,1])
    return float(np.median(values[good])) if good.any() else None

seams=[]
for i in range(len(roads)):
    for j in range(i):
        overlap=shapes[i].intersection(shapes[j])
        if overlap.area<.01:continue
        inside=overlap.buffer(-.025)
        if inside.is_empty:inside=overlap
        boundary=inside.boundary
        deltas=[]
        for f in np.linspace(0,1,40):
            p=boundary.interpolate(float(f),normalized=True)
            a=height(i,p);b=height(j,p)
            if a is not None and b is not None:deltas.append(abs(a-b))
        seams.append(dict(a=roads[i]['name'],b=roads[j]['name'],samples=len(deltas),max_delta_m=max(deltas,default=0)))

report=dict(road_count=len(roads),asphalt_components=len(list(shapely.get_parts(union))),
            asphalt_area_m2=union.area,graph_edges=len(graph['edges']),missing_edges=missing,
            seam_samples=sum(s['samples'] for s in seams),max_seam_delta_m=max((s['max_delta_m'] for s in seams),default=0),seams=seams,
            scope='MCP-exported static visual/collision geometry; not physical driving or access approval')
(ROOT/'artifacts/road_connections/final-independent-audit.json').write_text(json.dumps(report,indent=2,ensure_ascii=False),encoding='utf-8')
print(json.dumps({k:v for k,v in report.items() if k!='seams'},indent=2,ensure_ascii=True))
assert not missing, 'Routable centreline is missing asphalt'
assert report['max_seam_delta_m']<.06, 'Connection step exceeds 6cm development threshold'
assert report['asphalt_components']==1, 'Disconnected asphalt components'
