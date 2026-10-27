"""Prepare campus-only visual road repairs from an MCP export; never infer access.

Requires Shapely 2.1+ and NumPy. All coordinates are Unity world metres.
"""
import json
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parents[1] / 'tmp/road-repair-python'))
import numpy as np
import shapely
from shapely.geometry import Polygon, LineString
from shapely.ops import unary_union, nearest_points

ROOT = Path(__file__).resolve().parents[1]
OUT = ROOT / 'artifacts/road_connections'


def footprint(mesh):
    v = mesh['source']
    points = [(p['x'], p['z']) for p in v]
    indices = mesh['triangles']
    return shapely.set_precision(unary_union([
        Polygon([points[j] for j in indices[i:i+3]])
        for i in range(0, len(indices), 3)
        if Polygon([points[j] for j in indices[i:i+3]]).area > 1e-8
    ]), .001)


def main():
    data = json.loads((OUT / 'before-meshes.json').read_text(encoding='utf-8'))
    roads = [m for m in data['meshes'] if m['name'].startswith('Road ') or m['name'] == 'Continuous asphalt network']
    shapes = [footprint(m) for m in roads]
    union = unary_union(shapes)
    components = list(shapely.get_parts(union))
    gaps = []
    for i, a in enumerate(components):
        for j, b in enumerate(components[:i]):
            distance = a.distance(b)
            if distance < 20:
                pa, pb = nearest_points(a, b)
                gaps.append(dict(a=i, b=j, distance=distance, start=list(pa.coords)[0], end=list(pb.coords)[0]))
    grades = []
    for m in roads:
        v = np.array([[p['x'], p['y'], p['z']] for p in m['baked']])
        t = np.array(m['bakedTriangles']).reshape(-1,3)
        n = np.cross(v[t[:,1]]-v[t[:,0]],v[t[:,2]]-v[t[:,0]])
        valid = np.abs(n[:,1]) > .0002
        g = np.linalg.norm(n[:,[0,2]],axis=1)/np.maximum(np.abs(n[:,1]),1e-20)
        for k in np.flatnonzero(valid & (g > .1)):
            grades.append(dict(name=m['name'], grade=float(g[k]), area=float(abs(n[k,1])*.5), center=v[t[k]].mean(axis=0).tolist()))
    audit = dict(components=len(components), component_areas=[p.area for p in components], nearby_gaps=gaps,
                 steep_faces=sorted(grades,key=lambda g:-g['grade'])[:30], area=union.area)
    (OUT/'topology-before.json').write_text(json.dumps(audit,indent=2),encoding='utf-8')
    source=json.loads((ROOT/'Assets/CampusSim/Data/road_extensions.json').read_text(encoding='utf-8'))
    corridor_ids={'955217469','471332807','281621314','1390060991','481241658',
                  '284010312','284010313','731224474','471689462','1215393150','284973522','731804508'}
    extensions=[r for r in source['roads'] if r['osmId'] in corridor_ids and not r['pedestrian']]
    extended=unary_union([LineString([(p['x'],p['z']) for p in r['points']])
                         .buffer(r['width']/2,cap_style=2,join_style=2) for r in extensions])
    # Fill exactly the three source-backed graph corridors found by the audit.
    missing=json.loads((OUT/'missing-corridors.json').read_text(encoding='utf-8'))
    extended=extended.union(unary_union([LineString(r['points']).buffer(3,cap_style=2,join_style=2) for r in missing]))
    network=union.union(extended)
    # Do not add nearby but unconnected streets merely because they are inside
    # a station/dormitory neighbourhood. Keep only existing-network components.
    network=unary_union([p for p in shapely.get_parts(network) if p.intersection(union).area>1])
    extended=extended.intersection(network)
    new_corridors=extended.difference(union)
    # Two metre closing fills acute junction notches and point contacts. A one
    # metre opening rounds projecting corners. These are visual design values.
    target = shapely.set_precision(network.buffer(2,quad_segs=6).buffer(-2,quad_segs=6)
                                  .buffer(-1,quad_segs=6).buffer(1,quad_segs=6),.001)
    additions = target.difference(network)
    meshes = []

    def emit(index, shape, height):
        # Remove millimetre nearly-collinear boundary fragments before CDT;
        # otherwise float32 height quantisation becomes a false vertical face.
        shape = shapely.set_precision(shapely.make_valid(shape).simplify(.02,preserve_topology=True),0)
        shape = unary_union([g for g in shapely.get_parts(shapely.get_parts(shape)) if g.geom_type=='Polygon'])
        road=data['meshes'][index]['name'].startswith('Road ') or data['meshes'][index]['name']=='Continuous asphalt network'
        if road:
            # Clip to the profile's 2m triangular height grid. Every output
            # triangle lies in a single plane, so narrow junction fragments
            # cannot amplify interpolation error into artificial slopes.
            x0,z0,x1,z1=shape.bounds
            xs=np.arange(np.floor(x0/2)*2,x1,2)
            zs=np.arange(np.floor(z0/2)*2,z1,2)
            xx,zz=np.meshgrid(xs,zs);xx=xx.ravel();zz=zz.ravel()
            boxes=shapely.box(xx,zz,xx+2,zz+2)
            shapely.prepare(shape)
            keep=shapely.intersects(shape,boxes);xx=xx[keep];zz=zz[keep]
            a=np.stack([xx,zz],axis=1);b=a+[2,0];c=a+[0,2];d=a+[2,2]
            cells=shapely.polygons(np.concatenate([np.stack([a,b,c],axis=1),np.stack([d,c,b],axis=1)]))
            pieces=shapely.intersection(cells,shape,grid_size=.00001)
            triangles=shapely.constrained_delaunay_triangles(pieces)
            triangles=shapely.get_parts(triangles)
        else:
            triangles = shapely.get_parts(shapely.constrained_delaunay_triangles(shape))
        vertices = []
        for triangle in triangles:
            if triangle.area < 1e-7:
                continue
            # Unity upward winding is clockwise in XZ.
            coords = list(triangle.exterior.coords)[:3]
            a,b,c = coords
            if (b[0]-a[0])*(c[1]-a[1])-(b[1]-a[1])*(c[0]-a[0]) > 0:
                coords.reverse()
            vertices.extend(dict(x=x,y=height,z=z) for x,z in coords)
        anchor=data['meshes'][index]['source'][0]
        meshes.append(dict(index=index,name=data['meshes'][index]['name'],anchor=anchor,vertices=vertices))

    for m, shape in zip(roads,shapes):
        i = data['meshes'].index(m)
        # Preserve the earlier synthetic Hitech endpoint grade.
        if m['name'].startswith('Road 481241714 missing'):
            continue
        patched = shape.intersection(target).union(additions.intersection(shape.buffer(2.1)))
        if m['name']=='Continuous asphalt network':
            patched=patched.union(new_corridors.intersection(target)).union(additions.intersection(extended.buffer(2.1)))
        emit(i,patched,0)

    for i,m in enumerate(data['meshes']):
        if m['name']=='Continuous pedestrian network':
            emit(i,footprint(m).union(extended.buffer(1.75)).difference(target.buffer(.025)),.06)
        elif m['name']=='Continuous yellow road edges':
            emit(i,target.buffer(-.25,quad_segs=6).boundary.buffer(.055,quad_segs=3),0)
        elif m['name']=='Junction aware kerbs':
            # Retain the existing curb eligibility/gaps, move its top to the
            # corrected road outline, and keep the collision strip off asphalt.
            mask=footprint(m).buffer(2.1).union(extended.buffer(2.1))
            band=target.boundary.intersection(mask).buffer(.13,quad_segs=3).difference(target.buffer(.025))
            emit(i,band,.14)
    result=dict(schema=1,source='MCP before-meshes.json',status='synthetic visual repair; access unverified',
                roadOffset=.231,sigma=8,meshes=meshes)
    path=ROOT/'Assets/CampusSim/Data/road_surface_repairs.json'
    path.write_text(json.dumps(result,separators=(',',':')),encoding='utf-8')
    after=dict(components=len(list(shapely.get_parts(target))),area=target.area,
               junction_added_m2=additions.area,new_corridor_m2=new_corridors.area,removed_m2=union.difference(target).area,
               max_boundary_displacement_m=target.boundary.hausdorff_distance(union.boundary),
               mesh_count=len(meshes),triangles=sum(len(m['vertices'])//3 for m in meshes),
               source_way_ids=sorted(corridor_ids),
               note='Cached OSM corridor restoration; synthetic widths, closing radius 2m and corner radius 1m; unchanged server graph/access.')
    (OUT/'topology-prepared.json').write_text(json.dumps(after,indent=2),encoding='utf-8')
    print(json.dumps(after,indent=2))

    metadata=json.loads((OUT/'terrain.json').read_text(encoding='utf-8'))
    n=metadata['resolution'];size=metadata['size']
    raw=np.fromfile(OUT/'original-height.f32',dtype='<f4').reshape(n,n).astype('float64')*size['y']
    # Exact separable min/max cone envelopes for Manhattan distance. Each axis
    # is limited to 4%, hence the field's geometric gradient is at most 5.66%.
    # This is a synthetic profile only; the physical terrain retains its DSM
    # seed, and is lowered exclusively beneath the corrected road/sidewalks.
    step=.04*size['x']/(n-1)
    offsets=np.arange(n)*step
    def envelope(values):
        values=np.minimum.accumulate(values-offsets,axis=1)+offsets
        values=np.minimum.accumulate((values+offsets)[:,::-1],axis=1)[:,::-1]-offsets
        values=np.minimum.accumulate(values-offsets[:,None],axis=0)+offsets[:,None]
        return np.minimum.accumulate((values+offsets[:,None])[::-1],axis=0)[::-1]-offsets[:,None]
    profile=(envelope(raw)-envelope(-raw))*.5
    assert max(abs(np.diff(profile,axis=0)).max(),abs(np.diff(profile,axis=1)).max())<=step+1e-6
    (profile/size['y']).astype('<f4').tofile(ROOT/'Assets/CampusSim/Data/road_grade_profile.f32')
    (OUT/'profile-report.json').write_text(json.dumps(dict(axis_grade_limit=.04,
        planar_grid_grade_bound=float(np.sqrt(2)*.04),spacing_m=size['x']/(n-1),
        note='Synthetic grade envelope; original DSM retained for physical terrain outside surface footprints.'),indent=2),encoding='utf-8')


if __name__ == '__main__':
    main()
