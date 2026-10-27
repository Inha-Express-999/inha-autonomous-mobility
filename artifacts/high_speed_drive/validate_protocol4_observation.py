import json
from pathlib import Path
from campus_sim.unity_drive import EgoObservation
from jsonschema import Draft202012Validator

data=json.loads(Path('artifacts/high_speed_drive/protocol4-observation.json').read_text(encoding='utf-8-sig'))
value=EgoObservation(**data)
Draft202012Validator(EgoObservation.model_json_schema()).validate(data)
print(json.dumps(dict(schema_version=value.schema_version,half_width_m=value.half_width_m,
    half_length_m=value.half_length_m,rear_axle_offset_m=value.rear_axle_offset_m,
    sensor_valid=value.sensor_valid,obstacle_detection_enabled=value.obstacle_detection_enabled)))
