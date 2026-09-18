"""Prepare a finer authoring height grid without claiming new elevation accuracy.

Input is the preserved current Unity height array. Outputs are candidates only;
the Editor must validate the baseline and explicitly apply/rebake them.
"""
import json
from pathlib import Path

import numpy as np
from affine import Affine
from rasterio.features import rasterize
from scipy.ndimage import binary_dilation, map_coordinates

ROOT = Path(__file__).resolve().parents[1]
SOURCE = ROOT / 'Docs/MapResearch/Iterations/SportsRoadGrading'
OUT = SOURCE / 'FineTerrain'
OUT.mkdir(exist_ok=True)
before = np.fromfile(SOURCE / 'refinement-before.raw', '<f4').reshape(257, 257)
items = [line.split('|', 1) for line in
         (SOURCE / 'Footprints/manifest.txt').read_text(encoding='utf-8-sig').splitlines()]
reports = []
for resolution in (257, 513, 1025):
    spacing = 2048 / (resolution - 1)
    masks = []
    for filename, name in items:
        triangles = np.fromfile(SOURCE / 'Footprints' / filename, '<f4').reshape(-1, 3, 2)
        mask = rasterize(
            (({'type': 'Polygon', 'coordinates': [[*t.tolist(), t[0].tolist()]]}, 1)
             for t in triangles), out_shape=(resolution, resolution),
            transform=Affine(spacing, 0, -900-spacing/2, 0, spacing, -900-spacing/2),
            all_touched=True).astype(bool)
        masks.append(binary_dilation(mask, structure=np.ones((3, 3))))
    overlaps = [dict(first=items[i][1], second=items[j][1],
                     cells=int((masks[i] & masks[j]).sum()))
                for i in range(len(masks)) for j in range(i)
                if (masks[i] & masks[j]).any()]
    reports.append(dict(resolution=resolution, spacing_m=spacing, overlaps=overlaps))
    if resolution == 1025:
        axis = np.linspace(0, 256, resolution)
        zz, xx = np.meshgrid(axis, axis, indexing='ij')
        fine = map_coordinates(before, [zz, xx], order=1, mode='nearest').astype('<f4')
        assert np.array_equal(fine[::4, ::4], before)
        fine.tofile(OUT / 'baseline-1025.raw')
        np.savez_compressed(OUT / 'building-supports-1025.npz',
                            **{f'building_{i}': m for i, m in enumerate(masks)})
report = dict(status='candidate_not_applied', source='refinement-before.raw',
              interpolation='bilinear; original 257x257 samples preserved exactly',
              note='More authoring resolution only; no improvement in measured DSM accuracy.',
              comparisons=reports)
(OUT / 'resolution-review.json').write_text(json.dumps(report, ensure_ascii=False, indent=2), encoding='utf-8')
print(json.dumps({r['resolution']: len(r['overlaps']) for r in reports}))
