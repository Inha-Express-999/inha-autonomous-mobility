using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using CampusSim;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>Restore only missing portions of an existing exported OSM road.</summary>
public static class CampusMissingRoadAuthoring
{
    [Serializable] private class RoadSet { public Road[] roads; }
    [Serializable] private class Road { public string osmId; public float width; public Vector3[] points; }
    private const string RootName = "Hitech missing OSM road";
    private const string Folder = "Assets/CampusSim/Generated/HitechRoadRepair";

    [MenuItem("Campus/Map/Restore Missing Hitech Road")]
    public static void Build()
    {
        var scene = SceneManager.GetActiveScene();
        if (EditorApplication.isPlayingOrWillChangePlaymode || scene.path != "Assets/CampusSim/Scenes/CampusTerrain.unity")
            throw new InvalidOperationException("Open CampusTerrain in Edit Mode.");
        if (scene.GetRootGameObjects().Any(g => g.name == RootName))
            throw new InvalidOperationException("Repair already exists; inspect before rebuilding.");
        var roads = UnityEngine.Object.FindObjectsByType<CampusTerrainBinding>(FindObjectsSortMode.None)
            .Where(b => b.gameObject.scene == scene && b.roadHeightSource && b.roadSmoothingMeters > 0).ToArray();
        if (roads.Length == 0) throw new InvalidOperationException("Smoothed road sources required.");
        var triangles = new List<Vector3[]>();
        foreach (var road in roads)
        {
            var vertices = road.originalMesh.vertices.Select(road.transform.TransformPoint).ToArray();
            var indices = road.originalMesh.triangles;
            for (int i = 0; i < indices.Length; i += 3)
                triangles.Add(new[] { vertices[indices[i]], vertices[indices[i+1]], vertices[indices[i+2]] });
        }
        bool Covered(Vector3 p) => triangles.Any(t => Contains(p, t[0], t[1], t[2]));
        var data = JsonUtility.FromJson<RoadSet>(File.ReadAllText("Assets/CampusSim/Data/road_extensions.json"))
            .roads.Single(r => r.osmId == "481241714");
        var map = GameObject.Find("INHA UNIVERSITY").transform;
        var points = data.points.Select(map.TransformPoint).ToArray();
        // Quarter-metre coverage audit, then bisection of boundary transitions.
        var samples = new List<Vector3> { points[0] };
        for (int i = 1; i < points.Length; i++)
        {
            int count = Mathf.CeilToInt(Vector3.Distance(points[i-1], points[i]) / .25f);
            for (int j = 1; j <= count; j++) samples.Add(Vector3.Lerp(points[i-1], points[i], (float)j/count));
        }
        var gaps = new List<List<Vector3>>();
        List<Vector3> gap = null;
        bool previousCovered = Covered(samples[0]);
        // The full export also extends past the northern campus clip. Only restore
        // gaps with existing asphalt on both ends, never invent a new endpoint.
        for (int i = 1; i < samples.Count; i++)
        {
            bool covered = Covered(samples[i]);
            if (covered != previousCovered)
            {
                Vector3 a = samples[i-1], b = samples[i];
                for (int j = 0; j < 16; j++)
                {
                    var mid = (a+b)*.5f;
                    if (Covered(mid) == previousCovered) a = mid; else b = mid;
                }
                var boundary = (a+b)*.5f;
                if (!covered) gap = new List<Vector3> { boundary };
                else if (gap != null) { gap.Add(boundary); gaps.Add(gap); gap = null; }
            }
            if (!covered && gap != null) gap.Add(samples[i]);
            previousCovered = covered;
        }
        gaps.RemoveAll(g => Enumerable.Range(1,g.Count-1).Sum(i => Vector3.Distance(g[i-1],g[i])) < .1f);
        if (gaps.Count == 0) throw new InvalidOperationException("No bounded missing road interval found.");
        float length = gaps.Sum(g => Enumerable.Range(1,g.Count-1).Sum(i => Vector3.Distance(g[i-1],g[i])));
        if (length > 180 || data.width != 6) throw new InvalidOperationException("Unexpected repair extent/width; inspect export.");

        Directory.CreateDirectory(Folder);
        AssetDatabase.Refresh();
        var root = new GameObject(RootName);
        Undo.RegisterCreatedObjectUndo(root, "Restore Hitech OSM road");
        var material = roads.First(b => b.name == "Continuous asphalt network").GetComponent<MeshRenderer>().sharedMaterial;
        for (int part = 0; part < gaps.Count; part++)
        {
            var line = gaps[part];
            // Small overlap closes float/oblique edge cracks across both wheel tracks.
            line[0]-=(line[1]-line[0]).normalized*.15f;
            line[line.Count-1]+=(line[line.Count-1]-line[line.Count-2]).normalized*.15f;
            var vertices = new Vector3[line.Count*2];
            var indices = new int[(line.Count-1)*6];
            for (int i = 0; i < line.Count; i++)
            {
                var tangent = line[Math.Min(i+1,line.Count-1)] - line[Math.Max(0,i-1)];
                tangent.y = 0;
                var side = new Vector3(-tangent.z,0,tangent.x).normalized * data.width*.5f;
                vertices[2*i] = line[i]-side; vertices[2*i+1] = line[i]+side;
                if (i == line.Count-1) continue;
                int k=6*i,v=2*i;
                indices[k]=v; indices[k+1]=v+1; indices[k+2]=v+2;
                indices[k+3]=v+1; indices[k+4]=v+3; indices[k+5]=v+2;
            }
            var source = new Mesh { name = "481241714 missing source " + part };
            source.vertices=vertices; source.triangles=indices; source.RecalculateNormals(); source.RecalculateBounds();
            AssetDatabase.CreateAsset(source, Folder+"/source_"+part+".asset");
            var baked = UnityEngine.Object.Instantiate(source);
            AssetDatabase.CreateAsset(baked, Folder+"/baked_"+part+".asset");
            var obj = new GameObject("Road 481241714 missing " + part);
            obj.transform.SetParent(root.transform,false);
            obj.AddComponent<MeshFilter>().sharedMesh=baked;
            obj.AddComponent<MeshRenderer>().sharedMaterial=material;
            obj.AddComponent<MeshCollider>().sharedMesh=baked;
            var binding=obj.AddComponent<CampusTerrainBinding>();
            binding.kind=CampusTerrainBinding.BindingKind.Surface;
            binding.sourceId="way/481241714"; binding.verifiedAccess=false;
            binding.originalMesh=source; binding.sourceMesh=source; binding.bakedMesh=baked;
            binding.surfaceOffset=roads[0].surfaceOffset;
            binding.roadHeightSource=roads[0].roadHeightSource; binding.roadSmoothingMeters=8;
            GameObjectUtility.SetStaticEditorFlags(obj,StaticEditorFlags.BatchingStatic|StaticEditorFlags.OccludeeStatic);
        }
        FitEndpointGrade();
        CampusHitechSeamAuthoring.Apply();
        CampusRoadSmoothingValidation.Validate();
        Debug.Log("HITECH_ROAD_REPAIR parts="+gaps.Count+" length_m="+length+" width_m="+data.width+" source=existing_OSM_export access_unverified=true");
    }

