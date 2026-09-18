using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;
using CampusSim;

public static class CampusSportsGroundAuthoring
{
    static float Cross(Vector3 a,Vector3 b)=>a.x*b.z-a.z*b.x;
    static List<Vector3> Clip(List<Vector3> input,Vector3 a,Vector3 b,float sign)
    {
        var output=new List<Vector3>();if(input.Count==0)return output;
        var previous=input[input.Count-1];float dp=sign*Cross(b-a,previous-a);
        foreach(var current in input)
        {
            float dc=sign*Cross(b-a,current-a);
            if((dp>=0)!=(dc>=0))output.Add(Vector3.LerpUnclamped(previous,current,dp/(dp-dc)));
            if(dc>=0)output.Add(current);
            previous=current;dp=dc;
        }
        return output;
    }
    [MenuItem("Campus/Map/Clip Sports Lines To Playing Surfaces")]
    public static void Apply()
    {
        if(EditorApplication.isPlayingOrWillChangePlaymode||SceneManager.GetActiveScene().path!="Assets/CampusSim/Scenes/CampusTerrain.unity")throw new Exception("Open working scene");
        const string folder="Assets/CampusSim/Generated/SportsGround";
        Directory.CreateDirectory(folder);AssetDatabase.Refresh();
        var bindings=UnityEngine.Object.FindObjectsByType<CampusTerrainBinding>(FindObjectsSortMode.None);
        var lines=bindings.Single(b=>b.name=="Sports field line markings");
        var backup=AssetDatabase.LoadAssetAtPath<UnityEngine.Mesh>(folder+"/OriginalLines.asset");
        if(!backup){backup=UnityEngine.Object.Instantiate(lines.originalMesh?lines.originalMesh:lines.sourceMesh);AssetDatabase.CreateAsset(backup,folder+"/OriginalLines.asset");}
        var surfaces=new List<Vector3[]>();
        foreach(var ground in bindings.Where(b=>b.name=="대운동장"||b.name.StartsWith("sport ")))
        {
            var mesh=ground.originalMesh?ground.originalMesh:ground.sourceMesh;var v=mesh.vertices;var t=mesh.triangles;
            for(int i=0;i<t.Length;i+=3)surfaces.Add(new[]{ground.transform.TransformPoint(v[t[i]]),ground.transform.TransformPoint(v[t[i+1]]),ground.transform.TransformPoint(v[t[i+2]])});
        }
        var source=backup.vertices;var indices=backup.triangles;var vertices=new List<Vector3>();var triangles=new List<int>();
        float originalArea=0,clippedArea=0;
        for(int i=0;i<indices.Length;i+=3)
        {
            var p=lines.transform.TransformPoint(source[indices[i]]);var q=lines.transform.TransformPoint(source[indices[i+1]]);var r=lines.transform.TransformPoint(source[indices[i+2]]);
            originalArea+=Mathf.Abs(Cross(q-p,r-p))/2;
            foreach(var surface in surfaces)
            {
                float signedArea=Cross(surface[1]-surface[0],surface[2]-surface[0]);if(Mathf.Abs(signedArea)<.000001f)continue;
                var polygon=new List<Vector3>{p,q,r};
                for(int edge=0;edge<3;edge++)polygon=Clip(polygon,surface[edge],surface[(edge+1)%3],Mathf.Sign(signedArea));
                for(int j=1;j+1<polygon.Count;j++)
                {
                    float area=Mathf.Abs(Cross(polygon[j]-polygon[0],polygon[j+1]-polygon[0]))/2;if(area<.000001f)continue;
                    clippedArea+=area;int k=vertices.Count;
                    vertices.Add(lines.transform.InverseTransformPoint(polygon[0]));vertices.Add(lines.transform.InverseTransformPoint(polygon[j]));vertices.Add(lines.transform.InverseTransformPoint(polygon[j+1]));
                    triangles.AddRange(new[]{k,k+1,k+2});
                }
            }
        }
        UnityEngine.Mesh Get(string name)
        {var path=folder+"/"+name+".asset";var mesh=AssetDatabase.LoadAssetAtPath<UnityEngine.Mesh>(path);if(!mesh){mesh=new UnityEngine.Mesh{name=name,indexFormat=UnityEngine.Rendering.IndexFormat.UInt32};AssetDatabase.CreateAsset(mesh,path);}return mesh;}
        var clipped=Get("ClippedLines");clipped.Clear();clipped.SetVertices(vertices);clipped.SetTriangles(triangles,0);clipped.RecalculateNormals();clipped.RecalculateBounds();EditorUtility.SetDirty(clipped);
        lines.originalMesh=clipped;lines.sourceMesh=Get("ClippedLinesSource");lines.bakedMesh=Get("ClippedLinesBaked");
        EditorUtility.CopySerialized(clipped,lines.sourceMesh);EditorUtility.CopySerialized(clipped,lines.bakedMesh);EditorUtility.SetDirty(lines);
        CampusTerrainAuthoring.Bake();EditorSceneManager.SaveScene(SceneManager.GetActiveScene());
        File.WriteAllText("Docs/MapResearch/Iterations/sports-line-clipping.txt","Clipped original marking triangles to actual playing-surface triangle union. Original projected area="+originalArea+"m2; retained="+clippedArea+"m2; removed="+(originalArea-clippedArea)+"m2. Original source preserved. This does not certify field dimensions or fix field grading.\n");
    }
}
