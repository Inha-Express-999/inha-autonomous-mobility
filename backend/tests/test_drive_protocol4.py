import json
from pathlib import Path

import pytest
from jsonschema import Draft202012Validator
from pydantic import ValidationError

from campus_sim.unity_drive import DriveCommand, EgoObservation


def command(**changes):
    value=dict(schema_version=4, run_id='run', tick=1, target_speed_kmh=-3,
        steering_angle_rad=.3, emergency_stop=False, valid_for_s=.3,
        status='MANEUVER', reason='three-point turn')
    value.update(changes)
    return value


@pytest.mark.parametrize('version',[1,2,3,4])
def test_reverse_is_opt_in_to_protocol4(version):
    data=command(schema_version=version)
    validator=Draft202012Validator(DriveCommand.model_json_schema())
    assert validator.is_valid(data) == (version == 4)
    if version == 4:
        assert DriveCommand(**data).target_speed_kmh == -3
    else:
        with pytest.raises(ValidationError): DriveCommand(**data)


@pytest.mark.parametrize('changes',[dict(target_speed_kmh=-5.01),dict(status='DRIVING')])
def test_reverse_speed_and_phase_are_bounded(changes):
    data=command(**changes)
    assert not Draft202012Validator(DriveCommand.model_json_schema()).is_valid(data)
    with pytest.raises(ValidationError): DriveCommand(**data)


def test_protocol4_requires_full_measured_footprint():
    data=dict(schema_version=4,run_id='run',geometry_hash='0'*64,tick=1,
        x=0,z=0,heading_rad=0,speed_mps=0,wheelbase_m=1.3,max_steering_rad=.52,
        sensor_valid=True,sensor_age_s=0,obstacle_detection_enabled=False,rays=[],half_width_m=.46)
    for field in ('rear_axle_offset_m','half_length_m'):
        incomplete={**data,'rear_axle_offset_m':-.65,'half_length_m':1.55}
        incomplete.pop(field)
        with pytest.raises(ValidationError): EgoObservation(**incomplete)
        assert not Draft202012Validator(EgoObservation.model_json_schema()).is_valid(incomplete)
    complete={**data,'rear_axle_offset_m':-.65,'half_length_m':1.55}
    assert EgoObservation(**complete).half_length_m == 1.55
    Draft202012Validator(EgoObservation.model_json_schema()).validate(complete)


@pytest.mark.parametrize('model,name',[(DriveCommand,'command'),(EgoObservation,'observation')])
def test_checked_in_protocol_schema_matches_runtime(model,name):
    root=Path(__file__).resolve().parents[2]
    assert json.loads((root/f'contracts/unity_drive_{name}.schema.json').read_text()) == model.model_json_schema()
