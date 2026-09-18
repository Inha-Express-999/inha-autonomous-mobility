"""Inspect cached OSM connectivity without treating roads as approved footpaths."""
import hashlib
import json
from collections import defaultdict, deque
from pathlib import Path
import xml.etree.ElementTree as ET

SOURCE = Path('Docs/MapResearch/2026-09-17/expanded_campus.osm')
OUTPUT = Path('Docs/MapResearch/Iterations/dorm1-pedestrian-topology.json')
root = ET.parse(SOURCE).getroot()
nodes = {n.attrib['id']: n for n in root.findall('node')}
ways = {}
memberships = defaultdict(list)
for way in root.findall('way'):
    tags = {t.attrib['k']: t.attrib['v'] for t in way.findall('tag')}
    if 'highway' not in tags:
        continue
    refs = [n.attrib['ref'] for n in way.findall('nd')]
    wid = way.attrib['id']
    ways[wid] = {'tags': tags, 'nodes': refs}
    for ref in refs:
        memberships[ref].append(wid)

foot_classes = {'footway', 'pedestrian', 'path', 'steps'}
foot_ways = {wid for wid, w in ways.items()
             if w['tags']['highway'] in foot_classes
             and w['tags'].get('foot') != 'no'
             and w['tags'].get('access') not in {'no', 'private'}}
adjacency = defaultdict(set)
for wid in foot_ways:
    refs = ways[wid]['nodes']
    for a, b in zip(refs, refs[1:]):
        adjacency[a].add(b)
        adjacency[b].add(a)

def component(seeds):
    visited = set(seeds)
    queue = deque(seeds)
    while queue:
        for other in adjacency[queue.popleft()]:
            if other not in visited:
                visited.add(other)
                queue.append(other)
    return visited

driveway = '1098489687'
candidate_ids = ['1098546960', '1098546961', '1098546962']
drive_nodes = set(ways[driveway]['nodes'])
reports = []
for wid in candidate_ids:
    reached = component(ways[wid]['nodes'])
    crossings = []
    for ref in sorted(reached):
        node = nodes.get(ref)
        tags = {} if node is None else {t.attrib['k']: t.attrib['v'] for t in node.findall('tag')}
        road_ids = [other for other in memberships[ref] if other not in foot_ways]
        if road_ids or tags.get('highway') == 'crossing':
            crossings.append({'node': ref, 'tags': tags, 'other_highway_ways': road_ids})
    reports.append({'candidate_way': wid, 'tags': ways[wid]['tags'],
                    'reachable_foot_way_ids': sorted({w for n in reached for w in memberships[n] if w in foot_ways}),
                    'shared_nodes_with_dorm_driveway': sorted(reached & drive_nodes),
                    'road_contacts_and_crossings': crossings})

result = {
    'source': str(SOURCE), 'source_sha256': hashlib.sha256(SOURCE.read_bytes()).hexdigest(),
    'driveway': {'way': driveway, **ways[driveway]}, 'candidates': reports,
    'interpretation': 'Shared OSM nodes only. Roads are not silently traversed. Steps remain in this inventory and are not accessible routes. Missing links in this cache do not prove that a real-world footpath is absent. No width, grade, crossing safety or accessible Stop approval is inferred.'
}
OUTPUT.write_text(json.dumps(result, ensure_ascii=False, indent=2), encoding='utf-8')
for r in reports:
    print(r['candidate_way'], 'foot ways:', len(r['reachable_foot_way_ids']),
          'driveway contacts:', r['shared_nodes_with_dorm_driveway'],
          'road contacts:', len(r['road_contacts_and_crossings']))
