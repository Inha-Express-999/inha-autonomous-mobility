import json,time
from pathlib import Path
from campus_sim.web_demo import DemoSession
from campus_sim.unity_drive import EgoObservation
from campus_sim.domain import CreateRequest
s=DemoSession();p=s.physical
p.clock=lambda:0.
for v in ('V01','V02','V03'):
    d=json.loads((Path(__file__).parent/('garage-ego-'+v+'.json')).read_text(encoding='utf-8'))
    d.update(run_id=s.run_id,tick=1)
    p.observation(v,p.connect(v),EgoObservation(**d))
order=CreateRequest(command_id='diag',owner_id='diagnostic',service_type='PASSENGER',
    pickup_landmark_id='anniversary_60',dropoff_landmark_id='biryong_plaza')
r=s.service.create_request(order,external_execution=True).request
for v in ('V01','V02','V03'):
    start=time.perf_counter()
    try: print(v,s.physical_dispatch._estimate(v,r))
    except ValueError as e: print(v,str(e))
    print('seconds',time.perf_counter()-start)
s.service.dispatch_algorithm='greedy';s.physical_dispatch.step()
print('result',r.status,r.vehicle_id,s.service.waiting_reasons)
