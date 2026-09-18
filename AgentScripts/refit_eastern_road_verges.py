"""Smooth live-exported eastern roads and verges, preserving fixed architecture.

Editor-only approximate terrain design; not a vehicle accessibility certificate.
Requires EastRoadRefit before/protected/roads exports from the current scene.
"""
from pathlib import Path
import json
import argparse
import numpy as np
from scipy.ndimage import binary_dilation
from scipy.sparse import coo_matrix
from scipy.sparse.linalg import lsqr
from rasterio.features import rasterize
from affine import Affine

parser = argparse.ArgumentParser(description=__doc__)
parser.add_argument('--input', type=Path,
                    default=Path('Docs/MapResearch/Iterations/EastRoadRefit'))
out = parser.parse_args().input
n = 1025
before = np.fromfile(out / 'before.raw', '<f4').reshape(n, n)
h = before.astype(float) * 100 - 16
x, z = np.meshgrid(-900 + np.arange(n)*2, -900 + np.arange(n)*2)

def mask(name):
    triangles = np.fromfile(out / (name + '.raw'), '<f4').reshape(-1, 3, 2)
    shapes = [({'type': 'Polygon', 'coordinates': [[*t.tolist(), t[0].tolist()]]}, 1)
              for t in triangles]
    return rasterize(shapes, out_shape=(n,n), transform=Affine(2,0,-901,0,2,-901),
                     all_touched=True).astype(bool)

# One extra cell protects the interpolation support of fixed surfaces.
protected = binary_dilation(mask('protected'), iterations=1)
road = binary_dilation(mask('roads'), iterations=1)
region = (x > 195) & (x < 295) & (z > -170) & (z < -25)
free = region & ~protected
index = np.full((n,n), -1, dtype=int)
index[free] = np.arange(free.sum())
rows, cols, data, rhs = [], [], [], []

def equation(terms, target, weight):
    row = len(rhs)
    for j,i,value in terms:
        if index[j,i] >= 0:
            rows.append(row); cols.append(index[j,i]); data.append(value*weight)
        else:
            target -= value*h[j,i]
    rhs.append(target*weight)

for j,i in np.argwhere(free):
    equation([(j,i,1)], h[j,i], .15)
    equation([(j,i,4),(j-1,i,-1),(j+1,i,-1),(j,i-1,-1),(j,i+1,-1)], 0, 2)
    for dj,di in [(1,0),(0,1),(-1,0),(0,-1)]:
        weight = 10 if road[j,i] and road[j+dj,i+di] else .3
        equation([(j,i,1),(j+dj,i+di,-1)], 0, weight)

a = coo_matrix((data,(rows,cols)), shape=(len(rhs), int(free.sum()))).tocsr()
result = lsqr(a, np.asarray(rhs), atol=1e-10, btol=1e-10, iter_lim=20000)
if result[1] not in (1,2):
    raise RuntimeError(f'Unconverged solver: {result[1]}')
after = before.copy()
after[free] = ((result[0]+16)/100).astype('<f4')
if not np.isfinite(after).all() or np.any((after < 0) | (after > 1)):
    raise ValueError('Invalid terrain heights')
after.tofile(out / 'proposal.raw')
report = dict(free_cells=int(free.sum()), changed_cells=int((after != before).sum()),
              max_change_m=float(abs(after-before).max()*100),
              fixed_cell_change_m=float(abs(after-before)[~free].max()*100),
              solver_iterations=int(result[2]), applied=False)
(out / 'proposal.json').write_text(json.dumps(report, indent=2))
print(json.dumps(report, indent=2))
