using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using CampusSim;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>Editor-only repair of exported asphalt footprints. Source assets remain intact.</summary>
public static class CampusRoadConnectionRepair
{
    const string Folder = "Assets/CampusSim/Generated/RoadSurfaceRepair";
    [Serializable] class Document { public int schema; public float roadOffset; public float sigma; public Part[] meshes; }
    [Serializable] class Part { public int index; public string name; public Vector3 anchor; public Vector3[] vertices; }
    [Serializable] class Snapshot { public MeshSnapshot[] meshes; }
    [Serializable] class MeshSnapshot { public string name; public Vector3[] source; public Vector3[] baked; public int[] triangles; public int[] bakedTriangles; }

    public static void ExportSnapshot(string stem)
    {
        var all=UnityEngine.Object.FindObjectsByType<CampusTerrainBinding>(FindObjectsSortMode.None)
            .Where(b=>b.gameObject.activeInHierarchy && b.originalMesh && b.bakedMesh &&
                (b.name.StartsWith("Road ") || b.name.Contains("network") || b.name=="Junction aware kerbs" ||
                 b.name=="Continuous yellow road edges" || b.name=="Crosswalks and lane dashes")).ToArray();
        File.WriteAllText("artifacts/road_connections/"+stem+"-meshes.json",JsonUtility.ToJson(new Snapshot {
            meshes=all.Select(b=>new MeshSnapshot {name=b.name,
                source=b.originalMesh.vertices.Select(b.transform.TransformPoint).ToArray(),
                baked=b.bakedMesh.vertices.Select(b.transform.TransformPoint).ToArray(),
                triangles=b.originalMesh.triangles,bakedTriangles=b.bakedMesh.triangles}).ToArray()}));
    }

    public static void Capture(string name, Vector3 center, float size)
    {
        var go=new GameObject("Temporary road verification camera") {hideFlags=HideFlags.HideAndDontSave};
        var camera=go.AddComponent<Camera>();camera.orthographic=true;camera.orthographicSize=size;
        camera.transform.position=center+Vector3.up*1000;camera.transform.rotation=Quaternion.Euler(90,0,0);
        camera.farClipPlane=2000;camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=new Color(.43f,.55f,.40f);
        var rt=new RenderTexture(1600,1000,24);var previous=RenderTexture.active;
        var texture=new Texture2D(1600,1000,TextureFormat.RGB24,false);
        try {camera.targetTexture=rt;camera.Render();RenderTexture.active=rt;texture.ReadPixels(new Rect(0,0,1600,1000),0,0);texture.Apply();
            File.WriteAllBytes("artifacts/road_connections/"+name+".png",texture.EncodeToPNG());}
        finally {camera.targetTexture=null;RenderTexture.active=previous;UnityEngine.Object.DestroyImmediate(go);UnityEngine.Object.DestroyImmediate(rt);UnityEngine.Object.DestroyImmediate(texture);}
    }

