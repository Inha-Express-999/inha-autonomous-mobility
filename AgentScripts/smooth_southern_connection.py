"""Remove the artificial south grading seam without moving nearby building pads."""
import json
from pathlib import Path
import numpy as np
import scipy.sparse as sp
from scipy.sparse.linalg import spsolve
from rasterio.features import rasterize
from affine import Affine

root = Path(__file__).resolve().parents[1]
out = root / 'Docs/MapResearch/Iterations/SportsRoadGrading/FineTerrain'
n = 1025
before = np.fromfile(out / 'road-grade-before.raw', '<f4').reshape(n, n)
height = before.astype(float) * 100 - 16
x, z = np.meshgrid(-900 + np.arange(n) * 2, -900 + np.arange(n) * 2)
# Scene bounds audit: nearest dormitory ends at x=84.91; leave its pad intact.
free = (x > 90) & (x < 280) & (z > -510) & (z < -390)
index = np.full((n, n), -1, int)
index[free] = np.arange(free.sum())
rows, cols, values = [], [], []
rhs = []
for j, i in np.argwhere(free):
    row = index[j, i]
    rows.append(row); cols.append(row); values.append(4.01)
    target = .01 * height[j, i]
    for dj, di in ((1, 0), (-1, 0), (0, 1), (0, -1)):
        other = index[j + dj, i + di]
        if other >= 0:
            rows.append(row); cols.append(other); values.append(-1)
        else:
            target += height[j + dj, i + di]
    rhs.append(target)
matrix = sp.coo_matrix((values, (rows, cols))).tocsc()
after = before.copy()
after[free] = ((spsolve(matrix, rhs) + 16) / 100).astype('<f4')
triangles = np.fromfile(out / 'all-roads-xz.raw', '<f4').reshape(-1, 3, 2)
road = rasterize((({'type': 'Polygon', 'coordinates': [[*t.tolist(), t[0].tolist()]]}, 1)
                  for t in triangles), out_shape=(n, n), transform=Affine(2, 0, -901, 0, 2, -901), all_touched=True).astype(bool)
area = road & (x >= 88) & (x <= 282) & (z >= -512) & (z <= -388)
def metric(h):
    gz, gx = np.gradient(h.astype(float) * 100, 2, 2)
    g = np.hypot(gx, gz)[area]
    return dict(samples=len(g), p95=float(np.percentile(g, 95)), maximum=float(g.max()), above10=int((g > .1).sum()))
report = dict(before=metric(before), after=metric(after), max_change_m=float(abs(after-before).max()*100),
              changed_samples=int(free.sum()), applied=False,
              method='Screened harmonic smoothing; fixed outer boundary and unchanged building footprints')
assert np.array_equal(before[~free], after[~free])
assert report['after']['maximum'] < report['before']['maximum']
assert report['after']['above10'] < report['before']['above10']
after.tofile(out / 'south-seam-proposal.raw')
(out / 'south-seam-report.json').write_text(json.dumps(report, indent=2))
print(json.dumps(report, indent=2))
