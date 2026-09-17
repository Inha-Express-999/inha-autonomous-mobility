"""Prepare approximate campus relief; never edits Unity scenes or validates access."""
from pathlib import Path
import hashlib
import json
import xml.etree.ElementTree as ET

import numpy as np
import rasterio
from rasterio.transform import from_origin
from rasterio.warp import reproject, Resampling
from pyproj import CRS, Transformer
from PIL import Image, ImageDraw

ROOT = Path(__file__).resolve().parents[1]
SOURCE = ROOT / "Docs/MapResearch/2026-09-17"
OUT = ROOT / "maps/inha_relief_research"
OUT.mkdir(parents=True, exist_ok=True)
CRS_LOCAL = CRS.from_proj4("+proj=aeqd +lat_0=37.4506 +lon_0=126.6535 +datum=WGS84 +units=m +no_defs")
SIZE, N, XMIN, YMIN = 2048.0, 257, -900.0, -900.0
SPACING = SIZE / (N - 1)
# Pixel centers coincide with Terrain vertices, including both boundary endpoints.
transform = from_origin(XMIN - SPACING / 2, YMIN + SIZE + SPACING / 2, SPACING, SPACING)
raw = np.full((N, N), np.nan, dtype=np.float32)
with rasterio.open(SOURCE / "copernicus_n37_e126.tif") as src:
    source_info = dict(crs=str(src.crs), resolution=list(src.res), nodata=src.nodata,
                       width=src.width, height=src.height)
    reproject(rasterio.band(src, 1), raw, src_transform=src.transform, src_crs=src.crs,
              src_nodata=src.nodata, dst_transform=transform, dst_crs=CRS_LOCAL,
              dst_nodata=np.nan, resampling=Resampling.bilinear)
assert np.isfinite(raw).all(), "Missing elevation coverage; do not replace with zero"

# Artistic low-envelope approximation: not a surveyed bare-earth DTM.
# Work at 32m intervals, suppress local high DSM values, then smooth and upsample.
coarse = raw[::4, ::4]
windows = np.lib.stride_tricks.sliding_window_view(np.pad(coarse, 1, mode="edge"), (3, 3))
low = np.percentile(windows, 20, axis=(-1, -2)).astype(np.float32)
for _ in range(2):
    low = np.lib.stride_tricks.sliding_window_view(np.pad(low, 1, mode="edge"), (3, 3)).mean(axis=(-1, -2))
grid = np.arange(N)
knots = np.arange(0, N, 4)
horizontal = np.array([np.interp(grid, knots, row) for row in low])
relief = np.array([np.interp(grid, knots, horizontal[:, col]) for col in range(N)]).T.astype(np.float32)
origin_row = int(round((YMIN + SIZE) / SPACING))
origin_col = int(round(-XMIN / SPACING))
reference = float(relief[origin_row, origin_col])
relative = relief - reference
base = float(np.floor(relative.min()) - 1)
height_range = float(np.ceil(relative.max() - base) + 1)
normalized = (relative - base) / height_range
# Unity heights[z,x]: south row first; little-endian 16-bit RAW.
encoded = np.rint(np.flipud(normalized) * 65535).astype("<u2")
(OUT / "terrain_south_first_u16.raw").write_bytes(encoded.tobytes())
recovered = np.flipud(encoded.astype(float) / 65535 * height_range + base)
quantization_error = float(np.max(np.abs(recovered - relative)))
assert quantization_error <= height_range / 65535, "RAW roundtrip failed"
for name, data in [("source_dsm_projected.tif", raw), ("approximate_relief.tif", relief)]:
    with rasterio.open(OUT / name, "w", driver="GTiff", width=N, height=N, count=1,
                       dtype="float32", crs=CRS_LOCAL, transform=transform, compress="deflate") as dst:
        dst.write(data, 1)

to_local = Transformer.from_crs(4326, CRS_LOCAL, always_xy=True)
to_wgs = Transformer.from_crs(CRS_LOCAL, 4326, always_xy=True)
probe = np.array([[126.65, 37.45], [126.66, 37.45], [126.65, 37.455]])
xx, yy = to_local.transform(probe[:, 0], probe[:, 1])
lon, lat = to_wgs.transform(xx, yy)
assert np.max(np.abs(np.array([lon, lat]).T - probe)) < 1e-8

