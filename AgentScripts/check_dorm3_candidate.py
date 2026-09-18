"""Compare a historical map destination with cached OSM; never invent an outline."""
import json
import math
import xml.etree.ElementTree as ET
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
OUT = ROOT / 'Docs/MapResearch/Iterations'
candidate = json.loads((OUT / 'dorm3-location-candidate.json').read_text())
lon, lat = candidate['longitude'], candidate['latitude']

def project(node):
    return ((float(node.attrib['lon'])-lon)*111320*math.cos(math.radians(lat)),
            (float(node.attrib['lat'])-lat)*110980)

def segment_distance(a, b):
    dx, dy = b[0]-a[0], b[1]-a[1]
    length = dx*dx+dy*dy
    t = max(0, min(1, -(a[0]*dx+a[1]*dy)/length)) if length else 0
    return math.hypot(a[0]+t*dx, a[1]+t*dy)

def contains_origin(points):
    inside = False
    for a, b in zip(points, points[1:]):
        if (a[1] > 0) != (b[1] > 0):
            x = a[0] + (b[0]-a[0])*(-a[1])/(b[1]-a[1])
            if x > 0:
                inside = not inside
    return inside

osm = ET.parse(ROOT / 'Docs/MapResearch/2026-09-17/expanded_campus.osm').getroot()
nodes = {n.attrib['id']: project(n) for n in osm.findall('node')}
buildings, paths = [], []
for way in osm.findall('way'):
    tags = {t.attrib['k']: t.attrib['v'] for t in way.findall('tag')}
    refs = [n.attrib['ref'] for n in way.findall('nd')]
    if len(refs) < 2 or any(n not in nodes for n in refs):
        continue
    points = [nodes[n] for n in refs]
    distance = min(segment_distance(a, b) for a, b in zip(points, points[1:]))
    row = dict(osm_way=way.attrib['id'], tags=tags, edge_distance_m=round(distance, 2))
    if 'building' in tags:
        row['candidate_inside_polygon'] = refs[0] == refs[-1] and contains_origin(points)
        buildings.append(row)
    elif 'highway' in tags:
        paths.append(row)
report = dict(candidate=candidate,
              method='Local tangent approximation; origin is historical map destination; not surveyed geometry.',
              nearest_buildings=sorted(buildings, key=lambda x: x['edge_distance_m'])[:8],
              nearest_highways=sorted(paths, key=lambda x: x['edge_distance_m'])[:8],
              verified_building_geometry=False, verified_access=False)
(OUT / 'dorm3-osm-crosscheck.json').write_text(json.dumps(report, ensure_ascii=False, indent=2), encoding='utf-8')
print(json.dumps(report, ensure_ascii=False, indent=2))
