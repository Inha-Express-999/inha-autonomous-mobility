import argparse,json,urllib.request
from pathlib import Path

parser=argparse.ArgumentParser()
parser.add_argument('action',choices=['submit','status'])
args=parser.parse_args()
base='http://127.0.0.1:8765'
if args.action=='submit':
    payload=dict(command_id='observed-library-hitech-20261025',owner_id='physical-verification',
        service_type='PASSENGER',pickup_landmark_id='library',dropoff_landmark_id='hitech')
    req=urllib.request.Request(base+'/v1/demo/requests',data=json.dumps(payload).encode(),
                              headers={'Content-Type':'application/json'})
else:
    req=urllib.request.Request(base+'/v1/demo/state')
with urllib.request.urlopen(req,timeout=30) as response:
    data=json.load(response)
root=Path('artifacts/high_speed_drive')
(root/f'passenger-{args.action}.json').write_text(json.dumps(data,ensure_ascii=False,indent=2)+'\n',encoding='utf-8')
if args.action=='status':
    data=dict(requests=[r for r in data['requests'] if r['owner_id']=='physical-verification'],
        vehicles=[{k:v.get(k) for k in ('id','phase','physical_status','physical_reason','transport_reason','measured_speed_kmh','remaining_wh','request_id')}
                  for v in data['vehicles'] if v['id']=='V01'],
        events=data.get('physical_transport_events',[])[-8:],waiting_reasons=data['waiting_reasons'])
print(json.dumps(data,ensure_ascii=False))
