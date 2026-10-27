import json
from pathlib import Path
from campus_sim.unity_drive import DriveCommand,EgoObservation
for model,name in [(DriveCommand,'command'),(EgoObservation,'observation')]:
    Path(f'contracts/unity_drive_{name}.schema.json').write_text(
        json.dumps(model.model_json_schema(),ensure_ascii=False,indent=2)+'\n',encoding='utf-8')