    [MenuItem("Campus/Map/Repair All Road Connections")]
    public static void Apply()
    {
        var scene = SceneManager.GetActiveScene();
        if (EditorApplication.isPlayingOrWillChangePlaymode || scene.path != "Assets/CampusSim/Scenes/CampusTerrain.unity")
            throw new InvalidOperationException("Open CampusTerrain in edit mode.");
        var document = JsonUtility.FromJson<Document>(File.ReadAllText("Assets/CampusSim/Data/road_surface_repairs.json"));
        if (document.schema != 1 || document.meshes == null) throw new InvalidOperationException("Invalid repair data.");
        var bindings = UnityEngine.Object.FindObjectsByType<CampusTerrainBinding>(FindObjectsSortMode.None)
            .Where(b => b.gameObject.scene == scene && b.gameObject.activeInHierarchy && b.originalMesh).ToArray();
        Directory.CreateDirectory(Folder);
        if(!AssetDatabase.IsValidFolder(Folder))AssetDatabase.Refresh();
        var seed=AssetDatabase.LoadAssetAtPath<TerrainData>("Assets/CampusSim/Generated/CampusTerrain.asset");
        var profile=AssetDatabase.LoadAssetAtPath<TerrainData>(Folder+"/RoadGradeProfile.asset");
        if(!profile){profile=UnityEngine.Object.Instantiate(seed);AssetDatabase.CreateAsset(profile,Folder+"/RoadGradeProfile.asset");}
        var heights=new float[seed.heightmapResolution,seed.heightmapResolution];
        using(var reader=new BinaryReader(File.OpenRead("Assets/CampusSim/Data/road_grade_profile.f32")))
            for(int z=0;z<seed.heightmapResolution;z++)for(int x=0;x<seed.heightmapResolution;x++)heights[z,x]=reader.ReadSingle();
        Undo.RecordObject(profile,"Constrain synthetic road grade");profile.SetHeights(0,0,heights);EditorUtility.SetDirty(profile);
        foreach (var part in document.meshes)
        {
            string path = Folder + "/Original_" + part.index + ".asset";
            var binding = bindings.Single(b => b.name == part.name &&
                (AssetDatabase.GetAssetPath(b.originalMesh) == path ||
                 Vector3.Distance(b.transform.TransformPoint(b.originalMesh.vertices[0]), part.anchor) < .005f));
            var original = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (!original) { original = new Mesh(); AssetDatabase.CreateAsset(original, path); }
            Undo.RecordObject(original, "Repair road footprint");
            original.Clear(); original.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            original.vertices = part.vertices.Select(binding.transform.InverseTransformPoint).ToArray();
            original.triangles = Enumerable.Range(0, part.vertices.Length).ToArray();
            original.uv = part.vertices.Select(p => new Vector2(p.x, p.z) * .1f).ToArray();
            original.RecalculateNormals(); original.RecalculateBounds();
            var baked = AssetDatabase.LoadAssetAtPath<Mesh>(Folder + "/Baked_" + part.index + ".asset");
            if (!baked) { baked = UnityEngine.Object.Instantiate(original); AssetDatabase.CreateAsset(baked, Folder + "/Baked_" + part.index + ".asset"); }
            Undo.RecordObject(binding, "Repair road surface references");
            Undo.RecordObject(binding.GetComponent<MeshFilter>(), "Repair road render mesh");
            var collider = binding.GetComponent<MeshCollider>();
            if (collider) Undo.RecordObject(collider, "Repair road collider");
            binding.originalMesh = original; binding.sourceMesh = original; binding.bakedMesh = baked;
            binding.GetComponent<MeshFilter>().sharedMesh = baked;
            if (collider) collider.sharedMesh = baked;
            if (part.name == "Continuous asphalt network" || part.name.StartsWith("Road "))
            { binding.surfaceOffset = document.roadOffset; binding.roadSmoothingMeters = document.sigma;
              binding.roadHeightSource=profile;binding.roadSupportSource=seed;binding.roadPlanarGridProfile=true; }
            EditorUtility.SetDirty(original); EditorUtility.SetDirty(binding);
        }
        // All road pieces now share the same vertical datum. Re-anchor the
        // previously restored Hitech link to that datum before the shared bake.
        var main = bindings.Single(b => b.name == "Continuous asphalt network");
        var terrain = scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<Terrain>()).Single();
        CampusRoadSurfaceSmoothing.Prepare(terrain);
        foreach (var b in bindings.Where(b => b.roadEndpointGrade))
        {
            Undo.RecordObject(b, "Match connection endpoint heights");
            var a = b.roadGradeStart; var c = b.roadGradeEnd;
            a.y = CampusRoadSurfaceSmoothing.Height(main, terrain, a);
            c.y = CampusRoadSurfaceSmoothing.Height(main, terrain, c);
            b.roadGradeStart = a; b.roadGradeEnd = c;
            EditorUtility.SetDirty(b);
        }
        CampusRoadSurfaceSmoothing.Apply();
        FitBorders();
        CampusRoadSurfaceSmoothing.UpdateSupport(terrain);
        CampusRoadGradeAudit.Export();
        CampusRoadSmoothingValidation.Validate();
        AssetDatabase.SaveAssets();
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        File.WriteAllText("artifacts/road_connections/applied.txt", "Road footprints, height datum, roadside transitions and paint applied through Unity MCP.\n");
        Debug.Log("ROAD_CONNECTION_REPAIR_COMPLETE");
    }

    public static void FitBorders()
    {
        var bindings = UnityEngine.Object.FindObjectsByType<CampusTerrainBinding>(FindObjectsSortMode.None)
            .Where(b => b.gameObject.activeInHierarchy && b.originalMesh && b.bakedMesh).ToArray();
        var roads = bindings.Where(b => b.name == "Continuous asphalt network" || b.name.StartsWith("Road ")).ToArray();
        var terrain = UnityEngine.Object.FindFirstObjectByType<Terrain>();
        var triangles = roads.SelectMany(b => {
            var v = b.bakedMesh.vertices.Select(b.transform.TransformPoint).ToArray();
            var t = b.bakedMesh.triangles;
            return Enumerable.Range(0, t.Length / 3).Select(i => new[] { v[t[3*i]], v[t[3*i+1]], v[t[3*i+2]] });
        }).ToArray();
        var cells=new Dictionary<Vector2Int,List<Vector3[]>>();
        foreach(var t in triangles)
        {
            int x0=Mathf.FloorToInt((t.Min(p=>p.x)-8)/8),x1=Mathf.FloorToInt((t.Max(p=>p.x)+8)/8);
            int z0=Mathf.FloorToInt((t.Min(p=>p.z)-8)/8),z1=Mathf.FloorToInt((t.Max(p=>p.z)+8)/8);
            for(int z=z0;z<=z1;z++)for(int x=x0;x<=x1;x++)
            {var key=new Vector2Int(x,z);if(!cells.TryGetValue(key,out var list))cells[key]=list=new List<Vector3[]>();list.Add(t);}
        }
        float Surface(Vector3 p, out float distance)
        {
            float best = float.MaxValue, y = 0;
            var q = new Vector2(p.x, p.z);
            if(!cells.TryGetValue(new Vector2Int(Mathf.FloorToInt(p.x/8),Mathf.FloorToInt(p.z/8)),out var nearby))
            {distance=100;return 0;}
            foreach (var t in nearby)
            {
                var a = new Vector2(t[0].x,t[0].z); var b = new Vector2(t[1].x,t[1].z); var c = new Vector2(t[2].x,t[2].z);
                float cross = (b.x-a.x)*(c.y-a.y)-(b.y-a.y)*(c.x-a.x);
                if (Mathf.Abs(cross)<1e-7f) continue;
                float u=((q.x-a.x)*(c.y-a.y)-(q.y-a.y)*(c.x-a.x))/cross;
                float v=((b.x-a.x)*(q.y-a.y)-(b.y-a.y)*(q.x-a.x))/cross;
                if(u>=0 && v>=0 && u+v<=1) { distance=0; return t[0].y+u*(t[1].y-t[0].y)+v*(t[2].y-t[0].y); }
                for(int j=0;j<3;j++)
                {
                    var start=t[j];var end=t[(j+1)%3];var delta=new Vector2(end.x-start.x,end.z-start.z);
                    float f=delta.sqrMagnitude>1e-10f?Mathf.Clamp01(Vector2.Dot(q-new Vector2(start.x,start.z),delta)/delta.sqrMagnitude):0;
                    var point=Vector3.Lerp(start,end,f);float d=(q-new Vector2(point.x,point.z)).sqrMagnitude;
                    if(d<best){best=d;y=point.y;}
                }
            }
            distance=Mathf.Sqrt(best);return y;
        }
        foreach(var b in bindings.Where(b => b.name=="Continuous pedestrian network" || b.name=="Junction aware kerbs" || b.name=="Continuous yellow road edges" || b.name=="Crosswalks and lane dashes"))
        {
            bool paint=b.name=="Continuous yellow road edges" || b.name=="Crosswalks and lane dashes";
            var source=b.name=="Crosswalks and lane dashes"?b.bakedMesh:b.originalMesh;
            var vertices=source.vertices;var mesh=b.bakedMesh;
            Undo.RecordObject(mesh,"Fit road borders to common road surface");
            for(int i=0;i<vertices.Length;i++)
            {
                var p=b.transform.TransformPoint(vertices[i]);float y=Surface(p,out float distance);
                if(paint) { if(distance<.35f)p.y=y+.015f; }
                else
                {
                    float relief=terrain.SampleHeight(p)+terrain.transform.position.y+b.surfaceOffset+p.y;
                    float lift=b.name=="Junction aware kerbs"?.14f:.06f;
                    p.y=Mathf.Lerp(y+lift,relief,Mathf.SmoothStep(0,1,Mathf.Clamp01((distance-.5f)/7.5f)));
                }
                vertices[i]=b.transform.InverseTransformPoint(p);
            }
            if(source!=mesh)
            {
                mesh.Clear();mesh.indexFormat=source.indexFormat;mesh.vertices=vertices;mesh.uv=source.uv;
                mesh.subMeshCount=source.subMeshCount;
                for(int s=0;s<source.subMeshCount;s++)mesh.SetTriangles(source.GetTriangles(s),s);
            }
            else mesh.vertices=vertices;
            mesh.RecalculateNormals();mesh.RecalculateBounds();
            var collider=b.GetComponent<MeshCollider>();if(collider){collider.sharedMesh=null;collider.sharedMesh=mesh;}
            EditorUtility.SetDirty(mesh);
        }
    }
}
