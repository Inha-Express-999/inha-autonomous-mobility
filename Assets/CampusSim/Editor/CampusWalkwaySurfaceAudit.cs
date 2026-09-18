using System;
using System.IO;
using System.Text;
using System.Linq;
using UnityEditor;
using UnityEngine.SceneManagement;
using UnityEngine;
using CampusSim;

public static class CampusWalkwaySurfaceAudit
{
    [MenuItem("Campus/Map/Audit Authored Walkway Surfaces")]
    public static void Export()
    {
        if(SceneManager.GetActiveScene().path!="Assets/CampusSim/Scenes/CampusTerrain.unity")
            throw new InvalidOperationException("Open CampusTerrain before auditing authored walkways.");
        var required=new[]{"hitech_shared_frontage","hitech_general_lateral","biryong_plaza_accessible_lateral","rear_gate_accessible_lateral","dormitory1_shared_frontage","dormitory2_shared_frontage","dormitory3_shared_frontage","dormitory3_driveway_connection","library_shared_frontage"};
        var seen=new System.Collections.Generic.HashSet<string>();
        var report=new StringBuilder("area_id,top_triangles,max_surface_gradient,area_above_5_percent_m2,vehicle_excluded,access_verified,inverted_faces,collider_matches_mesh,geometry_status\n");
        foreach(var area in UnityEngine.Object.FindObjectsByType<CampusPedestrianArea>(FindObjectsSortMode.None))
        {
            if(area.name!="Lateral pedestrian connection"&&area.name!="Shared frontage connection"&&area.name!="Driveway pedestrian connection")continue;
            var filter=area.GetComponent<MeshFilter>();
            if(!seen.Add(area.areaId))throw new InvalidOperationException("Duplicate walkway area ID: "+area.areaId);
            if(!filter||!filter.sharedMesh)
            {report.AppendLine(area.areaId+",0,,,,,,false,missing_mesh");continue;}
            var vertices=filter.sharedMesh.vertices;var indices=filter.sharedMesh.triangles;
            int count=0,inverted=0;float maximum=0;double steepArea=0;
            for(int i=0;i<indices.Length;i+=3)
            {
                // Authoring frames are upright. Exact vertical skirts have no
                // local XZ area; skip before world translation loses precision.
                var localNormal=Vector3.Cross(vertices[indices[i+1]]-vertices[indices[i]],vertices[indices[i+2]]-vertices[indices[i]]);
                if(localNormal.y==0f && Vector3.Dot(filter.transform.up,Vector3.up)>.999999f)continue;
                var a=filter.transform.TransformPoint(vertices[indices[i]]);
                var b=filter.transform.TransformPoint(vertices[indices[i+1]]);
                var c=filter.transform.TransformPoint(vertices[indices[i+2]]);
                var normal=Vector3.Cross(b-a,c-a);
                // These generators author upward top faces and vertical side walls.
                // Ignore zero projected-area walls; do not hide steep upward faces.
                if(normal.y<-.000001f)inverted++;
                if(normal.y<=.000001f)continue;
                float grade=new Vector2(normal.x,normal.z).magnitude/normal.y;
                maximum=Mathf.Max(maximum,grade);count++;
                if(grade>.05f)steepArea+=normal.y*.5;
            }
            var collider=area.GetComponent<MeshCollider>();
            bool colliderMatches=collider&&collider.enabled&&collider.sharedMesh==filter.sharedMesh;
            string status=count>0&&inverted==0&&colliderMatches&&area.excludeFromVehicleRouting?"geometry_checked_access_unverified":"geometry_issue";
            report.AppendLine(FormattableString.Invariant($"{area.areaId},{count},{maximum},{steepArea},{area.excludeFromVehicleRouting},{area.pedestrianAccessVerified},{inverted},{colliderMatches},{status}"));
        }
        foreach(var id in required.Where(id=>!seen.Contains(id)))report.AppendLine(id+",0,,,,,,false,missing_area");
        File.WriteAllText("Docs/MapResearch/Iterations/walkway-surface-gradients.csv",report.ToString());
        Debug.Log(report.ToString());
    }
}

