// Execute through Unity Pipeline run_script. Editor-only authoring; no runtime dependency.
using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEditor;
using UnityEditor.SceneManagement;

public static class BuildInhaCampus
{
    [Serializable] public class Ring { public Vector3[] points; }
    [Serializable] public class Feature { public string id,name,kind,subtype; public float height,width; public Vector3[] points,triangles; public Ring[] holes; }
    [Serializable] public class Map { public double originLatitude,originLongitude; public Vector3[] boundary; public Feature[] features; }
    const string Folder="Assets/InhaCampus";
    static Map map; static Transform root; static int serial,treeCount; static System.Random random;
    static Material stone,concrete,roof,glass,glassLight,frame,asphalt,paving,grass,water,bark,leaves,leavesLight,yellow,white,brick;
    static readonly List<string> issues=new List<string>();
    class Geo
    {
        public List<Vector3> v=new List<Vector3>(); public List<Vector2> uv=new List<Vector2>(); public List<int> t=new List<int>();
        public void Tri(Vector3 a,Vector3 b,Vector3 c) { int n=v.Count; v.Add(a);v.Add(b);v.Add(c);uv.Add(new Vector2(a.x,a.z));uv.Add(new Vector2(b.x,b.z));uv.Add(new Vector2(c.x,c.z));t.Add(n);t.Add(n+1);t.Add(n+2); }
        public void Quad(Vector3 a,Vector3 b,Vector3 c,Vector3 d) { Tri(a,b,c);Tri(a,c,d); }
        public void Box(Vector3 p,Vector3 size,Quaternion q)
        {
            Vector3 x=q*Vector3.right*size.x/2,y=q*Vector3.up*size.y/2,z=q*Vector3.forward*size.z/2;
            Quad(p-x-y-z,p-x+y-z,p+x+y-z,p+x-y-z);Quad(p+x-y+z,p+x+y+z,p-x+y+z,p-x-y+z);
            Quad(p-x-y+z,p-x+y+z,p-x+y-z,p-x-y-z);Quad(p+x-y-z,p+x+y-z,p+x+y+z,p+x-y+z);
            Quad(p-x+y-z,p-x+y+z,p+x+y+z,p+x+y-z);Quad(p-x-y+z,p-x-y-z,p+x-y-z,p+x-y+z);
        }
        public void Ball(Vector3 p,Vector3 scale,int seed,int rings=7,int sides=10)
        {
            var r=new System.Random(seed); float[] jitter=new float[(rings+1)*(sides+1)];for(int k=0;k<jitter.Length;k++)jitter[k]=.88f+(float)r.NextDouble()*.24f;
            Vector3[,] pts=new Vector3[rings+1,sides+1];
            for(int i=0;i<=rings;i++)for(int j=0;j<=sides;j++){float a=Mathf.PI*i/rings,b=2*Mathf.PI*j/sides;pts[i,j]=p+Vector3.Scale(new Vector3(Mathf.Sin(a)*Mathf.Cos(b),Mathf.Cos(a),Mathf.Sin(a)*Mathf.Sin(b)),scale)*jitter[i*(sides+1)+(j%sides)];}
            for(int i=0;i<rings;i++)for(int j=0;j<sides;j++)Quad(pts[i,j],pts[i,j+1],pts[i+1,j+1],pts[i+1,j]);
        }
        public void Beam(Vector3 a,Vector3 b,float width,float depth=0) { Box((a+b)/2,new Vector3(width,depth==0?width:depth,Vector3.Distance(a,b)),Quaternion.LookRotation((b-a).normalized)); }
        public void Flat(Vector3[] tris,float h) { for(int i=0;i+2<tris.Length;i+=3){Vector3 a=tris[i]+Vector3.up*h,b=tris[i+1]+Vector3.up*h,c=tris[i+2]+Vector3.up*h;if(Vector3.Cross(b-a,c-a).y<0)Tri(a,c,b);else Tri(a,b,c);} }
    }
    static Material Mat(string name,Color color,float smooth=0,float metallic=0)
    {
        string path=Folder+"/Materials/"+name+".mat";var m=AssetDatabase.LoadAssetAtPath<Material>(path);
        if(m==null){m=new Material(Shader.Find("Universal Render Pipeline/Lit"));AssetDatabase.CreateAsset(m,path);}
        m.color=color;m.SetFloat("_Smoothness",smooth);m.SetFloat("_Metallic",metallic);m.enableInstancing=true;EditorUtility.SetDirty(m);return m;
    }
    static Color C(float r,float g,float b){return new Color(r,g,b);}
    static GameObject Emit(string name,Geo g,Material mat,Transform parent,bool collider=false)
    {
        if(g.v.Count==0)return null;var mesh=new Mesh{name=name,indexFormat=IndexFormat.UInt32};mesh.SetVertices(g.v);mesh.SetUVs(0,g.uv);mesh.SetTriangles(g.t,0);mesh.RecalculateNormals();mesh.RecalculateBounds();
        AssetDatabase.CreateAsset(mesh,Folder+"/Meshes/"+(serial++).ToString("D4")+".asset");
        var go=new GameObject(name);go.transform.SetParent(parent,false);go.isStatic=true;go.AddComponent<MeshFilter>().sharedMesh=mesh;go.AddComponent<MeshRenderer>().sharedMaterial=mat;if(collider)go.AddComponent<MeshCollider>().sharedMesh=mesh;return go;
    }
    static Transform Group(string name,Transform p){var g=new GameObject(name);g.transform.SetParent(p,false);g.isStatic=true;return g.transform;}
    static bool Inside(Vector3 p,Vector3[] poly)
    {
        bool c=false;for(int i=0,j=poly.Length-1;i<poly.Length;j=i++){var a=poly[i];var b=poly[j];if((a.z>p.z)!=(b.z>p.z)&&p.x<(b.x-a.x)*(p.z-a.z)/(b.z-a.z)+a.x)c=!c;}return c;
    }
    static float Distance(Vector3 p,Vector3 a,Vector3 b){var d=b-a;return Vector3.Distance(p,a+d*Mathf.Clamp01(Vector3.Dot(p-a,d)/Mathf.Max(.001f,d.sqrMagnitude)));}
    static float EdgeDistance(Vector3 p,Vector3[] poly,bool closed=true){float d=float.MaxValue;for(int i=0;i<poly.Length-(closed?0:1);i++)d=Mathf.Min(d,Distance(p,poly[i],poly[(i+1)%poly.Length]));return d;}
    static float Rand(float a,float b){return a+(b-a)*(float)random.NextDouble();}
    static bool Clear(Vector3 p,float clearance)
    {
        if(!Inside(p,map.boundary))return false;
        foreach(var f in map.features){if(f.kind=="building"||f.kind=="water"||f.kind=="sport"||f.kind=="plaza"){if(Inside(p,f.points)||EdgeDistance(p,f.points)<clearance)return false;}else if(f.kind=="road"&&EdgeDistance(p,f.points,false)<f.width/2+clearance)return false;}return true;
    }
    static void Building(Feature f,Transform parent)
    {
        var group=Group(f.name+" | OSM "+f.id,parent);Geo walls=new Geo(),caps=new Geo(),windows=new Geo(),windows2=new Geo(),frames=new Geo(),trim=new Geo();
        float h=f.height; bool tower=f.name.Contains("60주년")||f.name.Contains("하이테크");bool main=f.name=="본관";bool library=f.name.Contains("정석");
        if(f.name=="Grazie"||f.name.Contains("주차"))h=4;
        var rings=new List<Vector3[]>{f.points};if(f.holes!=null)rings.AddRange(f.holes.Select(x=>x.points));
        for(int ri=0;ri<rings.Count;ri++)
        {
            var pp=rings[ri];float area=0;for(int i=0;i<pp.Length;i++)area+=pp[i].x*pp[(i+1)%pp.Length].z-pp[(i+1)%pp.Length].x*pp[i].z;
            for(int i=0;i<pp.Length;i++)
            {
                Vector3 a=pp[i],b=pp[(i+1)%pp.Length],dir=(b-a).normalized;float len=Vector3.Distance(a,b);if(len<.05f)continue;
                var normal=new Vector3(dir.z,0,-dir.x)*(area>0?1:-1)*(ri==0?1:-1);var rot=Quaternion.LookRotation(normal);
                walls.Beam(a+Vector3.up*h/2,b+Vector3.up*h/2,.45f,h);
                trim.Beam(a+Vector3.up*.55f,b+Vector3.up*.55f,.65f,1.1f);
                trim.Beam(a+Vector3.up*(h+.3f),b+Vector3.up*(h+.3f),.75f,.6f);
                int floors=Mathf.Max(1,Mathf.RoundToInt(h/(library?5.6f:3.8f)));float floorH=h/floors;float bay=tower?2.7f:main?3.8f:3.2f;int bays=Mathf.FloorToInt(len/bay);
                for(int fl=0;fl<floors;fl++)
                {
                    float y=fl*floorH+floorH*.56f;
                    if(fl>0)trim.Beam(a+Vector3.up*(fl*floorH),b+Vector3.up*(fl*floorH),tower?.55f:.65f,tower?.16f:.25f);
                    for(int w=0;w<bays;w++)
                    {
                        var p=Vector3.Lerp(a,b,(w+.5f)/bays)+Vector3.up*y+normal*.28f;float ww=len/bays*(tower?.94f:.68f),wh=floorH*(tower?.91f:.55f);
                        (random.NextDouble()>.22?windows:windows2).Box(p,new Vector3(ww,wh,.09f),rot);
                        frames.Box(p+normal*.06f,new Vector3(.07f,wh,.1f),rot);
                        frames.Box(p+Vector3.up*wh*.5f+normal*.04f,new Vector3(ww,.08f,.1f),rot);
                        frames.Box(p-Vector3.up*wh*.5f+normal*.04f,new Vector3(ww,.08f,.1f),rot);
                        if(tower)frames.Box(p+normal*.04f,new Vector3(ww,.06f,.1f),rot);
                    }
                }
                if(main&&len>50)
                {
                    for(float x=3;x<len-2;x+=8){var p=a+dir*x+normal*.85f;trim.Box(p+Vector3.up*h*.5f,new Vector3(.9f,h-1,1.1f),rot);trim.Box(p+Vector3.up*(h-1),new Vector3(1.45f,.55f,1.5f),rot);}
                }
            }
        }
        caps.Flat(f.triangles,h-.1f);Emit("Facades",walls,tower?glass:stone,group,true);Emit("Roof",caps,roof,group,true);Emit("Window glazing",windows,glass,group);Emit("Window reflections",windows2,glassLight,group);Emit("Aluminium mullions",frames,frame,group);Emit("Stone bands and parapets",trim,concrete,group);
        Vector3 center=f.points.Aggregate(Vector3.zero,(a,b)=>a+b)/f.points.Length;
        Geo equipment=new Geo();for(int i=0;i<(tower?5:3);i++){var p=center+new Vector3(i*3-3,h+.8f,0);if(Inside(p,f.points)&&!(f.holes??new Ring[0]).Any(r=>Inside(p,r.points)))equipment.Box(p,new Vector3(2,1.3f,3),Quaternion.identity);}Emit("Roof ventilation",equipment,frame,group);
    }
    static void Roads(Transform parent)
    {
        Geo roads=new Geo(),walks=new Geo(),kerbs=new Geo(),mark=new Geo(),crossings=new Geo();
        foreach(var f in map.features.Where(x=>x.kind=="road"))
        {
            bool foot=f.subtype=="footway"||f.subtype=="path"||f.subtype=="steps"||f.subtype=="pedestrian";float w=f.width;
            for(int i=0;i<f.points.Length-1;i++)
            {
                Vector3 a=f.points[i],b=f.points[i+1],d=(b-a).normalized,n=new Vector3(d.z,0,-d.x);float len=Vector3.Distance(a,b);if(len<.01)continue;
                (foot?walks:roads).Beam(a+Vector3.up*(foot?.09f:.03f),b+Vector3.up*(foot?.09f:.03f),w,foot?.16f:.1f);
                if(!foot)
                {
                    foreach(int sign in new[]{-1,1}){
                        Vector3 off=n*sign*(w/2+1.05f);walks.Beam(a+off+Vector3.up*.12f,b+off+Vector3.up*.12f,2,.24f);
                        off=n*sign*(w/2+.1f);kerbs.Beam(a+off+Vector3.up*.16f,b+off+Vector3.up*.16f,.2f,.32f);
                        mark.Beam(a+n*sign*(w/2-.3f)+Vector3.up*.09f,b+n*sign*(w/2-.3f)+Vector3.up*.09f,.1f,.015f);
                    }
                    if(len>25&&w>=7)for(float s=4;s<len-3;s+=8)crossings.Beam(a+d*s+Vector3.up*.095f,a+d*Mathf.Min(s+3,len)+Vector3.up*.095f,.12f,.015f);
                }
            }
        }
        // Crosswalk paint only at mapped pedestrian/vehicle junctions.
        var junctions=new List<Vector3>();
        foreach(var f in map.features.Where(x=>x.kind=="road"&&(x.subtype=="footway"||x.subtype=="pedestrian")))foreach(var p in new[]{f.points[0],f.points[f.points.Length-1]})
        {
            if(junctions.Any(q=>Vector3.Distance(p,q)<14))continue;
            foreach(var r in map.features.Where(x=>x.kind=="road"&&x.width>=7))for(int i=0;i<r.points.Length-1;i++)if(Distance(p,r.points[i],r.points[i+1])<2)
            {var d=(r.points[i+1]-r.points[i]).normalized;var n=new Vector3(d.z,0,-d.x);for(float k=-r.width/2+.5f;k<r.width/2;k+=1.1f)crossings.Beam(p+n*k-d*1.5f+Vector3.up*.105f,p+n*k+d*1.5f+Vector3.up*.105f,.5f,.02f);junctions.Add(p);break;}
        }
        Emit("Mapped asphalt carriageways",roads,asphalt,parent,true);Emit("Pedestrian paths and sidewalks",walks,paving,parent,true);Emit("Granite kerbs",kerbs,concrete,parent,true);Emit("Yellow edge markings",mark,yellow,parent);Emit("Crosswalks and lane dashes",crossings,white,parent);
    }
    static void Landscape(Transform parent)
    {
        Geo baseGeo=new Geo();baseGeo.Box(new Vector3(0,-.6f,-90),new Vector3(1100,1,1000),Quaternion.identity);var ground=Emit("Ground | metres, east +X, north +Z",baseGeo,grass,parent,false);var groundCollider=ground.AddComponent<BoxCollider>();groundCollider.center=new Vector3(0,-.6f,-90);groundCollider.size=new Vector3(1100,1,1000);
        foreach(var f in map.features.Where(x=>x.kind=="green"||x.kind=="sport"||x.kind=="plaza"))
        {Geo g=new Geo();g.Flat(f.triangles,f.kind=="green"?-.04f:.055f);Emit(f.name,g,f.kind=="green"?grass:f.kind=="plaza"?paving:f.subtype=="soccer"?grass:brick,parent,f.kind!="green");}
        foreach(var f in map.features.Where(x=>x.kind=="water"))
        {
            var gp=Group(f.name=="인경호"?"인경호 | mapped shoreline":"본관 앞 수로 | reflecting pool",parent);Geo surface=new Geo(),bank=new Geo();surface.Flat(f.triangles,.06f);
            for(int i=0;i<f.points.Length;i++){var a=f.points[i];var b=f.points[(i+1)%f.points.Length];bank.Beam(a+Vector3.up*.18f,b+Vector3.up*.18f,.65f,.48f);}Emit("Water",surface,water,gp);Emit("Stone water edge",bank,concrete,gp,true);
        }
        Geo trunks=new Geo(),crowns=new Geo(),crownsLight=new Geo();var planted=new List<Vector3>();
        Action<Vector3,bool> tree=(p,pruned)=>{
            float h=pruned?Rand(3,5):Rand(6,10),rad=pruned?Rand(1.3f,2.1f):Rand(2.1f,3.4f);trunks.Beam(p,p+Vector3.up*h*.78f,pruned?.22f:.35f);
            for(int b=0;b<5;b++){float angle=b*2.39996f;var tip=p+new Vector3(Mathf.Cos(angle)*rad*.8f,h*(.56f+b*.075f),Mathf.Sin(angle)*rad*.8f);trunks.Beam(p+Vector3.up*h*.48f,tip,.11f);(b%2==0?crowns:crownsLight).Ball(tip,new Vector3(rad,rad*(pruned?.45f:.86f),rad),random.Next());}
            treeCount++;planted.Add(p);
        };
        // Rows follow real roads; gaps preserve intersections and building clearances.
        foreach(var f in map.features.Where(x=>x.kind=="road"&&x.width>=7))for(int i=0;i<f.points.Length-1;i++)
        {var a=f.points[i];var b=f.points[i+1];var d=(b-a).normalized;var n=new Vector3(d.z,0,-d.x);float len=Vector3.Distance(a,b);for(float s=5;s<len;s+=12)foreach(int sign in new[]{-1,1}){var p=a+d*s+n*sign*(f.width/2+4);if(Clear(p,2.2f)&&!planted.Any(q=>Vector3.Distance(p,q)<7))tree(p,false);}}
        foreach(var f in map.features.Where(x=>x.kind=="green"))
        {
            float minX=f.points.Min(p=>p.x),maxX=f.points.Max(p=>p.x),minZ=f.points.Min(p=>p.z),maxZ=f.points.Max(p=>p.z);
            for(int i=0;i<Mathf.Min(250,(maxX-minX)*(maxZ-minZ)/90);i++){Vector3 p=new Vector3(Rand(minX,maxX),0,Rand(minZ,maxZ));if(Inside(p,f.points)&&Clear(p,3)&&!planted.Any(q=>Vector3.Distance(p,q)<6)&&EdgeDistance(p,f.points)<7)tree(p,false);}
        }
        // Sculpted pines beside the photographed formal reflecting pool.
        var pool=map.features.First(x=>x.kind=="water"&&x.name!="인경호");
        for(int i=0;i<pool.points.Length;i++){var a=pool.points[i];var b=pool.points[(i+1)%pool.points.Length];if(Vector3.Distance(a,b)<40)continue;var d=(b-a).normalized;var n=new Vector3(d.z,0,-d.x);for(float s=5;s<Vector3.Distance(a,b)-4;s+=9)foreach(int sign in new[]{-1,1}){var p=a+d*s+n*sign*5;if(Clear(p,1.5f)&&!planted.Any(q=>Vector3.Distance(p,q)<4))tree(p,true);}}
        Emit("Tree trunks and branches",trunks,bark,parent,true);Emit("Broadleaf canopy",crowns,leaves,parent);Emit("Sunlit canopy variation",crownsLight,leavesLight,parent);
    }
    public static string Main()
    {
        if(Application.isPlaying)throw new InvalidOperationException("Exit Play mode before authoring.");
        if(EditorSceneManager.GetActiveScene().isDirty)throw new InvalidOperationException("Save current scene before generating the campus.");
        if(File.Exists(Folder+"/InhaCampus.unity"))throw new InvalidOperationException("Campus already exists; preserve it before rebuilding.");
        Directory.CreateDirectory(Folder+"/Materials");Directory.CreateDirectory(Folder+"/Meshes");AssetDatabase.Refresh();
        map=Newtonsoft.Json.JsonConvert.DeserializeObject<Map>(File.ReadAllText(Folder+"/Source/campus.json"));if(map.features==null||map.features.Length==0)throw new InvalidDataException("Missing geographic features.");random=new System.Random(1954);serial=0;treeCount=0;
        stone=Mat("Warm limestone",C(.71f,.69f,.63f));concrete=Mat("Light granite",C(.79f,.78f,.72f));roof=Mat("Roof membrane",C(.27f,.31f,.29f));glass=Mat("Blue green glazing",C(.16f,.32f,.35f),.82f,.3f);glassLight=Mat("Sky reflected glazing",C(.34f,.52f,.56f),.8f,.25f);frame=Mat("Window frames",C(.49f,.53f,.51f),.4f,.5f);asphalt=Mat("Asphalt",C(.17f,.185f,.19f));paving=Mat("Paving",C(.61f,.59f,.54f));grass=Mat("Campus lawn",C(.32f,.40f,.19f));water=Mat("Inkyung lake",C(.075f,.29f,.31f),.93f,.25f);bark=Mat("Tree bark",C(.24f,.19f,.12f));leaves=Mat("Leaf green",C(.19f,.31f,.09f));leavesLight=Mat("Leaf light green",C(.32f,.43f,.12f));yellow=Mat("Traffic yellow",C(.9f,.65f,.19f));white=Mat("Traffic white",C(.87f,.86f,.79f));brick=Mat("Sports clay",C(.43f,.25f,.17f));
        var scene=EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);root=Group("INHA UNIVERSITY | Map only",null);
        Landscape(Group("01 Terrain, water and vegetation",root));Roads(Group("02 Roads and pedestrian network",root));var buildings=Group("03 Campus buildings",root);foreach(var f in map.features.Where(x=>x.kind=="building"))Building(f,buildings);
        var sun=new GameObject("Sun").AddComponent<Light>();sun.type=LightType.Directional;sun.intensity=2.2f;sun.color=new Color(1,.95f,.85f);sun.shadows=LightShadows.Soft;sun.transform.rotation=Quaternion.Euler(48,-35,0);RenderSettings.sun=sun;RenderSettings.ambientMode=AmbientMode.Trilight;RenderSettings.ambientSkyColor=C(.6f,.72f,.85f);RenderSettings.ambientEquatorColor=C(.53f,.57f,.57f);RenderSettings.ambientGroundColor=C(.29f,.31f,.25f);RenderSettings.fog=true;RenderSettings.fogColor=C(.7f,.79f,.85f);RenderSettings.fogMode=FogMode.ExponentialSquared;RenderSettings.fogDensity=.00045f;
        var sky=new Material(Shader.Find("Skybox/Procedural"));sky.SetFloat("_AtmosphereThickness",.85f);sky.SetFloat("_Exposure",1.1f);AssetDatabase.CreateAsset(sky,Folder+"/Materials/Sky.mat");RenderSettings.skybox=sky;
        var camera=new GameObject("Campus overview camera").AddComponent<Camera>();camera.tag="MainCamera";camera.transform.position=new Vector3(-480,470,-690);camera.transform.LookAt(new Vector3(30,0,-90));camera.fieldOfView=48;camera.nearClipPlane=.3f;camera.farClipPlane=2500;camera.allowHDR=true;
        EditorSceneManager.SaveScene(scene,Folder+"/InhaCampus.unity");PrefabUtility.SaveAsPrefabAsset(root.gameObject,Folder+"/InhaCampusMap.prefab");AssetDatabase.SaveAssets();
        if(SceneView.lastActiveSceneView!=null)SceneView.lastActiveSceneView.LookAt(new Vector3(30,0,-100),Quaternion.Euler(48,32,0),640);
        string report="Buildings: "+map.features.Count(f=>f.kind=="building")+"; road sections: "+map.features.Count(f=>f.kind=="road")+"; trees: "+treeCount+"; mesh assets: "+serial;
        File.WriteAllText("Docs/AI/CampusBuildReport.txt",report);return report;
    }
}

