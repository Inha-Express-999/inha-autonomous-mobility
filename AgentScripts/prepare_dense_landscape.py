"""Seeded dense Idyllic planting, constrained to authoring greens and current circulation."""
import json,sys,math,random
from pathlib import Path
ROOT=Path(__file__).resolve().parents[1]
sys.path.insert(0,str(ROOT/'.utmp/road-link-deps'))
import numpy as np
import shapely
from shapely.geometry import Point,box
D=ROOT/'artifacts/validation/2026-11-04-dense-landscape'

def union(items):
    polys=[]
    for i in items:
        v=np.array([[p['x'],p['z']] for p in i['vertices']]); t=np.array(i['triangles']).reshape(-1,3)
        p=shapely.polygons(v[t]);polys.extend(p[shapely.area(p)>1e-6])
    return shapely.union_all(polys).buffer(.002).buffer(-.002)

def main():
    items=json.loads((D/'surfaces.json').read_text(encoding='utf-8'))['items']
    greens=union([i for i in items if i['kind']=='green'])
    blocked=union([i for i in items if i['kind'] in ('building','blocked')])
    water=union([i for i in items if i['kind']=='water'])
    lake=union([i for i in items if i['kind']=='water' and 'mapped shoreline' in i['name']])
    # Preserve a clear access apron at the relocated shelter.
    apron=Point(324.584,.375).buffer(5)
    tree_area=greens.buffer(-1.0).difference(blocked.buffer(2.4)).difference(water.buffer(2.0)).difference(apron)
    grass_area=greens.buffer(-.9).difference(blocked.buffer(.9)).difference(water.buffer(.6)).difference(apron)
    willow_area=lake.buffer(10).difference(lake.buffer(2.2)).intersection(greens.buffer(3)).difference(blocked.buffer(2.4))
    rng=random.Random(20261104);trees=[];occupied={};cell=4
    def plant(region,count,willow):
        x0,z0,x1,z1=region.bounds;attempts=0;accepted=0
        while accepted<count and attempts<count*250:
            attempts+=1;x=rng.uniform(x0,x1);z=rng.uniform(z0,z1);p=Point(x,z)
            if not region.covers(p):continue
            radius=rng.uniform(2.8,4.3) if not willow else rng.uniform(3.6,4.6)
            key=(int(x//cell),int(z//cell))
            if any((x-a)**2+(z-b)**2 < ((radius+r)*.60)**2 for dx in range(-3,4) for dz in range(-3,4) for a,b,r in occupied.get((key[0]+dx,key[1]+dz),[])):continue
            if not willow and rng.random()>.62+.22*math.sin(x*.047)*math.cos(z*.033):continue
            species=('WillowTree_0'+str(rng.randint(1,5))+'_Green') if willow else rng.choices(['BroadleafTree_01_Green','BroadleafTree_02_Green','BroadleafTree_03_Green','BroadleafTree_04_Green','BroadleafTree_05_Green','BlossomTree_01','BlossomTree_03','BlossomTree_05'],weights=[16,15,15,15,15,8,8,8])[0]
            trees.append({'x':x,'z':z,'yaw':rng.uniform(0,360),'height':rng.uniform(5.8,10.5) if willow else rng.uniform(4.2,9.0),'species':species,'tone':rng.randrange(5),'willow':willow})
            occupied.setdefault(key,[]).append((x,z,radius));accepted+=1
        return accepted
    willows=plant(willow_area,max(24,int(lake.length/6)),True)
    ordinary=plant(tree_area,min(1600,max(900,int(tree_area.area/40))),False)
    grass=[];x0,z0,x1,z1=grass_area.bounds;step=.40
    for z in np.arange(z0,z1,step):
        for x in np.arange(x0,x1,step):
            a=float(x+rng.uniform(-.20,.20));b=float(z+rng.uniform(-.20,.20))
            if not grass_area.covers(Point(a,b)):continue
            # Full jitter and varied clump size break up a mechanical grid.
            if rng.random()<.06+.09*(1+math.sin(a*.19)*math.cos(b*.13))/2:continue
            grass.append({'x':a,'z':b,'yaw':rng.uniform(0,360),'height':rng.uniform(.32,.72),'species':rng.choices([0,1,2],[.56,.28,.16])[0],'tone':rng.randrange(3)})
    doc={'seed':20261104,'trees':trees,'grass':grass,'stop':{'x':322.318289,'z':-3.74269,'shelterX':324.583689,'shelterZ':.374510,'yaw':208.81}}
    (ROOT/'Assets/CampusSim/Data/dense_landscape.json').write_text(json.dumps(doc,separators=(',',':')),encoding='utf-8')
    report={'green_area_m2':greens.area,'eligible_tree_area_m2':tree_area.area,'eligible_grass_area_m2':grass_area.area,'lake_willows':willows,'other_trees':ordinary,'grass_clumps':len(grass),'species':dict(__import__('collections').Counter(t['species'] for t in trees)),'seed':doc['seed']}
    (D/'placement-plan.json').write_text(json.dumps(report,indent=2),encoding='utf-8');print(json.dumps(report,indent=2))
if __name__=='__main__':main()
