"""Save a bounded snapshot of the live local validation run."""
import json
from pathlib import Path
from urllib.request import urlopen

root = Path(__file__).parent
with urlopen('http://127.0.0.1:8765/v1/demo/state', timeout=10) as response:
    state = json.load(response)
(root / 'cusp-service-progress.json').write_text(
    json.dumps(state, ensure_ascii=False, indent=2), encoding='utf-8')
vehicle = next(v for v in state['vehicles'] if v['id'] == 'V01')
print(json.dumps(dict(time=state['time_s'],
    requests=[(r['id'], r['status']) for r in state['requests']],
    vehicle={key: vehicle.get(key) for key in (
        'position', 'phase', 'physical_status', 'physical_reason',
        'measured_speed_kmh', 'transport_reason', 'request_id')},
    events=state['physical_transport_events']), ensure_ascii=True))
