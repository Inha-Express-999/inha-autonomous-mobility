using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;
using CampusSim;

public static class CampusLakesideAuthoring
{
    const string Folder="Assets/CampusSim/Generated/Lakeside";
    [MenuItem("Campus/Map/Plant Willows And Build Pavilion")]
    public static void Build()
    {
        if(EditorApplication.isPlayingOrWillChangePlaymode||SceneManager.GetActiveScene().path!="Assets/CampusSim/Scenes/CampusTerrain.unity")throw new Exception("Open working scene.");
        if(GameObject.Find("Inkyung lakeside garden"))throw new Exception("Lakeside garden already exists; preserve edits.");
        var terrain=UnityEngine.Object.FindObjectsByType<Terrain>(FindObjectsSortMode.None).Single();
        var all=UnityEngine.Object.FindObjectsByType<CampusTerrainBinding>(FindObjectsSortMode.None);
        var lake=all.Single(b=>b.kind==CampusTerrainBinding.BindingKind.Water&&b.name.Contains("인경호"));
        var water=lake.GetComponentsInChildren<MeshFilter>().Single(m=>m.name=="Water");
        var wv=water.sharedMesh.vertices.Select(water.transform.TransformPoint).ToArray();var wt=water.sharedMesh.triangles;
        var shore=new List<Vector3>();
        foreach(var green in all.Where(b=>b.kind==CampusTerrainBinding.BindingKind.Surface&&b.name.StartsWith("green ")))
        {
            var mesh=green.originalMesh?green.originalMesh:green.sourceMesh;var v=mesh.vertices.Select(green.transform.TransformPoint).ToArray();var t=mesh.triangles;
            for(float z=v.Min(p=>p.z);z<v.Max(p=>p.z);z+=4)for(float x=v.Min(p=>p.x);x<v.Max(p=>p.x);x+=4)
            {
                var p=new Vector2(x,z);if(!CampusLandscapeAuthoring.Inside(p,v,t)||CampusLandscapeAuthoring.Inside(p,wv,wt))continue;
                bool margin=true;for(int k=0;k<8;k++){float a=k*Mathf.PI/4;if(!CampusLandscapeAuthoring.Inside(p+new Vector2(Mathf.Cos(a),Mathf.Sin(a))*3,v,t)){margin=false;break;}}
                if(!margin)continue;
                bool near=false;for(int k=0;k<16;k++){float a=k*Mathf.PI/8;if(CampusLandscapeAuthoring.Inside(p+new Vector2(Mathf.Cos(a),Mathf.Sin(a))*10,wv,wt)){near=true;break;}}
                if(near)shore.Add(new Vector3(x,0,z));
            }
        }
        if(shore.Count==0)throw new Exception("No green shore candidates.");
        Directory.CreateDirectory(Folder);AssetDatabase.Refresh();
        var leaves=new Material(AssetDatabase.LoadAssetAtPath<Material>("Assets/CampusSim/Generated/Materials/CampusLeaves.mat"));
        leaves.name="Willow leaves";leaves.SetTexture("_BaseMap",AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/Idyllic Fantasy Nature/Textures/Trees/WillowTree_Branch.png"));leaves.SetColor("_BaseColor",new Color(.56f,.72f,.31f));AssetDatabase.CreateAsset(leaves,Folder+"/Willow.mat");
        var bark=AssetDatabase.LoadAssetAtPath<Material>("Assets/CampusSim/Generated/Materials/CampusBark.mat");
        var root=new GameObject("Inkyung lakeside garden").transform;
        var occupied=new List<Vector3>();
        var pavilionPoint=shore.OrderBy(p=>(p-new Vector3(water.GetComponent<Renderer>().bounds.center.x,0,water.GetComponent<Renderer>().bounds.center.z)).sqrMagnitude).First();
        var pavilion=new GameObject("Lakeside pavilion | photo guided proposal").transform;pavilion.SetParent(root,false);Bind(pavilion,pavilionPoint,terrain,"synthetic pavilion location; photo guided form");occupied.Add(pavilionPoint);
        var wood=Mat("Pavilion timber",new Color(.31f,.15f,.09f));var roof=Mat("Pavilion roof",new Color(.12f,.18f,.19f));
        Box(pavilion,"Deck",new Vector3(0,.25f,0),new Vector3(6,.5f,6),wood);
        foreach(float x in new[]{-2.5f,2.5f})foreach(float z in new[]{-2.5f,2.5f})Box(pavilion,"Timber column",new Vector3(x,2,z),new Vector3(.25f,3.5f,.25f),wood);
        var rm=new Mesh{name="Simple hipped pavilion roof"};rm.vertices=new[]{new Vector3(-3.6f,3.8f,-3.6f),new Vector3(3.6f,3.8f,-3.6f),new Vector3(3.6f,3.8f,3.6f),new Vector3(-3.6f,3.8f,3.6f),new Vector3(0,5.6f,0)};rm.triangles=new[]{0,4,1,1,4,2,2,4,3,3,4,0,0,1,2,0,2,3};rm.RecalculateNormals();AssetDatabase.CreateAsset(rm,Folder+"/PavilionRoof.asset");
        var rg=new GameObject("Hipped roof");rg.transform.SetParent(pavilion,false);rg.AddComponent<MeshFilter>().sharedMesh=rm;rg.AddComponent<MeshRenderer>().sharedMaterial=roof;
        foreach(float x in new[]{-2.5f,2.5f})Box(pavilion,"Side bench",new Vector3(x,.9f,0),new Vector3(.5f,.2f,4.5f),wood);
        int count=0;
        foreach(var point in shore.OrderBy(p=>p.z).ThenBy(p=>p.x))
        {
            if(occupied.Any(p=>(p-point).sqrMagnitude<100))continue;
            var prefab=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Idyllic Fantasy Nature/Prefabs/WillowTree_0"+(count%3+1)+"_Green.prefab");
            var tree=(GameObject)PrefabUtility.InstantiatePrefab(prefab,root);tree.name="Lakeside willow "+count;
            var renderers=tree.GetComponentsInChildren<Renderer>();var bounds=renderers[0].bounds;foreach(var r in renderers)bounds.Encapsulate(r.bounds);
            tree.transform.localScale*=10/Mathf.Max(.1f,bounds.size.y);tree.transform.localRotation=Quaternion.Euler(0,count*137,0);
            foreach(var renderer in renderers)renderer.sharedMaterials=renderer.sharedMaterials.Select(m=>m.name.IndexOf("bark",StringComparison.OrdinalIgnoreCase)>=0?bark:leaves).ToArray();
            Bind(tree.transform,point,terrain,"synthetic photo guided willow planting");occupied.Add(point);count++;
        }
        CampusWillowCanopy.Apply();
        AssetDatabase.SaveAssets();EditorSceneManager.SaveScene(SceneManager.GetActiveScene());
        File.WriteAllText("Docs/MapResearch/Iterations/lakeside-garden.txt","Willows="+count+"; pavilion=1. Approximate visual placement, not surveyed. Reference: https://www.dealbada.com/bbs/board.php?bo_table=gal_free&wr_id=11569\n");
    }
    static void Bind(Transform t,Vector3 p,Terrain terrain,string source)
    {var b=t.gameObject.AddComponent<CampusTerrainBinding>();b.kind=CampusTerrainBinding.BindingKind.Vegetation;b.originalPosition=p+Vector3.up*.15f;b.samplePoint=p;b.sourceId=source;t.position=b.originalPosition+Vector3.up*(terrain.SampleHeight(p)+terrain.transform.position.y);}
    static Material Mat(string name,Color c){var m=new Material(Shader.Find("FlatKit/Stylized Surface")){name=name};m.SetColor("_BaseColor",c);m.EnableKeyword("_CELPRIMARYMODE_SINGLE");AssetDatabase.CreateAsset(m,Folder+"/"+name+".mat");return m;}
    static void Box(Transform parent,string name,Vector3 p,Vector3 s,Material m){var g=GameObject.CreatePrimitive(PrimitiveType.Cube);g.name=name;g.transform.SetParent(parent,false);g.transform.localPosition=p;g.transform.localScale=s;g.GetComponent<Renderer>().sharedMaterial=m;}
}
