using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using CampusSim;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>Editor-only road profile baking. Source meshes and original DSM are preserved.</summary>
public static class CampusRoadSurfaceSmoothing
{
    const string ScenePath = "Assets/CampusSim/Scenes/CampusTerrain.unity";
    const string SupportPath = "Assets/CampusSim/Generated/CampusRoadSupportTerrain.asset";
    static readonly Dictionary<Vector2Int,float> RoadSamples = new Dictionary<Vector2Int,float>();
    const float SampleSpacing = 2;

    public static void Prepare(Terrain terrain)
    {
        RoadSamples.Clear();
        var roads=UnityEngine.Object.FindObjectsByType<CampusTerrainBinding>(FindObjectsSortMode.None)
            .Where(b=>b.gameObject.scene==terrain.gameObject.scene && b.roadHeightSource && b.roadSmoothingMeters>0);
        foreach(var road in roads)
        {
            var vertices=road.originalMesh.vertices.Select(road.transform.TransformPoint).ToArray();
            var indices=road.originalMesh.triangles;
            for(int i=0;i<indices.Length;i+=3)
            {
                var a=vertices[indices[i]];var b=vertices[indices[i+1]];var c=vertices[indices[i+2]];
                float cross=(b.x-a.x)*(c.z-a.z)-(b.z-a.z)*(c.x-a.x);
                if(Mathf.Abs(cross)<.00001f)continue;
                int x0=Mathf.FloorToInt(Mathf.Min(a.x,b.x,c.x)/SampleSpacing),x1=Mathf.CeilToInt(Mathf.Max(a.x,b.x,c.x)/SampleSpacing);
                int z0=Mathf.FloorToInt(Mathf.Min(a.z,b.z,c.z)/SampleSpacing),z1=Mathf.CeilToInt(Mathf.Max(a.z,b.z,c.z)/SampleSpacing);
                for(int z=z0;z<=z1;z++)for(int x=x0;x<=x1;x++)
                {
                    var key=new Vector2Int(x,z);if(RoadSamples.ContainsKey(key))continue;
                    float px=x*SampleSpacing-a.x,pz=z*SampleSpacing-a.z;
                    float u=(px*(c.z-a.z)-pz*(c.x-a.x))/cross;
                    float v=((b.x-a.x)*pz-(b.z-a.z)*px)/cross;
                    if(u<-.0001f||v<-.0001f||u+v>1.0001f)continue;
                    var origin=terrain.transform.position;var data=road.roadHeightSource;
                    RoadSamples[key]=data.GetInterpolatedHeight((x*SampleSpacing-origin.x)/data.size.x,
                        (z*SampleSpacing-origin.z)/data.size.z);
                }
            }
        }
    }

