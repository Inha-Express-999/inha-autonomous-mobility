import csv,json
from pathlib import Path
rows=list(csv.DictReader(Path('artifacts/high_speed_drive/hitech-turn-roundtrip.csv').open(encoding='utf-8-sig',newline='')))
changes=[]
for a,b in zip(rows,rows[1:]):
    delta=abs(float(b['camera_y'])-float(a['camera_y']))
    changes.append(dict(elapsed_s=float(b['elapsed_s']),camera_y=float(b['camera_y']),
                        delta_y=delta,speed_kmh=float(b['speed_kmh'])))
print(json.dumps(sorted(changes,key=lambda r:r['delta_y'],reverse=True)[:8],indent=2))
