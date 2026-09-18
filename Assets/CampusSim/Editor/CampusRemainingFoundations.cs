using System;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEditor;
using CampusSim;

public static class CampusRemainingFoundations
{
    public static void Fit()
    {
        Directory.CreateDirectory("Assets/CampusSim/Generated/BuildingFoundations");
        FitOne("4호관 |", "0138", "Building4");
        FitOne("6호관 |", "0151", "Building6");
        FitOne("9호관 |", "0158", "Building9");
        FitOne("체육관 |", "0069", "Gymnasium");
        FitOne("building 797050851 |", "0184", "Building797050851");
    }
    static void FitOne(string prefix,string sourceId,string id)
    {
        var root=UnityEngine.Object.FindObjectsByType<CampusTerrainBinding>(FindObjectsSortMode.None).SingleOrDefault(x=>x.name.StartsWith(prefix,StringComparison.Ordinal));
        if(!root)return;
        string sourcePath="Assets/InhaCampus/Meshes/"+sourceId+".asset";
        string outputPath="Assets/CampusSim/Generated/BuildingFoundations/"+id+".asset";
        var source=AssetDatabase.LoadAssetAtPath<Mesh>(sourcePath);
        if(!source)throw new Exception("Building foundation source missing.");
        var filter=root.GetComponentsInChildren<MeshFilter>().Single(f=>f.name=="Facades");
        var terrain=UnityEngine.Object.FindObjectsByType<Terrain>(FindObjectsSortMode.None).Single();
        var vertices=source.vertices;var triangles=source.triangles;
        float bottom=vertices.Min(v=>v.y),lowest=float.PositiveInfinity;
        int samples=0;
        for(int k=0;k<triangles.Length;k+=3)for(int edge=0;edge<3;edge++)
        {
            var a=vertices[triangles[k+edge]];var b=vertices[triangles[k+(edge+1)%3]];
            if(Mathf.Abs(a.y-bottom)>.001f||Mathf.Abs(b.y-bottom)>.001f)continue;
            var wa=filter.transform.TransformPoint(a);var wb=filter.transform.TransformPoint(b);
            int steps=Mathf.Max(1,Mathf.CeilToInt(Vector3.Distance(wa,wb)/.25f));
            for(int i=0;i<=steps;i++)
            {
                var w=Vector3.Lerp(wa,wb,i/(float)steps);
                w.y=terrain.SampleHeight(w)+terrain.transform.position.y-.05f;
                lowest=Mathf.Min(lowest,filter.transform.InverseTransformPoint(w).y);samples++;
            }
        }
        if(samples==0)throw new Exception("Building bottom edges missing.");
        int changed=0;
        for(int i=0;i<vertices.Length;i++)if(Mathf.Abs(vertices[i].y-bottom)<.001f)
        {vertices[i].y=Mathf.Min(bottom,lowest);changed++;}
        var mesh=AssetDatabase.LoadAssetAtPath<Mesh>(outputPath);
        if(!mesh){mesh=UnityEngine.Object.Instantiate(source);AssetDatabase.CreateAsset(mesh,outputPath);}
        else {Undo.RecordObject(mesh,"Fit Building foundation");mesh.Clear();mesh.indexFormat=source.indexFormat;mesh.vertices=source.vertices;mesh.uv=source.uv;mesh.subMeshCount=source.subMeshCount;for(int i=0;i<source.subMeshCount;i++)mesh.SetTriangles(source.GetTriangles(i),i);}
        mesh.name=id;mesh.vertices=vertices;mesh.RecalculateNormals();mesh.RecalculateBounds();EditorUtility.SetDirty(mesh);
        Undo.RecordObject(filter,"Ground building foundation");filter.sharedMesh=mesh;
        var collider=filter.GetComponent<MeshCollider>();
        if(!collider)collider=Undo.AddComponent<MeshCollider>(filter.gameObject);
        if(collider){Undo.RecordObject(collider,"Refit building collider");collider.sharedMesh=null;collider.sharedMesh=mesh;}
        File.WriteAllText("Docs/MapResearch/Iterations/"+id+"-foundation.txt",$"Bottom edge samples={samples}; lowered vertices={changed}; original local bottom={bottom}; fitted bottom={Mathf.Min(bottom,lowest)}. Upper source vertices retained; source asset unchanged.");
    }
}
