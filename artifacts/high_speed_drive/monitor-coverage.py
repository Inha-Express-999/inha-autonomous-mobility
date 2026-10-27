import json,time,urllib.request
from pathlib import Path
for i in range(10):
 s=json.load(urllib.request.urlopen('http://127.0.0.1:8765/v1/demo/state',timeout=5))
 with Path('artifacts/high_speed_drive/coverage-retry-observations.jsonl').open('a',encoding='utf-8') as f:f.write(json.dumps(dict(wall=time.time(),state=s),ensure_ascii=False)+'\n')
 print([(v['id'],v.get('physical_status'),round(v.get('measured_speed_kmh',0),1),v['position']) for v in s['vehicles']],flush=True)
 time.sleep(5)
