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
    static readonly Dictionary<Vector3Int,float> FittedHeights = new Dictionary<Vector3Int,float>();
    const float SampleSpacing = 2;

    public static void Prepare(Terrain terrain)
    {
        RoadSamples.Clear();
        FittedHeights.Clear();
        var roads=UnityEngine.Object.FindObjectsByType<CampusTerrainBinding>(FindObjectsSortMode.None)
            .Where(b=>b.gameObject.scene==terrain.gameObject.scene && b.roadHeightSource && b.roadSmoothingMeters>0 && !b.roadEndpointGrade && !b.roadPlanarGridProfile);
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
        if(binding.roadEndpointGrade)
        {
            var a=binding.roadGradeStart; var b=binding.roadGradeEnd;
            var delta=new Vector2(b.x-a.x,b.z-a.z);
            if(delta.sqrMagnitude<.01f)throw new InvalidOperationException("Invalid endpoint road grade");
            float t=Vector2.Dot(new Vector2(world.x-a.x,world.z-a.z),delta)/delta.sqrMagnitude;
            return Mathf.Lerp(a.y,b.y,Mathf.Clamp01(t));
        }
        var origin = terrain.transform.position;
        if(binding.roadPlanarGridProfile)
        {
            var data=binding.roadHeightSource;int n=data.heightmapResolution;
            float gx=(world.x-origin.x)*(n-1)/data.size.x,gz=(world.z-origin.z)*(n-1)/data.size.z;
            int x=Mathf.Clamp(Mathf.FloorToInt(gx),0,n-2),z=Mathf.Clamp(Mathf.FloorToInt(gz),0,n-2);
            float u=Mathf.Clamp01(gx-x),v=Mathf.Clamp01(gz-z);
            float a=data.GetHeight(x,z),b=data.GetHeight(x+1,z),c=data.GetHeight(x,z+1),d=data.GetHeight(x+1,z+1);
            float h=u+v<=1 ? a+(b-a)*u+(c-a)*v : d+(c-d)*(1-u)+(b-d)*(1-v);
            return h+origin.y+binding.surfaceOffset;
        }
        if(RoadSamples.Count==0)Prepare(terrain);
        var fit=new RoadHeightFit();
        float sigma=binding.roadSmoothingMeters,radius=sigma*3;
        var cacheKey=new Vector3Int(Mathf.RoundToInt(world.x*1000),Mathf.RoundToInt(world.z*1000),Mathf.RoundToInt(sigma*1000));
        float baseY = binding.transform.TransformPoint(binding.originalMesh.bounds.center).y;
        if(FittedHeights.TryGetValue(cacheKey,out float cachedHeight))
            return cachedHeight+origin.y+baseY+binding.surfaceOffset;
        // Fit a local plane to road-only samples. Adjacent building pads and hills cannot
        // imprint cross slopes on the carriageway; linear grades are reproduced exactly.
        for(int z=Mathf.CeilToInt((world.z-radius)/SampleSpacing);z<=Mathf.FloorToInt((world.z+radius)/SampleSpacing);z++)
        for(int x=Mathf.CeilToInt((world.x-radius)/SampleSpacing);x<=Mathf.FloorToInt((world.x+radius)/SampleSpacing);x++)
        {
            if(!RoadSamples.TryGetValue(new Vector2Int(x,z),out float y))continue;
            double dx=x*SampleSpacing-world.x,dz=z*SampleSpacing-world.z;
            fit.Add(dx,dz,y,sigma);
        }
        if(!fit.TryHeight(out double height))
            throw new InvalidOperationException("Insufficient road height samples at "+world);
        // The source road is a horizontal sheet; do not transfer decorative local relief.
        FittedHeights[cacheKey]=(float)height;
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
            if(!road.roadHeightSource)road.roadHeightSource=original;
            if(road.roadSmoothingMeters<=0) road.roadSmoothingMeters=8;
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
        Debug.Log("ROAD_SMOOTHING applied roads="+roads.Length+"; per-road smoothing distances retained; source DSM/meshes preserved. Geometry is synthetic/unverified.");
    }

    public static void UpdateSupport(Terrain terrain)
    {
        var roads=UnityEngine.Object.FindObjectsByType<CampusTerrainBinding>(FindObjectsSortMode.None)
            .Where(b=>b.gameObject.scene==terrain.gameObject.scene && b.gameObject.activeInHierarchy && b.roadHeightSource && b.roadSmoothingMeters>0).ToArray();
        if(roads.Length==0) return;
        if(AssetDatabase.GetAssetPath(terrain.terrainData)!=SupportPath)
            throw new InvalidOperationException("Road support must use the generated terrain copy.");
        var data=terrain.terrainData;
        int n=data.heightmapResolution;
        var supportSource=roads.Select(b=>b.roadSupportSource).FirstOrDefault(d=>d);
        if(!supportSource)supportSource=roads[0].roadHeightSource;
        var heights=supportSource.GetHeights(0,0,n,n);
        var ceilings=new float[n,n];
        var roadCells=new bool[n,n];
        for(int z=0;z<n;z++)for(int x=0;x<n;x++)ceilings[z,x]=float.PositiveInfinity;
        var borders=UnityEngine.Object.FindObjectsByType<CampusTerrainBinding>(FindObjectsSortMode.None)
            .Where(b=>b.gameObject.scene==terrain.gameObject.scene && b.gameObject.activeInHierarchy && b.originalMesh && b.bakedMesh &&
                (b.name=="Continuous pedestrian network" || b.name=="Junction aware kerbs") &&
                AssetDatabase.GetAssetPath(b.originalMesh).StartsWith("Assets/CampusSim/Generated/RoadSurfaceRepair/"));
        roads=roads.Concat(borders).ToArray();
        var origin=terrain.transform.position;
        float cellX=data.size.x/(n-1),cellZ=data.size.z/(n-1);
        foreach(var road in roads)
        {
            bool asphalt=road.roadHeightSource && road.roadSmoothingMeters>0;
            var vertices=road.bakedMesh.vertices.Select(road.transform.TransformPoint).ToArray();
            var indices=road.bakedMesh.triangles;
            for(int i=0;i<indices.Length;i+=3)
            {
                var a=vertices[indices[i]];var b=vertices[indices[i+1]];var c=vertices[indices[i+2]];
                int x0=Mathf.Clamp(Mathf.FloorToInt((Mathf.Min(a.x,b.x,c.x)-origin.x)/cellX),0,n-2);
                int x1=Mathf.Clamp(Mathf.FloorToInt((Mathf.Max(a.x,b.x,c.x)-origin.x)/cellX)+1,1,n-1);
                int z0=Mathf.Clamp(Mathf.FloorToInt((Mathf.Min(a.z,b.z,c.z)-origin.z)/cellZ),0,n-2);
                int z1=Mathf.Clamp(Mathf.FloorToInt((Mathf.Max(a.z,b.z,c.z)-origin.z)/cellZ)+1,1,n-1);
                // Clip before calculating the ceiling. A long roadside triangle's
                // lowest vertex must not excavate its entire bounding rectangle.
                for(int z=z0;z<z1;z++)for(int x=x0;x<x1;x++)
                {
                    var polygon=new List<Vector3>{a,b,c};
                    polygon=ClipSupport(polygon,0,origin.x+x*cellX,true);
                    polygon=ClipSupport(polygon,0,origin.x+(x+1)*cellX,false);
                    polygon=ClipSupport(polygon,2,origin.z+z*cellZ,true);
                    polygon=ClipSupport(polygon,2,origin.z+(z+1)*cellZ,false);
                    if(polygon.Count<3)continue;
                    float ceiling=Mathf.Clamp01((polygon.Min(p=>p.y)-.05f-origin.y)/data.size.y);
                    for(int dz=0;dz<=1;dz++)for(int dx=0;dx<=1;dx++)
                    {
                        // Low decorative roadside geometry must not excavate
                        // the asphalt foundation at their shared grid corners.
                        if(!asphalt && roadCells[z+dz,x+dx])continue;
                        ceilings[z+dz,x+dx]=Mathf.Min(ceilings[z+dz,x+dx],ceiling);
                        if(asphalt)roadCells[z+dz,x+dx]=true;
                    }
                }
            }
        }
        // Cut AND fill from the original DSM. A min(original, road) operation
        // leaves elevated profile sections hanging metres above the ground.
        // Blend a narrow shoulder outside the constrained cells; never smooth
        // back into them, which would reintroduce TerrainCollider penetration.
        const float shoulderMeters=8f; // Synthetic authoring transition, not surveyed earthworks.
        int rx=Mathf.CeilToInt(shoulderMeters/cellX),rz=Mathf.CeilToInt(shoulderMeters/cellZ);
        var weights=new float[n,n];var sums=new float[n,n];
        for(int z=0;z<n;z++)for(int x=0;x<n;x++)
        {
            if(float.IsPositiveInfinity(ceilings[z,x]))continue;
            heights[z,x]=ceilings[z,x];
            for(int dz=-rz;dz<=rz;dz++)for(int dx=-rx;dx<=rx;dx++)
            {
                int xx=x+dx,zz=z+dz;
                if(xx<0||xx>=n||zz<0||zz>=n||!float.IsPositiveInfinity(ceilings[zz,xx]))continue;
                float distance=Mathf.Sqrt(dx*dx*cellX*cellX+dz*dz*cellZ*cellZ);
                if(distance>=shoulderMeters)continue;
                float w=1-distance/shoulderMeters;w=w*w*(3-2*w);
                sums[zz,xx]+=ceilings[z,x]*w;weights[zz,xx]+=w;
            }
        }
        for(int z=0;z<n;z++)for(int x=0;x<n;x++)
            if(weights[z,x]>0)heights[z,x]=Mathf.Lerp(heights[z,x],sums[z,x]/weights[z,x],Mathf.Min(1,weights[z,x]));
        Undo.RecordObject(data,"Fit terrain below smoothed roads");
        data.SetHeights(0,0,heights);EditorUtility.SetDirty(data);
        terrain.Flush();
        CampusGarageAuthoring.FitVehicleSpawnsToRoad();
        Directory.CreateDirectory("artifacts/road_smoothing");
    }

    static List<Vector3> ClipSupport(List<Vector3> input,int axis,float boundary,bool greater)
    {
        var output=new List<Vector3>();
        if(input.Count==0)return output;
        var previous=input[input.Count-1];
        bool previousInside=greater?previous[axis]>=boundary:previous[axis]<=boundary;
        foreach(var current in input)
        {
            bool inside=greater?current[axis]>=boundary:current[axis]<=boundary;
            if(inside!=previousInside)
                output.Add(Vector3.LerpUnclamped(previous,current,(boundary-previous[axis])/(current[axis]-previous[axis])));
            if(inside)output.Add(current);
            previous=current;previousInside=inside;
        }
        return output;
    }
}
