using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEditor;
using UnityEditor.SceneManagement;
using Newtonsoft.Json.Linq;

public static class PolishInhaCampus
{
    const string F="Assets/InhaCampus";
    static System.Random rnd=new System.Random(1954);
    static List<Vector3> verts=new List<Vector3>();static List<Vector2> uv=new List<Vector2>();static List<int> tris=new List<int>();
    static float R(float a,float b){return a+(b-a)*(float)rnd.NextDouble();}
    static Vector3 V(JToken p){return new Vector3((float)p["x"],0,(float)p["z"]);}
    static void Q(Vector3 a,Vector3 b,Vector3 c,Vector3 d)
    {int n=verts.Count;verts.AddRange(new[]{a,b,c,d});uv.AddRange(new[]{Vector2.zero,Vector2.up,Vector2.one,Vector2.right});tris.AddRange(new[]{n,n+1,n+2,n,n+2,n+3});}
    static void Box(Vector3 p,Vector3 s,Quaternion r)
    {var x=r*Vector3.right*s.x/2;var y=r*Vector3.up*s.y/2;var z=r*Vector3.forward*s.z/2;Q(p-x-y-z,p-x+y-z,p+x+y-z,p+x-y-z);Q(p+x-y+z,p+x+y+z,p-x+y+z,p-x-y+z);Q(p-x-y+z,p-x+y+z,p-x+y-z,p-x-y-z);Q(p+x-y-z,p+x+y-z,p+x+y+z,p+x-y+z);Q(p-x+y-z,p-x+y+z,p+x+y+z,p+x+y-z);Q(p-x-y+z,p-x-y-z,p+x-y-z,p+x-y+z);}
    static GameObject Mesh(string name,Material mat,Transform parent,bool collider=false)
    {var mesh=new Mesh{name=name,indexFormat=IndexFormat.UInt32};mesh.SetVertices(verts);mesh.SetUVs(0,uv);mesh.SetTriangles(tris,0);mesh.RecalculateNormals();mesh.RecalculateBounds();string path=F+"/Meshes/Polish_"+name+".asset";var old=AssetDatabase.LoadAssetAtPath<Mesh>(path);if(old){EditorUtility.CopySerialized(mesh,old);UnityEngine.Object.DestroyImmediate(mesh);mesh=old;}else AssetDatabase.CreateAsset(mesh,path);var go=new GameObject(name);go.transform.SetParent(parent);go.isStatic=true;go.AddComponent<MeshFilter>().sharedMesh=mesh;go.AddComponent<MeshRenderer>().sharedMaterial=mat;if(collider)go.AddComponent<MeshCollider>().sharedMesh=mesh;verts.Clear();uv.Clear();tris.Clear();return go;}
    static Material M(string name){return AssetDatabase.LoadAssetAtPath<Material>(F+"/Materials/"+name+".mat");}
    static void Texture(string name,string kind,float tiling)
    {
        const int size=512;var t=new Texture2D(size,size,TextureFormat.RGBA32,true);var pixels=new Color[size*size];
        for(int y=0;y<size;y++)for(int x=0;x<size;x++)
        {
            float noise=R(.78f,1.16f),large=Mathf.PerlinNoise(x/42f,y/42f);float v=noise;
            if(kind=="grass")v=(.65f+large*.5f)*noise;
            if(kind=="stone")v=(x%128<2||y%128<2)?.58f:noise;
            if(kind=="paver")v=(x%128<3||(y+(x/128%2)*64)%128<3)?.65f:noise;
            if(kind=="water")v=.8f+Mathf.Sin(x*.18f+Mathf.Sin(y*.06f)*3)*.05f+large*.2f;
            pixels[y*size+x]=new Color(v,v,v,1);
        }
        t.SetPixels(pixels);t.Apply();string path=F+"/Materials/"+name+"_Detail.png";File.WriteAllBytes(path,t.EncodeToPNG());UnityEngine.Object.DestroyImmediate(t);AssetDatabase.ImportAsset(path);var importer=(TextureImporter)AssetImporter.GetAtPath(path);importer.wrapMode=TextureWrapMode.Repeat;importer.anisoLevel=8;importer.SaveAndReimport();var mat=M(name);mat.SetTexture("_BaseMap",AssetDatabase.LoadAssetAtPath<Texture2D>(path));mat.SetTextureScale("_BaseMap",Vector2.one*tiling);EditorUtility.SetDirty(mat);
    }
    static Material LeafMaterial()
    {
        const int sz=256;var tex=new Texture2D(sz,sz,TextureFormat.RGBA32,true);var px=new Color[sz*sz];
        // A branch-sized spray of individual pointed leaves, on transparency.
        for(int leaf=0;leaf<78;leaf++)
        {
            float angle=R(0,Mathf.PI*2),rad=Mathf.Sqrt(R(0,1))*94,cx=128+Mathf.Cos(angle)*rad,cy=128+Mathf.Sin(angle)*rad;
            float la=R(0,6.28f),len=R(8,19),wid=R(3,7),bright=R(.6f,1.3f);
            for(int y=Mathf.Max(0,(int)(cy-len));y<Mathf.Min(sz,cy+len);y++)for(int x=Mathf.Max(0,(int)(cx-len));x<Mathf.Min(sz,cx+len);x++)
            {float dx=x-cx,dy=y-cy,u=(dx*Mathf.Cos(la)+dy*Mathf.Sin(la))/len,v=(-dx*Mathf.Sin(la)+dy*Mathf.Cos(la))/wid;if(Mathf.Abs(v)<(1-u*u)*.85f&&Mathf.Abs(u)<1){float vein=Mathf.Abs(v)<.055f?1.18f:1;px[y*sz+x]=new Color(.48f*bright*vein,.64f*bright*vein,.22f*bright*vein,1);}}
        }
        tex.SetPixels(px);tex.Apply();string path=F+"/Materials/FoliageSpray.png";File.WriteAllBytes(path,tex.EncodeToPNG());UnityEngine.Object.DestroyImmediate(tex);AssetDatabase.ImportAsset(path);var ti=(TextureImporter)AssetImporter.GetAtPath(path);ti.alphaIsTransparency=true;ti.mipmapEnabled=true;ti.mipMapsPreserveCoverage=true;ti.alphaTestReferenceValue=.4f;ti.SaveAndReimport();
        string mp=F+"/Materials/Detailed foliage.mat";var m=AssetDatabase.LoadAssetAtPath<Material>(mp);if(!m){m=new Material(Shader.Find("Universal Render Pipeline/Lit"));AssetDatabase.CreateAsset(m,mp);}m.color=new Color(.72f,.86f,.58f);m.SetTexture("_BaseMap",AssetDatabase.LoadAssetAtPath<Texture2D>(path));m.SetFloat("_AlphaClip",1);m.SetFloat("_Cutoff",.4f);m.SetFloat("_Cull",0);m.SetFloat("_Smoothness",.15f);m.EnableKeyword("_ALPHATEST_ON");m.renderQueue=2450;m.SetOverrideTag("RenderType","TransparentCutout");m.enableInstancing=true;EditorUtility.SetDirty(m);return m;
    }
    public static string Main()
    {
        var root=GameObject.Find("INHA UNIVERSITY | Map only");if(!root)throw new Exception("Load generated campus first.");
        var terrain=root.transform.Find("01 Terrain, water and vegetation");var ground=terrain.Find("Ground | metres, east +X, north +Z").gameObject;UnityEngine.Object.DestroyImmediate(ground.GetComponent<MeshCollider>());var bc=ground.GetComponent<BoxCollider>();if(!bc)bc=ground.AddComponent<BoxCollider>();bc.center=new Vector3(0,-.6f,-90);bc.size=new Vector3(1100,1,1000);
        // Use face-space UVs so tile courses run vertically on facade meshes.
        foreach(var mf in root.GetComponentsInChildren<MeshFilter>())
        {
            if(mf.name.Contains("canopy")||mf.name.Contains("Tree"))continue;var mesh=mf.sharedMesh;var vv=mesh.vertices;var nn=mesh.normals;var u=new Vector2[vv.Length];for(int i=0;i<vv.Length;i++){var n=nn[i];u[i]=Mathf.Abs(n.y)>.7f?new Vector2(vv[i].x,vv[i].z):Mathf.Abs(n.x)>Mathf.Abs(n.z)?new Vector2(vv[i].z,vv[i].y):new Vector2(vv[i].x,vv[i].y);}mesh.uv=u;EditorUtility.SetDirty(mesh);
        }
        Texture("Campus lawn","grass",.22f);Texture("Asphalt","noise",.7f);Texture("Warm limestone","stone",.28f);Texture("Light granite","stone",.28f);Texture("Paving","paver",.7f);Texture("Inkyung lake","water",.16f);
        var foliage=LeafMaterial();int batches=0;
        foreach(string oldName in new[]{"Broadleaf canopy","Sunlit canopy variation"})
        {
            var old=terrain.Find(oldName);if(!old)continue;var ov=old.GetComponent<MeshFilter>().sharedMesh.vertices;
            // Each old canopy lobe has 7*10*6 vertices. Recover its location/size.
            for(int start=0;start<ov.Length;start+=420)
            {
                Bounds bounds=new Bounds(ov[start],Vector3.zero);for(int i=start;i<Mathf.Min(start+420,ov.Length);i++)bounds.Encapsulate(ov[i]);var c=bounds.center;var ext=bounds.extents;
                for(int leaf=0;leaf<64;leaf++)
                {
                    Vector3 dir=new Vector3(R(-1,1),R(-1,1),R(-1,1)).normalized;var p=c+Vector3.Scale(dir,ext)*R(.15f,.98f);var rot=Quaternion.Euler(R(0,360),R(0,360),R(0,360));float size=R(.75f,1.35f);var x=rot*Vector3.right*size;var y=rot*Vector3.up*size;Q(p-x-y,p-x+y,p+x+y,p+x-y);
                }
                if(verts.Count>200000)Mesh("Foliage "+(batches++),foliage,terrain);
            }
            if(verts.Count>0)Mesh("Foliage "+(batches++),foliage,terrain);UnityEngine.Object.DestroyImmediate(old.gameObject);
        }
        var features=JObject.Parse(File.ReadAllText(F+"/Source/campus.json"))["features"];
        var detail=new GameObject("04 Architectural details").transform;detail.SetParent(root.transform);
        var main=features.First(x=>(string)x["name"]=="본관");var pts=main["points"].Select(V).ToArray();float best=0;Vector3 aa=Vector3.zero,bb=Vector3.zero;
        for(int i=0;i<pts.Length;i++){var a=pts[i];var b=pts[(i+1)%pts.Length];float len=Vector3.Distance(a,b);if(len>best&&(a.z+b.z)/2< -140){best=len;aa=a;bb=b;}}
        if(best>0)
        {
            var dir=(bb-aa).normalized;var normal=new Vector3(-.48f,0,-.88f);var rot=Quaternion.LookRotation(normal);float h=(float)main["height"];
            foreach(float fraction in new[]{.12f,.88f}){var center=Vector3.Lerp(aa,bb,fraction)+normal*.55f;Box(center+Vector3.up*10,new Vector3(8.5f,20,1),rot);}Mesh("Main building glazed entrance towers",M("Blue green glazing"),detail);
            foreach(float fraction in new[]{.12f,.88f}){var center=Vector3.Lerp(aa,bb,fraction)+normal*1.15f;foreach(int sign in new[]{-1,1})Box(center+dir*sign*4.8f+Vector3.up*10.7f,new Vector3(1.1f,21.4f,1.4f),rot);Box(center+Vector3.up*21,new Vector3(10.5f,1,1.5f),rot);for(float y=2;y<20;y+=1.6f)Box(center+Vector3.up*y,new Vector3(8.5f,.09f,.1f),rot);for(float x=-3.4f;x<4;x+=1.7f)Box(center+dir*x+Vector3.up*10,new Vector3(.09f,20,.1f),rot);}Mesh("Main building stone portal frames",M("Light granite"),detail);
        }
        // Athletic surfaces retain mapped footprints; markings are environmental geometry.
        foreach(var f in features.Where(x=>(string)x["kind"]=="sport"))
        {
            var pp=f["points"].Select(V).ToArray();if(pp.Length<4)continue;var center=pp.Aggregate(Vector3.zero,(a,b)=>a+b)/pp.Length;var edges=pp.Select((p,i)=>pp[(i+1)%pp.Length]-p).OrderByDescending(e=>e.magnitude).ToArray();var axis=edges[0].normalized;var side=new Vector3(axis.z,0,-axis.x);float length=pp.Max(p=>Vector3.Dot(p-center,axis))-pp.Min(p=>Vector3.Dot(p-center,axis));float width=pp.Max(p=>Vector3.Dot(p-center,side))-pp.Min(p=>Vector3.Dot(p-center,side));length*=.88f;width*=.85f;var rotation=Quaternion.LookRotation(axis);
            foreach(int sign in new[]{-1,1}){Box(center+side*sign*width/2+Vector3.up*.083f,new Vector3(.13f,.015f,length),rotation);Box(center+axis*sign*length/2+Vector3.up*.083f,new Vector3(width,.015f,.13f),rotation);}Box(center+Vector3.up*.085f,new Vector3(width,.015f,.13f),rotation);
        }
        Mesh("Sports field line markings",M("Traffic white"),detail);
        var camera=Camera.main;camera.transform.position=new Vector3(-325,345,-545);camera.transform.LookAt(new Vector3(30,0,-95));camera.fieldOfView=53;
        var data=camera.GetComponent<UnityEngine.Rendering.Universal.UniversalAdditionalCameraData>();if(!data)data=camera.gameObject.AddComponent<UnityEngine.Rendering.Universal.UniversalAdditionalCameraData>();data.antialiasing=UnityEngine.Rendering.Universal.AntialiasingMode.SubpixelMorphologicalAntiAliasing;data.antialiasingQuality=UnityEngine.Rendering.Universal.AntialiasingQuality.High;
        PrefabUtility.SaveAsPrefabAsset(root,F+"/InhaCampusMap.prefab");EditorSceneManager.SaveScene(EditorSceneManager.GetActiveScene());AssetDatabase.SaveAssets();return "Surface textures, foliage, main facade and sports lines saved. Ground collider warning fixed.";
    }
}