    public static float Height(CampusTerrainBinding binding, Terrain terrain, Vector3 world)
    {
        var data = binding.roadHeightSource;
        var origin = terrain.transform.position;
        if(RoadSamples.Count==0)Prepare(terrain);
        double sum=0,weights=0,sx=0,sz=0,sxx=0,szz=0,sxz=0,sxy=0,szy=0;
        float sigma=binding.roadSmoothingMeters,radius=sigma*3;
        // Fit a local plane to road-only samples. Adjacent building pads and hills cannot
        // imprint cross slopes on the carriageway; linear grades are reproduced exactly.
        for(int z=Mathf.CeilToInt((world.z-radius)/SampleSpacing);z<=Mathf.FloorToInt((world.z+radius)/SampleSpacing);z++)
        for(int x=Mathf.CeilToInt((world.x-radius)/SampleSpacing);x<=Mathf.FloorToInt((world.x+radius)/SampleSpacing);x++)
        {
            if(!RoadSamples.TryGetValue(new Vector2Int(x,z),out float y))continue;
            double dx=x*SampleSpacing-world.x,dz=z*SampleSpacing-world.z;
            double distanceSquared=dx*dx+dz*dz;
            double taper=Math.Max(0,1-distanceSquared/(radius*radius));
            // Compact smooth support avoids a height step when a sample crosses the window edge.
            double w=Math.Exp(-distanceSquared/(2*sigma*sigma))*taper*taper*taper*taper;
            weights+=w;sum+=w*y;sx+=w*dx;sz+=w*dz;sxx+=w*dx*dx;szz+=w*dz*dz;
            sxz+=w*dx*dz;sxy+=w*dx*y;szy+=w*dz*y;
        }
        double height=weights>0?sum/weights:data.GetInterpolatedHeight(
            (world.x-origin.x)/data.size.x,(world.z-origin.z)/data.size.z);
        if(weights>0)
        {
            double xx=sxx-sx*sx/weights,zz=szz-sz*sz/weights,xz=sxz-sx*sz/weights;
            double xy=sxy-sx*sum/weights,zy=szy-sz*sum/weights,det=xx*zz-xz*xz;
            if(det>1e-8)height-=((xy*zz-zy*xz)*sx+(zy*xx-xy*xz)*sz)/(det*weights);
        }
        // The source road is a horizontal sheet; do not transfer decorative local relief.
        float baseY = binding.transform.TransformPoint(binding.originalMesh.bounds.center).y;
        return (float)height + origin.y + baseY + binding.surfaceOffset;
    }

    [MenuItem("Campus/Map/Smooth Driving Road Surfaces")]
    public static void Apply()
    {
        var scene = SceneManager.GetActiveScene();
        if (scene.path != ScenePath || EditorApplication.isPlayingOrWillChangePlaymode)
            throw new InvalidOperationException("Open CampusTerrain in edit mode first.");
        var terrain = scene.GetRootGameObjects().SelectMany(g=>g.GetComponentsInChildren<Terrain>()).Single();
        var roads = scene.GetRootGameObjects().SelectMany(g=>g.GetComponentsInChildren<CampusTerrainBinding>())
            .Where(b=>b.gameObject.activeInHierarchy && (b.name=="Continuous asphalt network" || b.name.StartsWith("Road "))).ToArray();
        if(roads.Length==0) throw new InvalidOperationException("No active roads.");
        foreach(var road in roads)
            if(!road.originalMesh || !road.bakedMesh || !road.GetComponent<MeshCollider>() ||
                !AssetDatabase.GetAssetPath(road.bakedMesh).StartsWith("Assets/CampusSim/Generated/"))
                throw new InvalidOperationException("Expected generated road with original mesh: "+road.name);
        var original = roads.Select(b=>b.roadHeightSource).FirstOrDefault(d=>d);
        if(!original) original=terrain.terrainData;
        if(AssetDatabase.GetAssetPath(original)==SupportPath)
            throw new InvalidOperationException("Original DSM reference missing; refusing cumulative smoothing.");
        var support = AssetDatabase.LoadAssetAtPath<TerrainData>(SupportPath);
        if(!support)
        {
            support=UnityEngine.Object.Instantiate(original);
            AssetDatabase.CreateAsset(support,SupportPath);
        }
        Undo.RecordObject(terrain,"Road support terrain");
        Undo.RecordObject(terrain.GetComponent<TerrainCollider>(),"Road support terrain collider");
        terrain.terrainData=support;
        terrain.GetComponent<TerrainCollider>().terrainData=support;
        foreach(var road in roads)
        {
            Undo.RecordObject(road,"Smooth road profile");
            road.roadHeightSource=original;
            road.roadSmoothingMeters=8;
        }
        Prepare(terrain);
        foreach(var road in roads)
        {
            var refined=CampusTerrainAuthoring.Subdivide(road.originalMesh,road.transform,terrain);
            var mesh=road.bakedMesh;
            Undo.RecordObject(mesh,"Smooth generated road mesh");
            var vertices=refined.vertices;
            for(int i=0;i<vertices.Length;i++)
            {
                var p=road.transform.TransformPoint(vertices[i]);
                p.y=Height(road,terrain,p);
                vertices[i]=road.transform.InverseTransformPoint(p);
            }
            mesh.Clear();mesh.indexFormat=refined.indexFormat;
            mesh.vertices=vertices;mesh.uv=refined.uv;mesh.subMeshCount=refined.subMeshCount;
            for(int s=0;s<refined.subMeshCount;s++)mesh.SetTriangles(refined.GetTriangles(s),s);
            UnityEngine.Object.DestroyImmediate(refined);
            mesh.RecalculateNormals();mesh.RecalculateBounds();
            var collider=road.GetComponent<MeshCollider>();
            collider.sharedMesh=null;collider.sharedMesh=mesh;
            EditorUtility.SetDirty(mesh);EditorUtility.SetDirty(road);
        }
        UpdateSupport(terrain);
        CampusTerrainAuthoring.FitRoadPaint();
        EditorUtility.SetDirty(terrain);EditorUtility.SetDirty(terrain.GetComponent<TerrainCollider>());
        EditorSceneManager.MarkSceneDirty(scene);
        AssetDatabase.SaveAssets();
        EditorSceneManager.SaveScene(scene);
        Debug.Log("ROAD_SMOOTHING applied roads="+roads.Length+" sigma=8m; source DSM/meshes preserved. Geometry is synthetic/unverified.");
    }