root = ET.parse(SOURCE / "expanded_campus.osm").getroot()
nodes = {n.get("id"): [float(n.get("lon")), float(n.get("lat"))] for n in root.findall("node")}
features, candidates = [], []
for e in root:
    tags = {t.get("k"): t.get("v") for t in e.findall("tag")}
    if e.tag == "node":
        geom = {"type": "Point", "coordinates": nodes[e.get("id")]}
    elif e.tag == "way":
        refs = [n.get("ref") for n in e.findall("nd")]
        if not refs or not all(n in nodes for n in refs):
            continue
        coords = [nodes[n] for n in refs]
        closed = len(coords) >= 4 and refs[0] == refs[-1]
        geom = {"type": "Polygon" if closed else "LineString", "coordinates": [coords] if closed else coords}
    else:
        continue
    if any(k in tags for k in ["building", "highway", "entrance", "railway", "natural", "amenity"]):
        features.append({"type": "Feature", "geometry": geom, "properties": {
            "osm_type": e.tag, "osm_id": e.get("id"), "tags": tags, "verified_access": False}})
    label = tags.get("name", "") + tags.get("ref", "")
    if any(s in label for s in ["생활관", "웅비재", "비룡재", "비룡플라자", "인하대학교 정문", "인하대학교 후문", "하이테크센터", "60주년기념관", "5호관", "2호관"]) or tags.get("name") == "인하대":
        candidates.append({"osm_type": e.tag, "osm_id": e.get("id"), "tags": tags,
                           "geometry": geom, "status": "osm_candidate_not_verified_stop"})
(OUT / "osm_features.geojson").write_text(json.dumps({"type": "FeatureCollection", "features": features}, ensure_ascii=False), encoding="utf-8")
(OUT / "landmark_candidates.json").write_text(json.dumps(candidates, ensure_ascii=False, indent=2), encoding="utf-8")

# Side-by-side overview uses one common elevation scale to expose DSM bias.
minimum, maximum = float(min(raw.min(), relief.min())), float(max(raw.max(), relief.max()))
canvas = Image.new("RGB", (1100, 620), "#f4f2ec")
draw = ImageDraw.Draw(canvas)
for offset, data, title in [(25, raw, "Original DSM (buildings + vegetation)"), (575, relief, "Approximate relief (synthetic smoothing)")]:
    t = np.clip((data - minimum) / (maximum - minimum), 0, 1)
    colors = np.stack([75 + 170*t, 125 + 95*t, 100 + 85*t], axis=-1).astype(np.uint8)
    img = Image.fromarray(colors).resize((500, 500), Image.Resampling.BILINEAR)
    canvas.paste(img, (offset, 55))
    for feature in features:
        geometry = feature["geometry"]
        tags = feature["properties"]["tags"]
        if geometry["type"] == "Polygon" and ("building" in tags or tags.get("amenity") in ["university", "college"]):
            points = geometry["coordinates"][0]
            px, py = to_local.transform(*zip(*points))
            screen = [(offset + (x-XMIN)/SIZE*500, 55+(YMIN+SIZE-y)/SIZE*500) for x,y in zip(px,py)]
            if all(offset <= x <= offset+500 and 55 <= y <= 555 for x,y in screen):
                draw.line(screen, fill="#324b4b", width=1)
    draw.text((offset, 20), title, fill="black")
    draw.text((offset, 570), f"Range {data.min():.1f} - {data.max():.1f} m | north up", fill="black")
draw.text((25, 600), "2048 m square | Source ~30m DSM; output grid 8m is interpolation, not 8m accuracy", fill="black")
canvas.save(OUT / "elevation_comparison.png")
manifest = dict(project_version=(ROOT / "VERSION").read_text().strip(),
    map_version="inha-relief-research-1", status="approximate_visual_only", source=source_info,
    source_sha256=hashlib.sha256((SOURCE / "copernicus_n37_e126.tif").read_bytes()).hexdigest(),
    crs_wkt=CRS_LOCAL.to_wkt(), vertical_datum="EGM2008 orthometric source heights; relative visual offset",
    origin_wgs84=[126.6535, 37.4506], reference_height_m=reference,
    unity=dict(resolution=N, size_x_m=SIZE, size_z_m=SIZE, position_x=XMIN, position_y=base,
               position_z=YMIN, size_y_m=height_range, raw="terrain_south_first_u16.raw", byte_order="little",
               row_order="south_to_north", columns="west_to_east"),
    source_dsm_range_m=[float(raw.min()), float(raw.max())], approximate_range_m=[float(relief.min()), float(relief.max())],
    quantization_error_m=quantization_error, grid_spacing_m=SPACING,
    algorithm="32m sampling, 3x3 p20 low envelope, two 3x3 mean passes, bilinear 8m interpolation",
    limitations=["Not bare-earth survey", "Buildings may remain or real hills may be suppressed",
                 "Not valid for slope/accessibility certification", "Existing map uses approximate local projection; alignment migration required"],
    feature_count=len(features), candidate_count=len(candidates))
(OUT / "manifest.json").write_text(json.dumps(manifest, ensure_ascii=False, indent=2), encoding="utf-8")
print(json.dumps({k:manifest[k] for k in ["source_dsm_range_m", "approximate_range_m", "quantization_error_m", "feature_count", "candidate_count"]}, indent=2))
