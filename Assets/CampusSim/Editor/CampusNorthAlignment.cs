using System;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;
using CampusSim;

public static class CampusNorthAlignment
{
    const string Dir = "Docs/MapResearch/Iterations/NorthAlignment";
    const string ScenePath = "Assets/CampusSim/Scenes/CampusTerrain.unity";
    [Serializable] public class State
    {
        public float yaw;
        public Vector3 mapPosition, terrainPosition, terrainSize;
        public int resolution;
        public float[] heights;
    }
    static Terrain Check()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode || SceneManager.GetActiveScene().path != ScenePath)
            throw new Exception("Open CampusTerrain in edit mode.");
        return UnityEngine.Object.FindObjectsByType<Terrain>(FindObjectsSortMode.None).Single();
    }
    [MenuItem("Campus/Map/Export North Alignment Source")]
    public static void Export()
    {
        var terrain=Check(); var map=GameObject.Find("INHA UNIVERSITY").transform;
        Directory.CreateDirectory(Dir);
        if (File.Exists(Dir+"/source.json")) throw new Exception("Alignment source exists; preserve checkpoint.");
        var data=terrain.terrainData; int n=data.heightmapResolution; var h=data.GetHeights(0,0,n,n);
        var s=new State { yaw=Mathf.DeltaAngle(0,map.eulerAngles.y),mapPosition=map.position,terrainPosition=terrain.transform.position,terrainSize=data.size,resolution=n,heights=new float[n*n] };
        for(int z=0;z<n;z++)for(int x=0;x<n;x++)s.heights[z*n+x]=h[z,x];
        File.WriteAllText(Dir+"/source.json",JsonUtility.ToJson(s));
        EditorSceneManager.SaveScene(SceneManager.GetActiveScene());
        File.Copy(ScenePath,Dir+"/scene-before.unity.txt");
        File.WriteAllText(Dir+"/checkpoint-note.txt","Scene snapshot references shared meshes. Restore source heights and scene, then Bake to restore derived geometry; this is not an isolated duplicate project.\n");
    }
    [MenuItem("Campus/Map/Apply North Alignment")]
    public static void Apply()
    {
        var terrain=Check();var map=GameObject.Find("INHA UNIVERSITY").transform;
        var source=JsonUtility.FromJson<State>(File.ReadAllText(Dir+"/source.json"));
        if(File.Exists(Dir+"/applied.txt"))throw new Exception("Alignment already applied.");
        if(Mathf.Abs(Mathf.DeltaAngle(source.yaw,map.eulerAngles.y))>.001f || map.position.sqrMagnitude>.000001f)
            throw new Exception("Map transform changed since export.");
        var data=terrain.terrainData;int n=data.heightmapResolution;
        var current=data.GetHeights(0,0,n,n);
        for(int z=0;z<n;z++)for(int x=0;x<n;x++)if(Mathf.Abs(current[z,x]-source.heights[z*n+x])>.000001f)throw new Exception("Terrain edited since alignment export.");
        var bytes=File.ReadAllBytes(Dir+"/aligned-heights-f32.raw");
        if(bytes.Length!=n*n*4)throw new Exception("Aligned height dimensions mismatch.");
        var heights=new float[n,n];for(int z=0;z<n;z++)for(int x=0;x<n;x++)
        {float value=BitConverter.ToSingle(bytes,(z*n+x)*4);if(float.IsNaN(value)||value<0||value>1)throw new Exception("Invalid aligned height.");heights[z,x]=value;}
        var rotation=Quaternion.Euler(0,-source.yaw,0);
        var scene=SceneManager.GetActiveScene();
        // Rotate all visual roots and cameras together. Unity Terrain itself stays axis-aligned.
        foreach(var root in scene.GetRootGameObjects())
        {
            if(root==terrain.gameObject)continue;
            Undo.RecordObject(root.transform,"Align campus north");
            root.transform.SetPositionAndRotation(rotation*root.transform.position,rotation*root.transform.rotation);
        }
        foreach(var root in scene.GetRootGameObjects())foreach(var b in root.GetComponentsInChildren<CampusTerrainBinding>(true))
        {
            b.samplePoint=rotation*b.samplePoint;b.originalPosition=rotation*b.originalPosition;EditorUtility.SetDirty(b);
        }
        data.SetHeights(0,0,heights);EditorUtility.SetDirty(data);
        CampusTerrainAuthoring.Bake();
        EditorSceneManager.SaveScene(scene);
        File.WriteAllText(Dir+"/applied.txt","North alignment applied. Map yaw="+Mathf.DeltaAngle(0,map.eulerAngles.y)+". Rotated authoring anchors and all visual roots; north-up terrain base rebuilt with transported sculpt residual. Legacy planar scale versus AEQD residual remains separately documented.\n");
        CampusTerrainAuthoring.Capture();
    }
    [MenuItem("Campus/Map/Refit North Aligned Building Pads")]
    public static void RefitPads() => PrepareOrApplyPads(true);
    [MenuItem("Campus/Map/Preview North Aligned Building Pads")]
    public static void PreviewPads() => PrepareOrApplyPads(false);
    static void PrepareOrApplyPads(bool apply)
    {
        var terrain=Check();var data=terrain.terrainData;int n=data.heightmapResolution;
        var heights=data.GetHeights(0,0,n,n);var origin=terrain.transform.position;
        if(!File.Exists(Dir+"/applied.txt"))throw new Exception("North alignment not applied.");
        if(File.Exists(Dir+"/pad-refit.txt"))throw new Exception("Pads already refitted; inspect before regenerating.");
        var original=(float[,])heights.Clone();
        if(apply)using(var file=new BinaryWriter(File.Create(Dir+"/pre-pad-heights-f32.raw")))
            for(int z=0;z<n;z++)for(int x=0;x<n;x++)file.Write(heights[z,x]);
        var report=new System.Text.StringBuilder("Synthetic building and sports pads after geographic realignment; not surveyed grades.\n");
        var all=UnityEngine.Object.FindObjectsByType<CampusTerrainBinding>(FindObjectsSortMode.None);
        foreach(var b in all.Where(b=>(b.kind==CampusTerrainBinding.BindingKind.Building&&!b.heightAnchor)||b.name.StartsWith("sport ")).OrderBy(b=>b.sourceId))
        {
            var filters=b.GetComponentsInChildren<MeshFilter>().Where(f=>!f.GetComponent<TextMesh>()&&f.name!="Terrain approach draft").ToArray();
            if(filters.Length==0)continue;
            var mask=new CampusVegetationFootprints(filters);
            var renderers=filters.Select(f=>f.GetComponent<Renderer>()).Where(r=>r).ToArray();if(renderers.Length==0)continue;
            var bounds=renderers[0].bounds;foreach(var r in renderers)bounds.Encapsulate(r.bounds);
            var sample=b.kind==CampusTerrainBinding.BindingKind.Building?b.samplePoint:bounds.center;
            float target=terrain.SampleHeight(sample)/data.size.y;int changed=0;
            for(int z=0;z<n;z++)for(int x=0;x<n;x++)
            {
                float wx=origin.x+x*data.size.x/(n-1),wz=origin.z+z*data.size.z/(n-1);
                if(wx<bounds.min.x-16||wx>bounds.max.x+16||wz<bounds.min.z-16||wz>bounds.max.z+16)continue;
                var p=new Vector2(wx,wz);if(!mask.Overlaps(p,16))continue;
                float blend=1;
                if(!mask.Overlaps(p,7))
                {float distance=16;for(float r=8;r<=16;r+=1)if(mask.Overlaps(p,r)){distance=r;break;}blend=1-Mathf.SmoothStep(0,1,(distance-7)/9);}
                heights[z,x]=Mathf.Lerp(heights[z,x],target,blend);changed++;
            }
            report.AppendLine(b.sourceId+": pad samples="+changed+", normalized height="+target);
        }
        int total=0;float maxChange=0;
        for(int z=0;z<n;z++)for(int x=0;x<n;x++){float delta=Mathf.Abs(heights[z,x]-original[z,x])*data.size.y;if(delta>.001f)total++;maxChange=Mathf.Max(maxChange,delta);}
        report.AppendLine("Unique changed samples="+total+"; maximum height change="+maxChange+"m. Footprint+7m level apron, transition to 16m. Field heights remain synthetic.");
        if(!apply)
        {
            File.WriteAllText(Dir+"/pad-preview.txt",report.ToString());
            using(var file=new BinaryWriter(File.Create(Dir+"/proposed-pad-heights-f32.raw")))for(int z=0;z<n;z++)for(int x=0;x<n;x++)file.Write(heights[z,x]);
            return;
        }
        data.SetHeights(0,0,heights);EditorUtility.SetDirty(data);
        CampusTerrainAuthoring.Bake();EditorSceneManager.SaveScene(SceneManager.GetActiveScene());
        File.WriteAllText(Dir+"/pad-refit.txt",report.ToString());
    }
}