    public static void UpdateSupport(Terrain terrain)
    {
        var roads=UnityEngine.Object.FindObjectsByType<CampusTerrainBinding>(FindObjectsSortMode.None)
            .Where(b=>b.gameObject.scene==terrain.gameObject.scene && b.roadHeightSource && b.roadSmoothingMeters>0).ToArray();
        if(roads.Length==0) return;
        if(AssetDatabase.GetAssetPath(terrain.terrainData)!=SupportPath)
            throw new InvalidOperationException("Road support must use the generated terrain copy.");
        var data=terrain.terrainData;
        int n=data.heightmapResolution;
        var heights=roads[0].roadHeightSource.GetHeights(0,0,n,n);
        var origin=terrain.transform.position;
        float cellX=data.size.x/(n-1),cellZ=data.size.z/(n-1);
        foreach(var road in roads)
        {
            var vertices=road.bakedMesh.vertices.Select(road.transform.TransformPoint).ToArray();
            var indices=road.bakedMesh.triangles;
            for(int i=0;i<indices.Length;i+=3)
            {
                var a=vertices[indices[i]];var b=vertices[indices[i+1]];var c=vertices[indices[i+2]];
                int x0=Mathf.Clamp(Mathf.FloorToInt((Mathf.Min(a.x,b.x,c.x)-origin.x)/cellX),0,n-2);
                int x1=Mathf.Clamp(Mathf.FloorToInt((Mathf.Max(a.x,b.x,c.x)-origin.x)/cellX)+1,1,n-1);
                int z0=Mathf.Clamp(Mathf.FloorToInt((Mathf.Min(a.z,b.z,c.z)-origin.z)/cellZ),0,n-2);
                int z1=Mathf.Clamp(Mathf.FloorToInt((Mathf.Max(a.z,b.z,c.z)-origin.z)/cellZ)+1,1,n-1);
                float ceiling=(Mathf.Min(a.y,b.y,c.y)-.05f-origin.y)/data.size.y;
                // Conservative support cells: every corner stays below the whole triangle.
                // This also prevents the TerrainCollider from protruding through the asphalt.
                for(int z=z0;z<=z1;z++)for(int x=x0;x<=x1;x++)
                    heights[z,x]=Mathf.Min(heights[z,x],Mathf.Clamp01(ceiling));
            }
        }
        Undo.RecordObject(data,"Fit terrain below smoothed roads");
        data.SetHeights(0,0,heights);EditorUtility.SetDirty(data);
        Directory.CreateDirectory("artifacts/road_smoothing");
    }
}
