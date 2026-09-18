"""Approximate Dormitory 3 placement from the official dormitory map.

Map pixels were read from the displayed 948x648 official image (not extracted
building-survey data). Blue Dormitory 2 corners register the green Dormitory 3
outline against the existing cached OSM footprint. Entrances remain synthetic.
"""
from pathlib import Path
import json
import numpy as np

root=Path(__file__).resolve().parents[1]
dorms=json.loads((root/'Assets/CampusSim/Data/dormitory_drafts.json').read_text())
dorm2=next(b for b in dorms['buildings'] if b['id']=='dorm_2')
# Displayed map had an offset of (166,36) from the screenshot origin.
pixels=np.array([[1044,432],[1053,439],[1026,545],[1004,536]],float)-[166,36]
indices=[11,10,5,4]
world=np.array([[dorm2['points'][i]['x'],dorm2['points'][i]['z']] for i in indices])
matrix=np.c_[pixels,np.ones(4)]
affine=np.linalg.lstsq(matrix,world,rcond=None)[0]
guest_pixels=np.array([[1048,532],[1067,540],[1060,562],[1027,549]],float)-[166,36]
points=np.c_[guest_pixels,np.ones(4)]@affine
area=np.sum(points[:,0]*np.roll(points[:,1],-1)-np.roll(points[:,0],-1)*points[:,1])/2
if area<0:points=points[::-1]
envelope=points.copy()
# The thick green map annotation encloses a site, not a surveyed wall line.
# Use a conservative massing inside it, then run the scene overlap preflight.
points=points.mean(axis=0)+(points-points.mean(axis=0))*.7
# A 3m westward draft adjustment stays inside the annotation envelope and
# separates the massing from the mapped east-side driveway. Not surveyed.
points+=np.array([-3.,0.])
report=dict(id='dorm_3',name='제3생활관 게스트하우스',floors=10,
    source='https://dormeng.inha.ac.kr/sites/dormeng/images/map_img02.jpg',
    context_source='https://sites.google.com/inha.ac.kr/meew2024/veune',
    floor_source='https://inha.uway.com/file/2026_inha_js.pdf',
    status='official_map_relative_location_approximate_outline_synthetic_entrances',
    control_point_rms_m=float(np.sqrt(np.mean(np.sum((matrix@affine-world)**2,axis=1)))),
    coordinate_frame='existing legacy campus local XZ; apply INHA UNIVERSITY transform',
    approximate=True,verified_geometry=False,verified_entrances=False,verified_access=False,
    points=[dict(x=float(p[0]),z=float(p[1])) for p in points],
    map_annotation_envelope=[dict(x=float(p[0]),z=float(p[1])) for p in envelope],
    synthetic_envelope_scale=.7,
    synthetic_placement_adjustment_m=dict(x=-3.,z=0.),
    height_per_floor_m=3.2,
    note='Map annotations approximate the building extent; registration error is not geographic accuracy. No surveyed entrance or accessible Stop implied.')
target=root/'Assets/CampusSim/Data/dormitory_three_draft.json'
target.write_text(json.dumps(report,ensure_ascii=False,indent=2)+'\n',encoding='utf-8')
print(json.dumps(report,ensure_ascii=False,indent=2))
