import sys,json
sys.path.insert(0,'Temp/campus-python')
from shapely.geometry import LineString,Polygon
from shapely.ops import unary_union,triangulate
from shapely import constrained_delaunay_triangles
from pathlib import Path
d=json.loads(Path('Assets/InhaCampus/Source/campus.json').read_text(encoding='utf8'))
road=[];walk=[]
for f in d['features']:
 if f['kind']!='road':continue
 line=LineString([(p['x'],p['z']) for p in f['points']])
 if f['width']>=7:road.append(line.buffer(f['width']/2,cap_style=2,join_style=2))
 else:walk.append(line.buffer(f['width']/2,cap_style=2,join_style=2))
roads=unary_union(road);walks=unary_union(walk+[roads.buffer(2,join_style=2)]).difference(roads)
def vec(p):return {'x':p[0],'y':0,'z':p[1]}
def ts(poly):return [vec(p) for t in constrained_delaunay_triangles(poly).geoms for p in list(t.exterior.coords)[:3]]
def loops(poly):return [{'points':[vec(p) for p in ring.coords]} for part in getattr(poly,'geoms',[poly]) for ring in [part.exterior,*part.interiors]]
accessible_edges=roads.boundary.difference(unary_union(walk).buffer(.3))
kerbs=[{'points':[vec(p) for p in line.coords]} for line in getattr(accessible_edges,'geoms',[accessible_edges]) if line.geom_type=='LineString']
out={'roads':ts(roads),'walks':ts(walks),'kerbs':kerbs,'yellow':loops(roads.buffer(-.25,join_style=2))}
Path('Assets/InhaCampus/Source/road-surfaces.json').write_text(json.dumps(out),encoding='utf8')
print('Road union triangles',len(out['roads'])//3,'sidewalk triangles',len(out['walks'])//3)
