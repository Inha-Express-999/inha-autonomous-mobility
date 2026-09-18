"""Create a reversible, synthetic smooth road-corridor terrain proposal.

Consumes the Unity export in Iterations/RoadGrading. Does not approve vehicle
routes or replace measured elevation. Building/water/sports footprints are fixed.
"""
import json
import argparse
from pathlib import Path
import numpy as np
from rasterio.features import rasterize
from affine import Affine

ROOT = Path(__file__).resolve().parents[1]
OUT = ROOT / 'Docs/MapResearch/Iterations/RoadGrading'
parser = argparse.ArgumentParser()
parser.add_argument('--precise-footprints', action='store_true')
precise = parser.parse_args().precise_footprints
suffix = '-refinement' if precise else ''
grid = json.loads((OUT / 'grid.json').read_text())
n = grid['resolution']
ox, oy, oz = grid['origin']
sx, sy, sz = grid['size']
dx, dz = sx / (n - 1), sz / (n - 1)
before = np.fromfile(OUT / f'before{suffix}.raw', '<f4').reshape(n, n).astype(float)
triangles = np.fromfile(OUT / 'roads-xz.raw', '<f4').reshape(-1, 3, 2)
shapes = []
for tri in triangles:
    a, b = tri[1] - tri[0], tri[2] - tri[0]
    if abs(a[0] * b[1] - a[1] * b[0]) > .0001:
        shapes.append(({'type': 'Polygon', 'coordinates': [[*tri.tolist(), tri[0].tolist()]]}, 1))
road = rasterize(shapes, out_shape=(n, n), transform=Affine(dx, 0, ox-dx/2, 0, dz, oz-dz/2), all_touched=True).astype(bool)
x, z = np.meshgrid(ox + np.arange(n)*dx, oz + np.arange(n)*dz)
protected = np.zeros((n, n), bool)
for xmin, zmin, xmax, zmax in np.fromfile(OUT / 'protected-bounds.raw', '<f4').reshape(-1, 4):
    protected |= (x >= xmin-dx/2) & (x <= xmax+dx/2) & (z >= zmin-dz/2) & (z <= zmax+dz/2)
if precise:
    triangles = np.fromfile(OUT / 'protected-triangles.raw', '<f4').reshape(-1, 3, 2)
    protected = rasterize(
        (({'type':'Polygon', 'coordinates':[[*t.tolist(),t[0].tolist()]]},1) for t in triangles),
        out_shape=(n,n), transform=Affine(dx,0,ox-dx/2,0,dz,oz-dz/2), all_touched=True).astype(bool)
    # Keep all four bilinear support samples of each rigid object's anchor fixed.
    for ax, az in np.fromfile(OUT / 'anchors.raw', '<f4').reshape(-1,2):
        ix, iz = int(np.floor((ax-ox)/dx)), int(np.floor((az-oz)/dz))
        protected[max(0,iz):min(n,iz+2), max(0,ix):min(n,ix+2)] = True

def dilate(mask, radius):
    cells = int(np.ceil(radius/min(dx, dz)))
    padded = np.pad(mask, cells)
    out = np.zeros_like(mask)
    for j in range(-cells, cells+1):
        for i in range(-cells, cells+1):
            if (i*dx)**2 + (j*dz)**2 <= radius**2:
                out |= padded[cells+j:cells+j+n, cells+i:cells+i+n]
    return out

kernel = np.exp(-.5*(np.arange(-8, 9)/2.5)**2)
kernel /= kernel.sum()
def blur(values):
    for axis in (0, 1):
        values = np.apply_along_axis(lambda a: np.convolve(np.pad(a, 8, mode='edge'), kernel, 'valid'), axis, values)
    return values

# Ignore lake basins and building platforms when estimating road elevation.
weights = (~protected).astype(float)
smooth = blur(before*weights) / np.maximum(blur(weights), 1e-8)
blend = np.zeros_like(before)
for radius, weight in ((32, .15), (24, .4), (16, .75), (8, 1.0)):
    blend[dilate(road, radius)] = weight
blend[dilate(protected, 8)] *= .35
blend[protected] = 0
after = before + (smooth-before)*blend
after = after.astype('<f4')
assert np.isfinite(after).all() and after.min() >= 0 and after.max() <= 1
assert np.array_equal(after[protected], before.astype('<f4')[protected])
after.tofile(OUT / f'graded{suffix}.raw')
road.astype('u1').tofile(OUT / 'road-mask.raw')
core = road & ~dilate(protected, 8)
def metrics(h):
    metres = h*sy
    gy, gx = np.gradient(metres, dz, dx)
    lap = (np.roll(metres,1,0)+np.roll(metres,-1,0)+np.roll(metres,1,1)+np.roll(metres,-1,1)-4*metres)
    return {'road_grade_p95':float(np.percentile(np.hypot(gx,gy)[core],95)), 'road_grid_roughness_rms_m':float(np.sqrt(np.mean(lap[core]**2)))}
report = {'description':'Synthetic road corridor smoothing, 20m Gaussian scale; fixed building/water/sports bounds. Grid metrics exclude protected-border road cells.', 'road_cells':int(road.sum()), 'measured_core_cells':int(core.sum()), 'changed_cells':int((abs(after-before)*sy>.001).sum()), 'max_height_change_m':float(abs(after-before).max()*sy), 'protected_height_change_m':float(abs(after-before)[protected].max()*sy), 'before':metrics(before), 'after':metrics(after)}
if precise:
    report['description'] = 'Second road grading pass; actual projected structural triangles and rigid anchor support cells protected.'
(OUT/f'report{suffix}.json').write_text(json.dumps(report,indent=2)+'\n')
print(json.dumps(report,indent=2))
