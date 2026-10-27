using System;
using System.IO;
using System.Linq;
using CampusSim;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class CampusRoadSmoothingValidation
{
    // Batch entry point: -executeMethod CampusRoadSmoothingValidation.BakeAndValidate
    public static void BakeAndValidate()
    {
        EditorSceneManager.OpenScene("Assets/CampusSim/Scenes/CampusTerrain.unity");
        CampusRoadSurfaceSmoothing.Apply();
        CampusRoadGradeAudit.Export();
        Validate();
    }

    public static void Validate()
    {
        var terrain=UnityEngine.Object.FindObjectsByType<Terrain>(FindObjectsSortMode.None).Single();
        var roads=UnityEngine.Object.FindObjectsByType<CampusTerrainBinding>(FindObjectsSortMode.None)
            .Where(b=>b.roadHeightSource && b.roadSmoothingMeters>0).ToArray();
        int samples=0,penetrations=0,mismatches=0,triangles=0;
        float minimumClearance=float.MaxValue,maximumClearance=0,maximumGrade=0;
        double area=0,steepArea=0;
        foreach(var road in roads)
        {
            if(road.GetComponent<MeshCollider>().sharedMesh!=road.GetComponent<MeshFilter>().sharedMesh)mismatches++;
            var vertices=road.bakedMesh.vertices.Select(road.transform.TransformPoint).ToArray();
            var indices=road.bakedMesh.triangles;
            for(int i=0;i<indices.Length;i+=3)
            {
                var a=vertices[indices[i]];var b=vertices[indices[i+1]];var c=vertices[indices[i+2]];
                var normal=Vector3.Cross(b-a,c-a);
                float faceArea=Mathf.Abs(normal.y)*.5f;
                if(faceArea>.0001f)
                {
                    float grade=new Vector2(normal.x,normal.z).magnitude/Mathf.Abs(normal.y);
                    if(!float.IsFinite(grade))throw new Exception("Nonfinite road face");
                    maximumGrade=Mathf.Max(maximumGrade,grade);area+=faceArea;
                    if(grade>.1f)steepArea+=faceArea;
                    triangles++;
                }
                for(int j=0;j<=4;j++)for(int k=0;k<=4-j;k++)
                {
                    var p=a+(b-a)*(j/4f)+(c-a)*(k/4f);
                    float clearance=p.y-terrain.SampleHeight(p)-terrain.transform.position.y;
                    minimumClearance=Mathf.Min(minimumClearance,clearance);
                    maximumClearance=Mathf.Max(maximumClearance,clearance);
                    samples++;if(clearance<.001f)penetrations++;
                }
            }
        }
        Directory.CreateDirectory("artifacts/road_smoothing");
        string report=$"roads={roads.Length}\ntriangles={triangles}\nsamples={samples}\nterrainPenetrations={penetrations}\nmeshColliderMismatches={mismatches}\nminimumClearanceM={minimumClearance:R}\nmaximumGrade={maximumGrade:R}\nhorizontalAreaM2={area:R}\nareaAbove10PercentM2={steepArea:R}\n";
        report+=$"maximumClearanceM={maximumClearance:R}\n";
        File.WriteAllText("artifacts/road_smoothing/collision-audit.txt",report);
        Debug.Log(report);
        int hitechRepairs=roads.Count(r=>r.sourceId=="way/481241714" && r.name.StartsWith("Road 481241714 missing "));
        if(roads.Length!=15+hitechRepairs || penetrations!=0 || mismatches!=0 || maximumGrade>.5f || maximumClearance>.25f)
            throw new Exception("Road smoothing geometry regression; inspect collision-audit.txt");
    }
}
