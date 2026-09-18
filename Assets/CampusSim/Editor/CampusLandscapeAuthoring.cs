using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;
using CampusSim;

public static class CampusLandscapeAuthoring
{
    [MenuItem("Campus/Map/Style Campus Grove Materials")]
    public static void Style()
    {
        var root=GameObject.Find("Campus planted groves");if(!root)throw new Exception("Plant groves first.");
        const string folder="Assets/CampusSim/Generated/Materials/";
        var leaf=AssetDatabase.LoadAssetAtPath<Material>(folder+"CampusLeaves.mat");
        var bark=AssetDatabase.LoadAssetAtPath<Material>(folder+"CampusBark.mat");
        if(!leaf)
        {
            leaf=new Material(Shader.Find("FlatKit/Stylized Surface")){name="Campus leaves"};
            leaf.SetColor("_BaseColor",new Color(.48f,.70f,.32f,1));
            leaf.SetTexture("_BaseMap",AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/Idyllic Fantasy Nature/Textures/Trees/BroadleafTree_Leaves.png"));
            leaf.SetFloat("_AlphaClip",1);leaf.SetFloat("_Cutoff",.35f);leaf.SetFloat("_Cull",0);
            leaf.EnableKeyword("_ALPHATEST_ON");leaf.EnableKeyword("_CELPRIMARYMODE_SINGLE");leaf.renderQueue=2450;
            leaf.SetOverrideTag("RenderType","TransparentCutout");AssetDatabase.CreateAsset(leaf,folder+"CampusLeaves.mat");
        }
        if(!bark)
        {
            bark=new Material(Shader.Find("FlatKit/Stylized Surface")){name="Campus bark"};bark.SetColor("_BaseColor",new Color(.32f,.25f,.17f));
            bark.EnableKeyword("_CELPRIMARYMODE_SINGLE");AssetDatabase.CreateAsset(bark,folder+"CampusBark.mat");
        }
        foreach(var renderer in root.GetComponentsInChildren<Renderer>())
        {
            renderer.sharedMaterials=renderer.sharedMaterials.Select(m=>m.name.Contains("Bark")||m.name.Contains("bark")?bark:leaf).ToArray();
        }
        AssetDatabase.SaveAssets();EditorSceneManager.SaveScene(SceneManager.GetActiveScene());
    }
    static float Cross(Vector2 a,Vector2 b)=>a.x*b.y-a.y*b.x;
    public static bool Inside(Vector2 p,Vector3[] vertices,int[] triangles)
    {
        for(int i=0;i<triangles.Length;i+=3)
        {
            Vector2 V(int j)=>new Vector2(vertices[triangles[i+j]].x,vertices[triangles[i+j]].z);
            var a=V(0);var b=V(1);var c=V(2);
            if(Mathf.Abs(Cross(b-a,c-a))<.0001f)continue;
            float x=Cross(b-a,p-a),y=Cross(c-b,p-b),z=Cross(a-c,p-c);
            if((x>=0&&y>=0&&z>=0)||(x<=0&&y<=0&&z<=0))return true;
        }
        return false;
    }
    [MenuItem("Campus/Map/Plant Campus Grove")]
    public static void Plant() => PlantCore(false);
    [MenuItem("Campus/Map/Densify Campus Grove")]
    public static void Densify() => PlantCore(true);
    static void PlantCore(bool dense)
    {
        if(SceneManager.GetActiveScene().path!="Assets/CampusSim/Scenes/CampusTerrain.unity" || EditorApplication.isPlayingOrWillChangePlaymode)throw new Exception("Open working scene in edit mode.");
        if(!dense && GameObject.Find("Campus planted groves"))throw new Exception("Groves already exist; preserve manual edits.");
        if(dense && GameObject.Find("Campus dense grove addition"))throw new Exception("Dense addition already exists; preserve edits.");
        var all=UnityEngine.Object.FindObjectsByType<CampusTerrainBinding>(FindObjectsSortMode.None);
        var pedestrianFootprints=new CampusVegetationFootprints(UnityEngine.Object.FindObjectsByType<CampusPedestrianArea>(FindObjectsSortMode.None)
            .Select(a=>a.GetComponent<MeshFilter>()).Where(f=>f)
            .Concat(UnityEngine.Object.FindObjectsByType<MeshCollider>(FindObjectsSortMode.None)
                .Where(c=>c.enabled && (c.name.Contains("Continuous asphalt") || c.name.Contains("Continuous pedestrian") || c.name.StartsWith("Road ")))
                .Select(c=>c.GetComponent<MeshFilter>()).Where(f=>f)));
        var exclusions=all.Where(b=>b.kind==CampusTerrainBinding.BindingKind.Building||b.kind==CampusTerrainBinding.BindingKind.Water)
            .SelectMany(b=>b.GetComponentsInChildren<Renderer>()).Select(r=>{var bounds=r.bounds;bounds.Expand(new Vector3(10,0,10));return bounds;}).ToArray();
        var prefabs=new[]{"BroadleafTree_01_Green","BroadleafTree_03_Green","BroadleafTree_04_Green"}
            .Select(n=>AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Idyllic Fantasy Nature/Prefabs/"+n+".prefab")).ToArray();
        if(prefabs.Any(p=>!p))throw new Exception("Idyllic Fantasy Nature tree prefab missing.");
        var terrain=UnityEngine.Object.FindObjectsByType<Terrain>(FindObjectsSortMode.None).Single();
        var root=new GameObject(dense?"Campus dense grove addition":"Campus planted groves");
        var existing = all.Where(b=>b.kind==CampusTerrainBinding.BindingKind.Vegetation).Select(b=>new Vector3(b.samplePoint.x,0,b.samplePoint.z)).ToList();
        var accepted=new List<Vector3>();var random=new System.Random(17092026);
        foreach(var green in all.Where(b=>b.kind==CampusTerrainBinding.BindingKind.Surface && (b.name.StartsWith("green ")||b.name.Contains("하와이"))).OrderBy(b=>b.sourceId))
        {
            var mesh=green.originalMesh?green.originalMesh:green.sourceMesh;
            var vertices=mesh.vertices.Select(v=>green.transform.TransformPoint(v)).ToArray();var triangles=mesh.triangles;
            float xmin=vertices.Min(v=>v.x),xmax=vertices.Max(v=>v.x),zmin=vertices.Min(v=>v.z),zmax=vertices.Max(v=>v.z);
            float spacing=dense?7:17, margin=dense?3:7;
            for(float z=zmin+margin;z<zmax-margin;z+=spacing)for(float x=xmin+margin;x<xmax-margin;x+=spacing)
            {
                var p=new Vector2(x+(float)random.NextDouble()*3-1.5f,z+(float)random.NextDouble()*3-1.5f);
                bool fits=true;
                for(int i=0;i<8;i++){float angle=i*Mathf.PI/4;if(!Inside(p+new Vector2(Mathf.Cos(angle),Mathf.Sin(angle))*(dense?2.5f:6),vertices,triangles)){fits=false;break;}}
                if(!fits || !Inside(p,vertices,triangles))continue;
                if(exclusions.Any(b=>p.x>b.min.x&&p.x<b.max.x&&p.y>b.min.z&&p.y<b.max.z))continue;
                // Keep a trunk/planting margin clear of authored walking surfaces.
                if(pedestrianFootprints.Overlaps(p,1.5f))continue;
                var point=new Vector3(p.x,0,p.y);
                if(accepted.Concat(existing).Any(v=>(v-point).sqrMagnitude<(dense?30.25f:225)))continue;
                var instance=(GameObject)PrefabUtility.InstantiatePrefab(prefabs[random.Next(prefabs.Length)],root.transform);
                instance.name="Grove tree "+accepted.Count.ToString("D3");
                instance.transform.rotation=Quaternion.Euler(0,(float)random.NextDouble()*360,0);
                var renderers=instance.GetComponentsInChildren<Renderer>();var bounds=renderers[0].bounds;
                foreach(var r in renderers)bounds.Encapsulate(r.bounds);
                float height=(dense?7:6)+(float)random.NextDouble()*(dense?4:2);
                instance.transform.localScale*=height/Mathf.Max(bounds.size.y,.1f);
                bounds=renderers[0].bounds;foreach(var r in renderers)bounds.Encapsulate(r.bounds);
                var binding=instance.AddComponent<CampusTerrainBinding>();binding.kind=CampusTerrainBinding.BindingKind.Vegetation;
                // Slight root burial avoids a visible air gap after terrain rebakes.
                binding.originalPosition=point+Vector3.up*(-.02f-bounds.min.y);binding.samplePoint=point;
                binding.sourceId="synthetic landscaping within "+green.sourceId;
                instance.transform.position=binding.originalPosition+Vector3.up*(terrain.SampleHeight(point)+terrain.transform.position.y);
                if(dense)
                {
                    var leaf=AssetDatabase.LoadAssetAtPath<Material>("Assets/CampusSim/Generated/Materials/CampusLeaves.mat");
                    var bark=AssetDatabase.LoadAssetAtPath<Material>("Assets/CampusSim/Generated/Materials/CampusBark.mat");
                    foreach(var renderer in renderers)renderer.sharedMaterials=renderer.sharedMaterials.Select(m=>m.name.IndexOf("bark",StringComparison.OrdinalIgnoreCase)>=0?bark:leaf).ToArray();
                }
                accepted.Add(point);
                if(accepted.Count>=(dense?650:110))goto Done;
            }
        }
        Done:
        EditorSceneManager.SaveScene(SceneManager.GetActiveScene());
        Directory.CreateDirectory("Docs/MapResearch/Iterations");
        File.WriteAllText("Docs/MapResearch/Iterations/"+(dense?"dense-groves.txt":"groves.txt"),"Idyllic Fantasy Nature added instances="+accepted.Count+"\nExisting vegetation="+existing.Count+"\nSeed=17092026; visual landscaping only, not surveyed tree positions.\nSampled green boundary margin="+(dense?2.5f:6)+"m; building/water bounds excluded. Route graph validation remains pending.\n");
        Debug.Log("Planted Idyllic Fantasy Nature grove trees: "+accepted.Count);
    }
}
