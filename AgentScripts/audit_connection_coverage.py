"""Check selected OSM corridors against exported ACTIVE Unity road triangles.

This tests horizontal visual coverage, not grade, legal access or routability.
Run after exporting active-road-triangles.csv from the working scene.
"""
import json
import math
import runpy
from pathlib import Path
import numpy as np

ROOT = Path(__file__).resolve().parents[1]
scope = runpy.run_path(str(ROOT / 'AgentScripts/scope_campus_connections.py'))
tri = np.loadtxt(ROOT / 'Docs/MapResearch/Iterations/active-road-triangles.csv',
                 delimiter=',', skiprows=1).reshape(-1, 3, 2)
low, high = tri.min(axis=1), tri.max(axis=1)

def covered(p):
    candidates = tri[np.all(low <= p + .001, axis=1) & np.all(high >= p - .001, axis=1)]
    if not len(candidates):
        return False
    edges = np.roll(candidates, -1, axis=1) - candidates
    delta = p - candidates
    cross = edges[:, :, 0] * delta[:, :, 1] - edges[:, :, 1] * delta[:, :, 0]
    area = np.abs(np.sum(candidates[:, :, 0] * np.roll(candidates[:, :, 1], -1, axis=1)
                         - candidates[:, :, 1] * np.roll(candidates[:, :, 0], -1, axis=1), axis=1))
    return bool(np.any(((cross >= -.001).all(axis=1) | (cross <= .001).all(axis=1)) & (area > 1e-8)))

records = []
for wid, edges in sorted(scope['selected'].items()):
    for u, v in sorted(edges):
        a, b = np.array(scope['nodes'][u]), np.array(scope['nodes'][v])
        length = float(np.linalg.norm(b-a))
        steps = max(1, math.ceil(length))
        missing = []
        for i in range(steps):
            p = a+(b-a)*((i+.5)/steps)
            if not covered(p):
                missing.append([float(x) for x in p])
        if missing:
            records.append(dict(osm_way=wid, nodes=[u,v], length_m=length,
                                uncovered_length_estimate_m=length*len(missing)/steps,
                                missing_samples_xz=missing))
report = dict(status='horizontal_visual_coverage_only', sample_spacing_max_m=1,
              active_mesh_triangles=len(tri), uncovered_edges=records,
              uncovered_length_estimate_m=sum(r['uncovered_length_estimate_m'] for r in records),
              caveats=['Legacy OSM coordinates; working map must be north aligned at origin.',
                       'Does not validate building entrance gaps, surface heights or access.'])
out = ROOT/'Docs/MapResearch/Iterations/connection-coverage.json'
out.write_text(json.dumps(report, ensure_ascii=False, indent=2), encoding='utf-8')
print(json.dumps({k:v for k,v in report.items() if k!='uncovered_edges'}, indent=2))
print('Uncovered ways:', sorted(set(r['osm_way'] for r in records)))
