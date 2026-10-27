import csv
import json
from pathlib import Path

root=Path('artifacts/high_speed_drive')
rows=list(csv.DictReader((root/'hitech-turn-roundtrip.csv').open(encoding='utf-8-sig',newline='')))
turn=[r for r in rows if r['status']=='저속 회차 중']
directions=[]; transitions=[]
for i,row in enumerate(turn):
    target=float(row['target_kmh'])
    if not target: continue
    direction=1 if target>0 else -1
    if not directions or direction != directions[-1]:
        if directions:
            preceding=[]
            j=i-1
            while j>=0 and float(turn[j]['target_kmh'])==0:
                preceding.append(turn[j]); j-=1
            transitions.append(dict(to_direction=direction,
                preceding_brake_samples=len(preceding),
                last_brake_speed_kmh=float(preceding[0]['speed_kmh']) if preceding else None))
        directions.append(direction)
result=dict(source='hitech-turn-roundtrip.csv',samples=len(turn),
    elapsed_s=float(turn[-1]['elapsed_s'])-float(turn[0]['elapsed_s']),
    commanded_directions=directions,transitions=transitions,
    peak_measured_speed_kmh=max(float(r['speed_kmh']) for r in turn),
    all_wheels_airborne_samples=sum(float(r['grounded_wheels'])==0 for r in turn),
    limitation='CSV sampling is not every control tick; speed is body magnitude, direction is commanded sign.')
(root/'hitech-turn-segment-summary.json').write_text(json.dumps(result,indent=2)+'\n',encoding='utf-8')
print(json.dumps(result))
