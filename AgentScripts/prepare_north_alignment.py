"""Prepare north-up terrain without discarding the authored relief residual.

The scene's legacy planar scale is preserved; differences from AEQD are measured,
not silently relabelled as an exact CRS migration.
"""
from pathlib import Path
import json
import hashlib
import math
import xml.etree.ElementTree as ET
import numpy as np
from pyproj import CRS, Transformer

ROOT = Path(__file__).resolve().parents[1]
OUT = ROOT / "Docs/MapResearch/Iterations/NorthAlignment"
state = json.loads((OUT / "source.json").read_text())
manifest = json.loads((ROOT / "maps/inha_relief_research/manifest.json").read_text())
n = state["resolution"]
origin = state["terrainPosition"]
size = state["terrainSize"]
assert n == 257 and abs(state["yaw"] + 27.6) < .01
assert max(abs(v) for v in state["mapPosition"].values()) < 1e-5
base = np.fromfile(ROOT / "maps/inha_relief_research/terrain_south_first_u16.raw", dtype="<u2").reshape(n, n) / 65535.
authored = np.array(state["heights"]).reshape(n, n)

def sample(grid, x, z):
    fx = np.clip((x - origin["x"]) / size["x"] * (n-1), 0, n-1)
    fz = np.clip((z - origin["z"]) / size["z"] * (n-1), 0, n-1)
    ix, iz = np.floor(fx).astype(int), np.floor(fz).astype(int)
    jx, jz = np.minimum(ix+1, n-1), np.minimum(iz+1, n-1)
    ax, az = fx-ix, fz-iz
    return ((1-ax)*grid[iz,ix]+ax*grid[iz,jx])*(1-az)+((1-ax)*grid[jz,ix]+ax*grid[jz,jx])*az

x,z=np.meshgrid(np.linspace(origin["x"],origin["x"]+size["x"],n),np.linspace(origin["z"],origin["z"]+size["z"],n))
angle=math.radians(state["yaw"])
# Unity yaw maps local (x,z) -> (cos*x+sin*z, -sin*x+cos*z).
old_x=np.cos(angle)*x+np.sin(angle)*z
old_z=-np.sin(angle)*x+np.cos(angle)*z
inside=(old_x>=origin["x"])&(old_x<=origin["x"]+size["x"])&(old_z>=origin["z"])&(old_z<=origin["z"]+size["z"])
residual=np.where(inside,sample(authored-base,old_x,old_z),0)
to_local=Transformer.from_crs(4326,CRS.from_wkt(manifest["crs_wkt"]),always_xy=True)
east_scale=111320*math.cos(math.radians(37.4506))
east,north=to_local.transform(126.6535+x/east_scale,37.4506+z/110980)
aligned_base=sample(base,east,north)
result=aligned_base+residual
clipped=int(np.count_nonzero((result<0)|(result>1)))
assert clipped == 0, "Terrain range insufficient; do not silently clip sculpted terrain"
result.astype("<f4").tofile(OUT/"aligned-heights-f32.raw")

controls=[]
osm=ET.parse(ROOT/"Docs/MapResearch/2026-09-17/expanded_campus.osm").getroot()
nodes={item.get("id"):item for item in osm.findall("node")}
probes=[]
for name,node_id in [("main_gate","4741793208"),("rear_gate","4741793209")]:
    node=nodes[node_id]
    probes.append((name,float(node.get("lon")),float(node.get("lat")),"OSM node/"+node_id))
probes.append(("dorm_2_area",126.6596125,37.4476264,"OSM footprint centroid probe; not entrance"))
for name,lon,lat,provenance in probes:
    lx=(lon-126.6535)*east_scale; lz=(lat-37.4506)*110980
    ex,nz=to_local.transform(lon,lat)
    ox=math.cos(angle)*lx+math.sin(angle)*lz;oz=-math.sin(angle)*lx+math.cos(angle)*lz
    controls.append(dict(name=name,kind=provenance,longitude=lon,latitude=lat,
                         legacy_north_up=[lx,lz],aeqd=[ex,nz],rotation_displacement_m=math.hypot(ox-lx,oz-lz),
                         legacy_vs_aeqd_m=math.hypot(ex-lx,nz-lz)))
report=dict(status="prepared; scene application separately recorded",source_sha256=hashlib.sha256((OUT/"source.json").read_bytes()).hexdigest(),
            yaw_before=state["yaw"],yaw_after=0,controls=controls,clipped_height_samples=clipped,
            transported_sculpt_residual_range_m=[float(residual.min()*size["y"]),float(residual.max()*size["y"])],
            limitations=["8m bilinear transport smooths authored changes; source retained for rollback",
                         "Legacy linear map scale remains; not a complete AEQD geometry migration",
                         "Rotated terrain boundary outside previous extent receives source relief without invented sculpt residual",
                         "Building pads, lake basins and entrance approaches require post-application checks"])
(OUT/"preparation.json").write_text(json.dumps(report,indent=2),encoding="utf-8")
print(json.dumps(report,indent=2))

# Review diagram from map/elevation data, not a Unity render or an accessibility map.
from PIL import Image, ImageDraw, ImageFont
font=ImageFont.truetype("C:/Windows/Fonts/arial.ttf",20)
small=ImageFont.truetype("C:/Windows/Fonts/arial.ttf",15)
canvas=Image.new("RGB",(1600,830),(244,247,248));draw=ImageDraw.Draw(canvas)
world=(-550,-600,800,550);w,h=760,650
gx,gz=np.meshgrid(np.linspace(world[0],world[2],w),np.linspace(world[3],world[1],h))
features=json.loads((ROOT/"Assets/InhaCampus/Source/campus.json").read_text(encoding="utf-8"))["features"]
for panel,grid,title in [(0,authored,"BEFORE: rotated map over north-up terrain"),(1,result,"PREPARED: north-up map and transported sculpt")]:
    values=sample(grid,gx,gz)*size["y"]+origin["y"]
    shade=np.clip((values+10)/40,0,1)
    rgb=np.stack((125+65*shade,158+27*shade,113+37*shade),axis=-1).astype("uint8")
    tile=Image.fromarray(rgb);pen=ImageDraw.Draw(tile)
    def point(p):
        px,pz=p["x"],p["z"]
        if panel==0:px,pz=math.cos(angle)*px+math.sin(angle)*pz,-math.sin(angle)*px+math.cos(angle)*pz
        return ((px-world[0])/(world[2]-world[0])*(w-1),(world[3]-pz)/(world[3]-world[1])*(h-1))
    for feature in features:
        points=[point(p) for p in feature["points"]]
        if feature["kind"]=="road" and len(points)>1:pen.line(points,fill=(80,88,94),width=3)
        elif feature["kind"] in ["building","water"] and len(points)>2:
            pen.polygon(points,fill=(235,236,225) if feature["kind"]=="building" else (63,174,191),outline=(60,85,87))
    left=25+panel*790;canvas.paste(tile,(left,95));draw.text((left,55),title,font=font,fill=(20,42,54))
    draw.text((left+12,108),"N ^",font=font,fill=(10,30,40))
draw.text((25,12),"CAMPUS NORTH ALIGNMENT / REVIEW ONLY - NOT APPLIED",font=font,fill=(15,38,51))
draw.text((25,765),"Terrain sculpt residual is retained by bilinear transport. Source height array and scene snapshot are preserved.",font=small,fill=(20,42,54))
draw.text((25,794),"Diagram uses source footprint data, not final scene appearance. Legacy planar scale still differs from AEQD (up to 0.69 m at probes).",font=small,fill=(20,42,54))
canvas.save(OUT/"review.png")
