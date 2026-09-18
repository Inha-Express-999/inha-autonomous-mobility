"""Summarize exported road faces by horizontal area, not triangle count.

Adaptive subdivision creates many tiny triangles at abrupt terrain changes;
an unweighted percentile would therefore misrepresent the drivable surface.
This is a visual geometry audit, not a legal/accessibility certification.
"""
import csv
import json
from pathlib import Path

import numpy as np

root = Path(__file__).resolve().parents[1]
folder = root / 'Docs/MapResearch/Iterations'
rows = list(csv.DictReader((folder / 'all-active-road-triangles.csv').open(encoding='utf-8-sig')))
area = np.array([float(row['areaM2']) for row in rows])
grade = np.array([float(row['grade']) for row in rows])
if not len(rows) or not np.isfinite(area).all() or not np.isfinite(grade).all():
    raise ValueError('Missing or nonfinite road geometry')
order = np.argsort(grade)
percentile = np.searchsorted(np.cumsum(area[order]), .95 * area.sum())
report = dict(
    triangles=len(rows), horizontal_area_m2=float(area.sum()),
    area_weighted_p95_grade=float(grade[order][percentile]),
    area_over_10percent_m2=float(area[grade > .1].sum()),
    max_grade=float(grade.max()),
    note='Surface gradient magnitude; longitudinal and cross grades are not separated.',
)
(folder / 'all-active-road-summary.json').write_text(json.dumps(report, indent=2))
print(json.dumps(report, indent=2))
