using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using CampusSim;

public static class CampusMainBuildingFrontage
{
    public static void Fit()
    {
        var landmark=UnityEngine.Object.FindObjectsByType<CampusLandmarkEntrances>(FindObjectsSortMode.None).SingleOrDefault(x=>x.landmarkId=="main_building");
        if(!landmark)return;
        var portal=landmark.generalEntrance;
        var landing=portal.Find("Landing");
        var paving=landmark.accessibleEntrance.Find("Terrain approach draft").GetComponent<MeshCollider>();
        var terrain=UnityEngine.Object.FindObjectsByType<Terrain>(FindObjectsSortMode.None).Single();
        const int rows=28,columns=4;
        var vertices=new List<Vector3>();var triangles=new List<int>();
        float start=-landing.localScale.x/2,top=landing.localPosition.y+landing.localScale.y/2;
        float maxGrade=0,maxBurial=0,minGap=100,maxGap=-100;
        for(int j=0;j<=columns;j++)
        {
            float z=Mathf.Lerp(2.65f,4.45f,j/(float)columns);
            var approach=portal.Find("Terrain approach draft").GetComponent<MeshCollider>();
            var origin=portal.TransformPoint(new Vector3(start+.001f,0,z));
            if(!approach.Raycast(new Ray(origin+Vector3.up*100,Vector3.down),out var startHit,200))throw new Exception("Approach start missing.");
            top=portal.InverseTransformPoint(startHit.point).y;
            float end=portal.InverseTransformPoint(landmark.accessibleEntrance.position).x+1.65f;
            var target=portal.TransformPoint(new Vector3(end-.001f,0,z));
            if(!paving.Raycast(new Ray(target+Vector3.up*100,Vector3.down),out var endHit,200))throw new Exception("Accessible approach target missing.");
            float height=portal.InverseTransformPoint(endHit.point).y;
            maxGrade=Mathf.Max(maxGrade,Mathf.Abs(height-top)/Mathf.Abs(start-end));
            for(int i=0;i<=rows;i++)
            {
                float t=i/(float)rows;var v=new Vector3(Mathf.Lerp(start,end,t),Mathf.Lerp(top,height,t),z);
                var w=portal.TransformPoint(v);
                // Preserve fitted endpoint seams, but follow local sculpted terrain inside the strip.
                if(i>0&&i<rows)
                {
                    float ground=terrain.SampleHeight(w)+terrain.transform.position.y;
                    if(w.y<ground+.025f){w.y=ground+.025f;v=portal.InverseTransformPoint(w);}
                }
                if(i>0)
                {
                    var previous=vertices[vertices.Count-1];
                    maxGrade=Mathf.Max(maxGrade,Mathf.Abs(v.y-previous.y)/Mathf.Max(.0001f,Mathf.Abs(v.x-previous.x)));
                }
                maxBurial=Mathf.Max(maxBurial,terrain.SampleHeight(w)+terrain.transform.position.y-w.y);
                if(i==rows&&paving.Raycast(new Ray(w+Vector3.up*100,Vector3.down),out var hit,200))
                {minGap=Mathf.Min(minGap,w.y-hit.point.y);maxGap=Mathf.Max(maxGap,w.y-hit.point.y);}
                vertices.Add(v);
            }
        }
        if(maxBurial>.001f)throw new Exception("Lateral sidewalk intersects Terrain; grade site before applying.");
        for(int j=0;j<columns;j++)for(int i=0;i<rows;i++)
        {int k=j*(rows+1)+i;triangles.AddRange(new[]{k,k+1,k+rows+1,k+1,k+rows+2,k+rows+1});}
        var boundary=new List<int>();
        for(int i=0;i<=rows;i++)boundary.Add(i);
        for(int j=1;j<=columns;j++)boundary.Add(j*(rows+1)+rows);
        for(int i=rows-1;i>=0;i--)boundary.Add(columns*(rows+1)+i);
        for(int j=columns-1;j>0;j--)boundary.Add(j*(rows+1));
        var polygon=boundary.Select(i=>vertices[i]).ToArray();
        for(int i=0;i<boundary.Count;i++)
        {
            var a=vertices[boundary[i]];var b=vertices[boundary[(i+1)%boundary.Count]];
            Vector3 Bottom(Vector3 v){var w=portal.TransformPoint(v);v.y=portal.InverseTransformPoint(new Vector3(w.x,terrain.SampleHeight(w)+terrain.transform.position.y-.05f,w.z)).y;return v;}
            int k=vertices.Count;vertices.AddRange(new[]{a,Bottom(a),b,Bottom(b)});triangles.AddRange(new[]{k,k+1,k+2,k+2,k+1,k+3});
        }
        const string path="Assets/CampusSim/Generated/EntranceApproaches/MainBuildingSharedFrontage.asset";
        var mesh=AssetDatabase.LoadAssetAtPath<Mesh>(path);if(!mesh){mesh=new Mesh();AssetDatabase.CreateAsset(mesh,path);}
        
        Undo.RecordObject(mesh,"Fit main building frontage");
        mesh.Clear();mesh.SetVertices(vertices);mesh.SetTriangles(triangles,0);mesh.RecalculateNormals();mesh.RecalculateBounds();EditorUtility.SetDirty(mesh);
        var child=portal.Find("Shared frontage connection");
        if(!child){child=new GameObject("Shared frontage connection").transform;Undo.RegisterCreatedObjectUndo(child.gameObject,"Create lateral sidewalk");child.SetParent(portal,false);child.gameObject.AddComponent<MeshFilter>();child.gameObject.AddComponent<MeshRenderer>();child.gameObject.AddComponent<MeshCollider>();child.gameObject.AddComponent<CampusPedestrianArea>();}
        child.GetComponent<MeshFilter>().sharedMesh=mesh;
        child.GetComponent<MeshRenderer>().sharedMaterial=AssetDatabase.LoadAssetAtPath<Material>("Assets/CampusSim/Generated/Hub/Walkway.mat");
        var collider=child.GetComponent<MeshCollider>();collider.sharedMesh=null;collider.sharedMesh=mesh;
        var area=child.GetComponent<CampusPedestrianArea>();area.areaId="main_building_shared_frontage";area.localBoundary=polygon;area.excludeFromVehicleRouting=true;area.pedestrianAccessVerified=false;area.source="Synthetic sidewalk connection; measured access and Stop routing unverified.";
        GameObjectUtility.SetStaticEditorFlags(child.gameObject,StaticEditorFlags.BatchingStatic|StaticEditorFlags.OccludeeStatic);
        File.WriteAllText("Docs/MapResearch/Iterations/main-building-shared-frontage.txt",$"Width=1.8m; top samples={(rows+1)*(columns+1)}; maximum longitudinal grade={maxGrade}; terrain burial={maxBurial}; {columns+1} terminal samples gap to accessible approach={minGap}..{maxGap}m. Authoring geometry only.");
    }
}


