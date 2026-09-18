using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using CampusSim;
using UnityEditor;
using UnityEngine;

// Visual edge protection only; dimensions are synthetic, not certified access data.
public static class CampusMainRampRails
{
    public static void Fit()
    {
        var landmark=UnityEngine.Object.FindObjectsByType<CampusLandmarkEntrances>(FindObjectsSortMode.None).SingleOrDefault(l=>l.landmarkId=="main_building");
        if(!landmark)return;
        var parent=landmark.generalEntrance.Find("Lateral pedestrian connection");
        if(!parent)throw new InvalidOperationException("Main ramp must be generated first.");
        var source=parent.GetComponent<MeshFilter>().sharedMesh.vertices;
        if(source.Length<485)throw new InvalidOperationException("Unexpected main ramp topology.");
        var material=AssetDatabase.LoadAssetAtPath<Material>("Assets/CampusSim/Generated/Hub/Graphite.mat");
        if(!material)throw new InvalidOperationException("Owned rail material missing.");
        var vertices=new List<Vector3>();var triangles=new List<int>();int posts=0,rails=0;
        int[] cube={0,2,1,0,3,2,4,5,6,4,6,7,0,1,5,0,5,4,3,7,6,3,6,2,0,4,7,0,7,3,1,2,6,1,6,5};
        void Bar(Vector3 a,Vector3 b,float thickness)
        {
            var d=b-a;if(d.sqrMagnitude<1e-8f)return;
            var rotation=Quaternion.FromToRotation(Vector3.up,d.normalized);var center=(a+b)*.5f;int first=vertices.Count;
            foreach(var corner in new[]{new Vector3(-1,-1,-1),new Vector3(1,-1,-1),new Vector3(1,1,-1),new Vector3(-1,1,-1),new Vector3(-1,-1,1),new Vector3(1,-1,1),new Vector3(1,1,1),new Vector3(-1,1,1)})
                vertices.Add(center+rotation*Vector3.Scale(corner,new Vector3(thickness,d.magnitude,thickness)*.5f));
            triangles.AddRange(cube.Select(i=>i+first));
        }
        foreach(int row in new[]{0,4})
        {
            float sincePost=100;
            for(int i=0;i<=96;i+=4)
            {
                var point=source[row*97+i];
                if(i>0)sincePost+=Vector3.Distance(point,source[row*97+i-4]);
                if(sincePost>=2.4f||i==8||i==80||i==96)
                {Bar(point,point+Vector3.up*.95f,.065f);posts++;sincePost=0;}
                if(i<96)foreach(float height in new[]{.48f,.92f})
                {Bar(point+Vector3.up*height,source[row*97+i+4]+Vector3.up*height,.05f);rails++;}
            }
        }
        const string path="Assets/CampusSim/Generated/EntranceApproaches/MainRampRails.asset";
        var mesh=AssetDatabase.LoadAssetAtPath<Mesh>(path);if(!mesh){mesh=new Mesh();AssetDatabase.CreateAsset(mesh,path);}
        Undo.RecordObject(mesh,"Refit main ramp rails");mesh.Clear();mesh.SetVertices(vertices);mesh.SetTriangles(triangles,0);mesh.RecalculateNormals();mesh.RecalculateBounds();EditorUtility.SetDirty(mesh);
        var child=parent.Find("Edge rails");if(!child){child=new GameObject("Edge rails").transform;Undo.RegisterCreatedObjectUndo(child.gameObject,"Create main ramp rails");child.SetParent(parent,false);child.gameObject.AddComponent<MeshFilter>();child.gameObject.AddComponent<MeshRenderer>();}
        Undo.RecordObject(child.GetComponent<MeshFilter>(),"Fit rail mesh");Undo.RecordObject(child.GetComponent<MeshRenderer>(),"Fit rail material");
        child.GetComponent<MeshFilter>().sharedMesh=mesh;child.GetComponent<MeshRenderer>().sharedMaterial=material;
        GameObjectUtility.SetStaticEditorFlags(child.gameObject,StaticEditorFlags.BatchingStatic|StaticEditorFlags.OccludeeStatic);
        File.WriteAllText("Docs/MapResearch/Iterations/main-ramp-rails.txt",$"Posts={posts}; longitudinal rails={rails}; triangles={triangles.Count/3}; renderers=1; materials=1; no transverse end bars; visual synthetic dimensions only.");
    }
}