    [MenuItem("Campus/Map/Fit Hitech Connection Endpoint Grade")]
    public static void FitEndpointGrade()
    {
        if(EditorApplication.isPlayingOrWillChangePlaymode)throw new InvalidOperationException("Use Edit Mode.");
        var root=GameObject.Find(RootName);
        if(!root)throw new InvalidOperationException("Missing repair root.");
        var terrain=UnityEngine.Object.FindFirstObjectByType<Terrain>();
        var network=UnityEngine.Object.FindObjectsByType<CampusTerrainBinding>(FindObjectsSortMode.None)
            .Single(b=>b.name=="Continuous asphalt network");
        var repairs=root.GetComponentsInChildren<CampusTerrainBinding>();
        foreach(var repair in repairs)repair.roadEndpointGrade=true;
        CampusRoadSurfaceSmoothing.Prepare(terrain);
        foreach(var repair in repairs)
        {
            var vertices=repair.originalMesh.vertices;
            var a=repair.transform.TransformPoint((vertices[0]+vertices[1])*.5f);
            var b=repair.transform.TransformPoint((vertices[vertices.Length-2]+vertices[vertices.Length-1])*.5f);
            a.y=CampusRoadSurfaceSmoothing.Height(network,terrain,a);
            b.y=CampusRoadSurfaceSmoothing.Height(network,terrain,b);
            repair.roadGradeStart=a; repair.roadGradeEnd=b;
            EditorUtility.SetDirty(repair);
            Debug.Log("HITECH_ENDPOINT_GRADE start="+a+" end="+b);
        }
        CampusRoadSurfaceSmoothing.Apply();
    }

    private static bool Contains(Vector3 p, Vector3 a, Vector3 b, Vector3 c)
    {
        float cross=(b.x-a.x)*(c.z-a.z)-(b.z-a.z)*(c.x-a.x);
        if (Mathf.Abs(cross)<1e-7f) return false;
        float u=((p.x-a.x)*(c.z-a.z)-(p.z-a.z)*(c.x-a.x))/cross;
        float v=((b.x-a.x)*(p.z-a.z)-(b.z-a.z)*(p.x-a.x))/cross;
        // Source export and Unity meshes use different float precision at clip boundaries.
        return u>=-.00001f && v>=-.00001f && u+v<=1.00001f;
    }
}
