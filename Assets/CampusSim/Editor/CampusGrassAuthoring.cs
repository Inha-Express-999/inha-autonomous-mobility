using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;
using CampusSim;

public static class CampusGrassAuthoring
{
    const string Folder="Assets/CampusSim/Generated/DenseGrass";
    [MenuItem("Campus/Map/Balance Grass Palette")]
    public static void Balance()
    {
        var terrain=UnityEngine.Object.FindObjectsByType<Terrain>(FindObjectsSortMode.None).Single();
        foreach(var layer in terrain.terrainData.terrainLayers)
        {
            if(!AssetDatabase.GetAssetPath(layer).StartsWith("Assets/CampusSim/"))continue;
            layer.diffuseTexture=AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/CampusSim/Generated/Materials/GrassColor.asset");
            EditorUtility.SetDirty(layer);
        }
        var active=GameObject.Find("Campus dense grass cover");
        var grass=active?active.GetComponentInChildren<MeshRenderer>().sharedMaterial:AssetDatabase.LoadAssetAtPath<Material>(Folder+"/Grass.mat");
        grass.SetColor("_ColorDim",new Color(.31f,.45f,.20f));grass.SetColor("_BaseColor",new Color(.44f,.59f,.25f));grass.SetFloat("_ShadowEdgeSize",.25f);EditorUtility.SetDirty(grass);
        AssetDatabase.SaveAssets();EditorSceneManager.SaveScene(SceneManager.GetActiveScene());
    }
    [MenuItem("Campus/Map/Cover Greens With Idyllic Grass")]
    public static void Build() => BuildCore(false);
    [MenuItem("Campus/Map/Refine Grass Building Footprints")]
    public static void Refine() => BuildCore(true);
    static void BuildCore(bool refined)
    {
        string Folder=refined?"Assets/CampusSim/Generated/RefinedGrass":"Assets/CampusSim/Generated/DenseGrass";
        if(EditorApplication.isPlayingOrWillChangePlaymode||SceneManager.GetActiveScene().path!="Assets/CampusSim/Scenes/CampusTerrain.unity")throw new Exception("Open working scene.");
        var previous=GameObject.Find("Campus dense grass cover");
        if(!refined&&previous)throw new Exception("Grass cover already exists; preserve edits.");
        if(refined&&Directory.Exists(Folder))throw new Exception("Refined output already exists; inspect it before regenerating.");
        Directory.CreateDirectory(Folder);AssetDatabase.Refresh();
        var leaf=AssetDatabase.LoadAssetAtPath<Material>("Assets/CampusSim/Generated/Materials/CampusLeaves.mat");
        var grass=new Material(leaf){name="Idyllic grass campus"};grass.SetTexture("_BaseMap",AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/Idyllic Fantasy Nature/Textures/Grass/Grass_01.png"));grass.SetColor("_BaseColor",new Color(.44f,.59f,.25f));grass.SetFloat("_Cutoff",.35f);grass.SetColor("_ColorDim",new Color(.28f,.40f,.18f));AssetDatabase.CreateAsset(grass,Folder+"/Grass.mat");
        var ground=new Material(Shader.Find("FlatKit/Stylized Surface")){name="Soft campus lawn"};ground.EnableKeyword("_CELPRIMARYMODE_SINGLE");ground.SetColor("_BaseColor",new Color(.46f,.59f,.32f));ground.SetColor("_ColorDim",new Color(.37f,.48f,.27f));ground.SetTexture("_BaseMap",AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/Idyllic Fantasy Nature/Textures/Ground/Grass/Grass_Albedo.png"));ground.SetFloat("_TextureImpact",.18f);ground.SetFloat("_ShadowEdgeSize",.2f);AssetDatabase.CreateAsset(ground,Folder+"/Lawn.mat");
        var prefab=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Idyllic Fantasy Nature/Prefabs/Grass_01.prefab");var blade=prefab.GetComponent<MeshFilter>().sharedMesh;
        var all=UnityEngine.Object.FindObjectsByType<CampusTerrainBinding>(FindObjectsSortMode.None);
        var forbidden=all.Where(b=>b.kind==CampusTerrainBinding.BindingKind.Building).SelectMany(b=>b.GetComponentsInChildren<Renderer>()).Select(r=>r.bounds).ToArray();
        var footprints=refined?new CampusVegetationFootprints(all.Where(b=>b.kind==CampusTerrainBinding.BindingKind.Building||b.kind==CampusTerrainBinding.BindingKind.Water).SelectMany(b=>b.GetComponentsInChildren<MeshFilter>())):null;
        var entrances=UnityEngine.Object.FindObjectsByType<CampusLandmarkEntrances>(FindObjectsSortMode.None).SelectMany(l=>new[]{l.generalEntrance,l.accessibleEntrance}).Where(t=>t).Select(t=>t).ToArray();
        var pedestrianFootprints=new CampusVegetationFootprints(UnityEngine.Object.FindObjectsByType<CampusPedestrianArea>(FindObjectsSortMode.None)
            .Select(a=>a.GetComponent<MeshFilter>()).Where(f=>f)
            .Concat(UnityEngine.Object.FindObjectsByType<MeshCollider>(FindObjectsSortMode.None)
                .Where(c=>c.name=="Continuous asphalt network"||c.name=="Continuous pedestrian network"||c.name.StartsWith("Road "))
                .Select(c=>c.GetComponent<MeshFilter>()).Where(f=>f)));
        var root=new GameObject(refined?"Campus refined grass staging":"Campus dense grass cover").transform;var random=new System.Random(20260917);int count=0,chunks=0;
        var coverage=new System.Text.StringBuilder("Per-green accepted grass clumps; refined projected building/water footprints.\n");
        foreach(var green in all.Where(b=>b.kind==CampusTerrainBinding.BindingKind.Surface&&b.name.StartsWith("green ")).OrderBy(b=>b.sourceId))
        {
            int countBefore=count;
            green.GetComponent<Renderer>().sharedMaterial=ground;
            var source=green.originalMesh?green.originalMesh:green.sourceMesh;var vertices=source.vertices.Select(green.transform.TransformPoint).ToArray();var triangles=source.triangles;
            var instances=new List<CombineInstance>();
            void Flush()
            {
                if(instances.Count==0)return;
                var mesh=new Mesh{name="Grass patch "+chunks,indexFormat=UnityEngine.Rendering.IndexFormat.UInt32};mesh.CombineMeshes(instances.ToArray(),true,true);mesh.RecalculateBounds();AssetDatabase.CreateAsset(mesh,Folder+"/Source_"+chunks+".asset");
                var baked=UnityEngine.Object.Instantiate(mesh);AssetDatabase.CreateAsset(baked,Folder+"/Baked_"+chunks+".asset");
                var g=new GameObject("Grass patch "+chunks);g.transform.SetParent(root,false);g.AddComponent<MeshFilter>().sharedMesh=baked;var r=g.AddComponent<MeshRenderer>();r.sharedMaterial=grass;r.shadowCastingMode=UnityEngine.Rendering.ShadowCastingMode.Off;
                var b=g.AddComponent<CampusTerrainBinding>();b.kind=CampusTerrainBinding.BindingKind.Surface;b.sourceMesh=mesh;b.bakedMesh=baked;b.surfaceOffset=.17f;b.sourceId="synthetic grass on "+green.sourceId;
                chunks++;instances.Clear();
            }
            for(float z=vertices.Min(v=>v.z)+.7f;z<vertices.Max(v=>v.z);z+=.8f)for(float x=vertices.Min(v=>v.x)+.7f;x<vertices.Max(v=>v.x);x+=.8f)
            {
                var p=new Vector2(x+(float)random.NextDouble()*.78f-.39f,z+(float)random.NextDouble()*.78f-.39f);
                if(!CampusLandscapeAuthoring.Inside(p,vertices,triangles))continue;
                if(!refined&&forbidden.Any(b=>p.x>b.min.x-1&&p.x<b.max.x+1&&p.y>b.min.z-1&&p.y<b.max.z+1))continue;
                if(entrances.Any(e=>{var q=e.InverseTransformPoint(new Vector3(p.x,e.position.y,p.y));return Mathf.Abs(q.x)<2.5f&&q.z> -1&&q.z<9;}))continue;
                float scale=1.1f+(float)random.NextDouble()*.7f;
                var rotation=Quaternion.Euler(0,(float)random.NextDouble()*360,0);
                var matrix=Matrix4x4.TRS(new Vector3(p.x,-blade.bounds.min.y*scale,p.y),rotation,Vector3.one*scale);
                // Check the full rotated footprint, including margin samples, instead of accepting the center alone.
                float radius=new Vector2(Mathf.Max(Mathf.Abs(blade.bounds.min.x),Mathf.Abs(blade.bounds.max.x)),Mathf.Max(Mathf.Abs(blade.bounds.min.z),Mathf.Abs(blade.bounds.max.z))).magnitude*scale;
                if(refined&&footprints.Overlaps(p,radius+.2f))continue;
                if(pedestrianFootprints.Overlaps(p,radius+.2f))continue;
                bool fits=true;
                for(int k=0;k<24;k++){float a=k*Mathf.PI/12;if(!CampusLandscapeAuthoring.Inside(p+new Vector2(Mathf.Cos(a),Mathf.Sin(a))*(radius+.15f),vertices,triangles)){fits=false;break;}}
                if(!fits)continue;
                foreach(var v in blade.vertices){var w=matrix.MultiplyPoint3x4(v);if(!CampusLandscapeAuthoring.Inside(new Vector2(w.x,w.z),vertices,triangles)){fits=false;break;}}
                if(!fits)continue;
                instances.Add(new CombineInstance{mesh=blade,transform=matrix});count++;
                if(instances.Count>=128)Flush();
            }
            Flush();
            coverage.AppendLine(green.sourceId+": "+(count-countBefore));
        }
        var terrain=UnityEngine.Object.FindObjectsByType<Terrain>(FindObjectsSortMode.None).Single();
        foreach(var layer in terrain.terrainData.terrainLayers){if(!AssetDatabase.GetAssetPath(layer).StartsWith("Assets/CampusSim/"))continue;layer.diffuseTexture=AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/CampusSim/Generated/Materials/GrassColor.asset");layer.tileSize=new Vector2(18,18);EditorUtility.SetDirty(layer);}
        var old=GameObject.Find("Campus grass cover");if(old)old.SetActive(false);
        if(refined&&previous){previous.name="Campus dense grass cover | prior bounds mask";previous.SetActive(false);}
        root.name="Campus dense grass cover";
        root.gameObject.AddComponent<CampusVegetationQuality>();
        CampusTerrainAuthoring.Bake();AssetDatabase.SaveAssets();EditorSceneManager.SaveScene(SceneManager.GetActiveScene());
        File.WriteAllText("Docs/MapResearch/Iterations/grass-green-coverage.txt",coverage.ToString());
        File.WriteAllText("Docs/MapResearch/Iterations/grass-cover.txt","Idyllic Grass_01 clumps="+count+"; combined renderer patches="+chunks+". Spacing=0.8m with full-cell random jitter, scale=1.1..1.8. All blade vertices and 24 footprint envelope samples checked inside source green. Exclusion mask="+(refined?"projected building/water mesh triangles":"building bounds")+" plus entrance corridors. Terrain Bake updates grass vertices. Mobile profile not yet benchmarked.\n");
    }
}
