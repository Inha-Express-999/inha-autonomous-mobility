import csv
import sys
import tempfile
import unittest
from pathlib import Path
sys.path.insert(0,str(Path(__file__).resolve().parents[2]/'AgentScripts'))
from analyze_drive_telemetry import summarize

class CameraMeasurementTests(unittest.TestCase):
    def test_overview_transition_does_not_count_as_follow_vibration(self):
        rows=[]
        for i in range(60):
            follow=i<20 or i>=40
            rows.append(dict(elapsed_s=i*.1,x=i,z=0,y=0,pitch_deg=0,roll_deg=0,
                camera_y=10 if follow else 900,camera_pitch_deg=0,camera_roll_deg=0,
                camera_live_mode=1 if follow else 2,camera_follow_requested=int(follow),camera_blending=0,
                speed_kmh=35,grounded_wheels=4,lane_error_m=0,vertical_mps=0,status='DRIVING'))
        with tempfile.TemporaryDirectory() as folder:
            path=Path(folder)/'trace.csv'
            with path.open('w',newline='') as stream:
                writer=csv.DictWriter(stream,fieldnames=rows[0]);writer.writeheader();writer.writerows(rows)
            result=summarize(path)['speed_bands'][0]
        self.assertLess(result['camera_follow_samples'],40)
        self.assertGreater(result['camera_follow_samples'],20)
        self.assertEqual(result['one_second_detrended_rms']['camera_y'],0)

if __name__=='__main__': unittest.main()
