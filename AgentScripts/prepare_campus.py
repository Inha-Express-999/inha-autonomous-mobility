import json, math, sys, xml.etree.ElementTree as ET
sys.path.insert(0,'Temp/campus-python')
from shapely.geometry import Polygon, LineString
from shapely.ops import triangulate
from shapely import constrained_delaunay_triangles
from pathlib import Path
from PIL import Image, ImageDraw, ImageFont
root=ET.parse('Assets/InhaCampus/Source/campus.osm').getroot()
origin=(37.4506,126.6535)
# Way 1203054818 is described by OSM as open-air grandstand seating, although
# it carries building=commercial. Keep it out of generic building extrusion;
# CampusTerrain uses a dedicated, explicitly unverified visual prefab instead.
# Preserve the original OSM source and tags; this does not verify the stand geometry.
NON_BUILDING_STRUCTURE_WAYS={'1203054818'}
nodes={n.attrib['id']:((float(n.attrib['lon'])-origin[1])*111320*math.cos(math.radians(origin[0])),(float(n.attrib['lat'])-origin[0])*110980) for n in root.findall('node')}
ways=[]; holes={}
for w in root.findall('way'):
 t={e.attrib['k']:e.attrib['v'] for e in w.findall('tag')}; p=[nodes[e.attrib['ref']] for e in w.findall('nd')]
 if len(p)>2 and p[0]==p[-1]: p=p[:-1]
 ways.append((w.attrib['id'],t,p))
boundary=next(p for i,t,p in ways if i=='472447787')
for rel in root.findall('relation'):
 t={e.attrib['k']:e.attrib['v'] for e in rel.findall('tag')}
 if 'building' not in t:continue
 for member in rel.findall('member'):
  if member.attrib.get('role')=='outer':
   p=next((p for i,_,p in ways if i==member.attrib['ref']),None)
   if p:
    rid='r'+rel.attrib['id']+'_'+member.attrib['ref']; ways.append((rid,t,p))
    holes[rid]=[hp for m in rel.findall('member') if m.attrib.get('role')=='inner' for wi,_,hp in ways if wi==m.attrib['ref']]
def inside(p,poly):
 x,y=p; c=False
 for a,b in zip(poly,poly[1:]+poly[:1]):
  if (a[1]>y)!=(b[1]>y) and x<(b[0]-a[0])*(y-a[1])/(b[1]-a[1])+a[0]: c=not c
 return c
def vec(p):return {'x':round(p[0],3),'y':0,'z':round(p[1],3)}
items=[]
for i,t,p in ways:
 if not p or not any(inside(q,boundary) for q in p):continue
 if i in NON_BUILDING_STRUCTURE_WAYS:continue
 kind=''
 if 'building' in t:kind='building'
 elif 'highway' in t and t['highway'] not in ['construction']:kind='road'
 elif t.get('natural')=='water' or t.get('water') or t.get('amenity')=='fountain':kind='water'
 elif t.get('leisure') in ['pitch','track']:kind='sport'
 elif t.get('landuse') in ['grass','forest'] or t.get('natural') in ['wood','scrub'] or t.get('leisure') in ['garden','park']:kind='green'
 elif t.get('amenity')=='parking' or t.get('place')=='square':kind='plaza'
 if not kind:continue
 name=t.get('name',kind+' '+i)
 height=float(t.get('height',t.get('building:levels','0')).replace('m','').strip() or 0)
 if 'height' not in t:height*=3.8
 if not height:height={'본관':19,'정석학술정보관':40,'하이테크센터':58,'학생회관':16,'60주년기념관':56,'5호관':20,'2호관':20,'로스쿨관':24,'체육관':14}.get(name,15)
 hw=t.get('highway',''); width=3 if hw in ['footway','path','steps'] else 5 if hw=='pedestrian' else 7
 if hw in ['secondary','tertiary','residential']:width=12
 pieces=[p]
 if kind=='road':
  clipped=LineString(p).intersection(Polygon(boundary).buffer(4))
  pieces=[list(g.coords) for g in getattr(clipped,'geoms',[clipped]) if g.geom_type=='LineString']
 for pi,points in enumerate(pieces):
  triangles=[]
  if kind!='road' and len(points)>2:
   poly=Polygon(points,holes.get(i,[])).buffer(0)
   for tri in constrained_delaunay_triangles(poly).geoms:
    triangles.extend([vec(q) for q in list(tri.exterior.coords)[:3]])
  items.append(dict(id=i+'_'+str(pi),name=name,kind=kind,subtype=hw or t.get('sport',''),height=height,width=width,points=[vec(q) for q in points],holes=[{'points':[vec(q) for q in hp]} for hp in holes.get(i,[])],triangles=triangles))
data=dict(originLatitude=origin[0],originLongitude=origin[1],boundary=[vec(p) for p in boundary],features=items)
Path('Assets/InhaCampus/Source/campus.json').write_text(json.dumps(data,ensure_ascii=False,indent=2),encoding='utf-8')
im=Image.new('RGB',(1400,1400),'#e4e7de');d=ImageDraw.Draw(im);font=ImageFont.truetype('C:/Windows/Fonts/malgun.ttf',15)
def pix(p):return (700+p[0]*1.4,700-p[1]*1.4)
d.polygon([pix(p) for p in boundary],fill='#b6c892',outline='#222222')
for f in sorted(items,key=lambda f:f['kind']=='building'):
 p=[(q['x'],q['z']) for q in f['points']];pp=[pix(q) for q in p]
 if f['kind']=='road':d.line(pp,fill='#8a8d88',width=int(f['width']*1.4))
 elif len(p)>2:d.polygon(pp,fill={'building':'#c4b5a3','water':'#6093b5','sport':'#b39174','green':'#719056','plaza':'#d1ccc0'}[f['kind']],outline='#777777')
 if f['kind']=='building' and not f['name'].startswith('building'):d.text(pix(p[0]),f['name'],font=font,fill='black')
im.save('Docs/AI/GeographicLayout.png')
print({k:sum(f['kind']==k for f in items) for k in set(f['kind'] for f in items)})
for f in items:
 if f['kind'] in ['building','water']:print(f['id'],f['name'],round(sum(q['x'] for q in f['points'])/len(f['points'])),round(sum(q['z'] for q in f['points'])/len(f['points'])))
