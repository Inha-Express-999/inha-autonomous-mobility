using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;
using CampusSim;

public static class CampusLakeFurniture
{
    const string Folder="Assets/CampusSim/Generated/LakeFurniture";
    static Vector3 P(float x,float z)=>new Vector3(x,0,z);
    // Approximate placement from the north-aligned lake outline, not surveyed furniture coordinates.
    static readonly Vector3[] Path={P(227,-49),P(223,-56),P(219,-73),P(215,-84),P(212,-92),P(203,-104),P(191,-110),P(182,-117),P(175,-128),P(178,-140),P(202,-154)};
    static float Distance(Vector3 p,Vector3 a,Vector3 b)
    {p.y=a.y=b.y=0;var d=b-a;return Vector3.Distance(p,a+d*Mathf.Clamp01(Vector3.Dot(p-a,d)/d.sqrMagnitude));}
    [MenuItem("Campus/Map/Build Lake Stone Path And Benches")]
    public static void Build()
    {
        if(EditorApplication.isPlayingOrWillChangePlaymode||SceneManager.GetActiveScene().path!="Assets/CampusSim/Scenes/CampusTerrain.unity")throw new Exception("Open working scene.");
        if(Directory.Exists(Folder))throw new Exception("Furniture already authored; preserve edits.");
        var terrain=UnityEngine.Object.FindObjectsByType<Terrain>(FindObjectsSortMode.None).Single();
        Directory.CreateDirectory(Folder);AssetDatabase.Refresh();
        var root=new GameObject("Inkyung stone path and roadside benches");
        var stone=Material("Warm grey stone",new Color(.57f,.57f,.51f));
        var wood=Material("Bench timber",new Color(.34f,.23f,.14f));
        var metal=Material("Bench dark frame",new Color(.15f,.20f,.19f));
        int slabs=0;var rng=new System.Random(20260917);
        for(int s=1;s<Path.Length;s++)
        {
            var a=Path[s-1];var b=Path[s];int count=Mathf.CeilToInt(Vector3.Distance(a,b)/1.15f);
            var side=Vector3.Cross(Vector3.up,(b-a).normalized);
            for(int i=0;i<count;i++)for(int lane=0;lane<2;lane++)
            {
                var p=Vector3.Lerp(a,b,(i+.5f)/count)+side*((lane-.5f)*1.05f);
                var go=Box(root.transform,"Stone "+slabs,p,new Vector3(.98f,.10f,1.06f),stone);
                go.transform.rotation=Quaternion.LookRotation(b-a)*Quaternion.Euler(0,(float)(rng.NextDouble()-.5)*7,0);
                Bind(go,terrain,p,.10f);slabs++;
            }
        }
        // Short branch reaches the existing pavilion deck without moving its footprint.
        var pavilion=UnityEngine.Object.FindObjectsByType<CampusTerrainBinding>(FindObjectsSortMode.None).Single(b=>b.name.Contains("pavilion"));
        var branch=P(pavilion.transform.position.x,pavilion.transform.position.z);
        for(int i=0;i<3;i++){var p=Vector3.Lerp(P(212,-92),branch,i/3f);Bind(Box(root.transform,"Pavilion connecting stone",p,new Vector3(1.1f,.10f,1.6f),stone),terrain,p,.10f);}
        var seats=new List<Vector3>();
        var start=P(255.18f,-58.46f);var end=P(212.44f,-147.88f);var normal=P(.902f,-.431f);
        for(int i=0;i<10;i++)
        {
            var p=Vector3.Lerp(start,end,(i+.5f)/10)+normal*.65f;seats.Add(p);
            var bench=new GameObject("Lakeside bench "+(i+1));bench.transform.SetParent(root.transform,false);bench.transform.rotation=Quaternion.LookRotation(-normal);
            Bind(bench,terrain,p,.12f);
            for(int slat=0;slat<4;slat++)Box(bench.transform,"Seat slat",new Vector3(0,.45f,-.23f+slat*.15f),new Vector3(1.8f,.09f,.12f),wood);
            for(int slat=0;slat<3;slat++)Box(bench.transform,"Back slat",new Vector3(0,.69f+slat*.13f,-.32f),new Vector3(1.8f,.10f,.08f),wood);
            foreach(float x in new[]{-.65f,.65f}){Box(bench.transform,"Leg",new Vector3(x,.22f,0),new Vector3(.09f,.44f,.5f),metal);Box(bench.transform,"Back support",new Vector3(x,.65f,-.34f),new Vector3(.07f,.6f,.07f),metal);}
        }
        int cleared=ClearGrass(seats,branch);
        CampusTerrainAuthoring.Bake();
        AssetDatabase.SaveAssets();EditorSceneManager.SaveScene(SceneManager.GetActiveScene());
        File.WriteAllText("Docs/MapResearch/Iterations/lake-furniture.txt","User-observed pavilion-side stone path and roadside benches. Synthetic spacing and dimensions, not measured access. Stone slabs="+slabs+" + 3 branch slabs; benches=10; removed grass clumps="+cleared+". Independent terrain bindings; shared instanced materials; fixed furniture batching enabled.\n");
        CampusTerrainAuthoring.CaptureLake();
    }
    static int ClearGrass(List<Vector3> seats,Vector3 branch)
    {
        int removed=0;var root=GameObject.Find("Campus dense grass cover");
        foreach(var binding in root.GetComponentsInChildren<CampusTerrainBinding>())
        {
            var material=binding.GetComponent<Renderer>().sharedMaterial;var texture=material.GetTexture("_BaseMap");
            int species=texture.name.Contains("03")?3:texture.name.Contains("02")?2:1;
            var blade=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Idyllic Fantasy Nature/Prefabs/Grass_0"+species+".prefab").GetComponent<MeshFilter>().sharedMesh;
            var mesh=binding.sourceMesh;var vertices=mesh.vertices;int size=blade.vertexCount;
            if(vertices.Length%size!=0)throw new Exception("Unexpected grass layout.");
            var keep=new bool[vertices.Length/size];bool changed=false;
            for(int i=0;i<keep.Length;i++)
            {
                var center=Vector3.zero;for(int j=0;j<size;j++)center+=vertices[i*size+j];center=binding.transform.TransformPoint(center/size);center.y=0;
                bool clear=Enumerable.Range(1,Path.Length-1).Any(s=>Distance(center,Path[s-1],Path[s])<1.65f)||Distance(center,P(212,-92),branch)<1.25f||seats.Any(p=>Vector3.Distance(center,p)<1.5f);
                keep[i]=!clear;if(clear){removed++;changed=true;}
            }
            if(!changed)continue;
            var copy=UnityEngine.Object.Instantiate(mesh);var triangles=mesh.triangles;var retained=new List<int>();
            for(int i=0;i<triangles.Length;i+=3)if(keep[triangles[i]/size]){retained.Add(triangles[i]);retained.Add(triangles[i+1]);retained.Add(triangles[i+2]);}
            copy.triangles=retained.ToArray();AssetDatabase.CreateAsset(copy,Folder+"/GrassSource_"+binding.GetInstanceID()+".asset");
            var baked=UnityEngine.Object.Instantiate(copy);AssetDatabase.CreateAsset(baked,Folder+"/GrassBaked_"+binding.GetInstanceID()+".asset");binding.sourceMesh=copy;binding.bakedMesh=baked;binding.GetComponent<MeshFilter>().sharedMesh=baked;EditorUtility.SetDirty(binding);
        }
        return removed;
    }
    static void Bind(GameObject go,Terrain terrain,Vector3 p,float offset)
    {var b=go.AddComponent<CampusTerrainBinding>();b.kind=CampusTerrainBinding.BindingKind.Building;b.originalPosition=p;b.samplePoint=p;b.surfaceOffset=offset;b.sourceId="synthetic lakeside furniture; user-observed arrangement";go.transform.position=p+Vector3.up*(terrain.SampleHeight(p)+terrain.transform.position.y+offset);}
    static Material Material(string name,Color color)
    {var m=new Material(AssetDatabase.LoadAssetAtPath<Material>("Assets/CampusSim/Generated/Materials/CampusBark.mat")){name=name,enableInstancing=true};m.SetColor("_BaseColor",color);m.SetColor("_ColorDim",color*.7f);m.SetTexture("_BaseMap",null);AssetDatabase.CreateAsset(m,Folder+"/"+name+".mat");return m;}
    static GameObject Box(Transform parent,string name,Vector3 p,Vector3 scale,Material material)
    {var go=GameObject.CreatePrimitive(PrimitiveType.Cube);go.name=name;go.transform.SetParent(parent,false);go.transform.localPosition=p;go.transform.localScale=scale;go.GetComponent<Renderer>().sharedMaterial=material;GameObjectUtility.SetStaticEditorFlags(go,StaticEditorFlags.BatchingStatic|StaticEditorFlags.OccludeeStatic);return go;}
}
