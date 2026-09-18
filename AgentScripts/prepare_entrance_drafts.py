"""Generate explicitly synthetic entrance drafts from existing campus footprints."""
import json
import math
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
source = json.loads((ROOT / 'Assets/InhaCampus/Source/campus.json').read_text(encoding='utf-8'))
targets = [
    ('building_5', '5호관', 'r3149246_218188830_0'),
    ('building_2', '2호관', 'r14633181_218264949_0'),
    ('hitech', '하이테크', '218081857_0'),
    ('anniversary_60', '60주년기념관', '218189298_0'),
]
drafts = []
for landmark_id, name, feature_id in targets:
    feature = next(f for f in source['features'] if f['id'] == feature_id)
    points = [(p['x'], p['z']) for p in feature['points']]
    edges = list(zip(points, points[1:] + points[:1]))
    area = sum(a[0]*b[1]-b[0]*a[1] for a,b in edges)
    a,b = max(edges, key=lambda edge: math.dist(*edge))
    if landmark_id == 'anniversary_60':
        # Opposite facade selected after scene review: the longest face is obscured.
        a,b = edges[2]
    length = math.dist(a,b)
    dx,dz = (b[0]-a[0])/length,(b[1]-a[1])/length
    nx,nz = (dz,-dx) if area > 0 else (-dz,dx)
    entries=[]
    facade_offset = .9 if landmark_id == 'anniversary_60' else .25
    for role,t in [('general', .3), ('accessible', .7)]:
        entries.append(dict(id=f'{landmark_id}_{role}', role=role,
            x=a[0]+(b[0]-a[0])*t+nx*facade_offset,
            z=a[1]+(b[1]-a[1])*t+nz*facade_offset,
            heading=math.degrees(math.atan2(nx,nz))))
    assert math.dist((entries[0]['x'],entries[0]['z']), (entries[1]['x'],entries[1]['z'])) > 8
    drafts.append(dict(id=landmark_id,name=name,featureId=feature_id,
        status='synthetic_facade_entrances_not_verified_access',entrances=entries))
output = dict(coordinates='legacy campus local x=east z=north; apply map transform',
    excludedAmbiguousCandidates=[
        dict(osmType='way', osmId='218017288', name='5호관', reason='Different from existing Inha building footprint; about 449m centroid discrepancy; campus identity unresolved'),
        dict(osmType='way', osmId='218036946', name='2호관', reason='Different from existing Inha building footprint; about 474m centroid discrepancy; campus identity unresolved'),
    ],
    landmarks=drafts,
    additionalSceneLandmarks=['main_gate','rear_gate'],
    gatePassageFile='gate_passage_drafts.json',
    pending=['인하대역','비룡플라자','제1생활관','제2생활관','제3생활관'])
destination = ROOT / 'Assets/CampusSim/Data/entrance_drafts.json'
destination.parent.mkdir(parents=True,exist_ok=True)
destination.write_text(json.dumps(output,ensure_ascii=False,indent=2)+'\n',encoding='utf-8')
print(f'{len(drafts)} building drafts plus 2 gate scene landmarks; 5 pending')
