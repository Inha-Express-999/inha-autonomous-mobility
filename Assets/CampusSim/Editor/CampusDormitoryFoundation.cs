using System;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEditor;
using CampusSim;

public static class CampusDormitoryFoundation
{
    public static void Fit()
    {
        var root=UnityEngine.Object.FindObjectsByType<CampusLandmarkEntrances>(FindObjectsSortMode.None).FirstOrDefault(x=>x.landmarkId=="dorm_2");
        if(!root)return;
        const string sourcePath="Assets/CampusSim/Generated/Dormitories/dorm_2.asset";
        const string outputPath="Assets/CampusSim/Generated/Dormitories/dorm_2_grounded.asset";
        var source=AssetDatabase.LoadAssetAtPath<Mesh>(sourcePath);
        if(!source)throw new Exception("Dormitory foundation source missing.");
        var filter=root.GetComponent<MeshFilter>();
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
        if(samples==0)throw new Exception("Dormitory bottom edges missing.");
        int changed=0;
        for(int i=0;i<vertices.Length;i++)if(Mathf.Abs(vertices[i].y-bottom)<.001f)
        {vertices[i].y=Mathf.Min(bottom,lowest);changed++;}
        var mesh=AssetDatabase.LoadAssetAtPath<Mesh>(outputPath);
        if(!mesh){mesh=UnityEngine.Object.Instantiate(source);AssetDatabase.CreateAsset(mesh,outputPath);}
        else EditorUtility.CopySerialized(source,mesh);
        mesh.name="dorm_2_grounded";mesh.vertices=vertices;mesh.RecalculateNormals();mesh.RecalculateBounds();EditorUtility.SetDirty(mesh);
        Undo.RecordObject(filter,"Ground dormitory foundation");filter.sharedMesh=mesh;
        var collider=root.GetComponent<MeshCollider>();
        if(collider){Undo.RecordObject(collider,"Refit dormitory collider");collider.sharedMesh=null;collider.sharedMesh=mesh;}
        File.WriteAllText("Docs/MapResearch/Iterations/DormitoryRoadGrade/foundation-fit.txt",$"Bottom edge samples={samples}; lowered vertices={changed}; original local bottom={bottom}; fitted bottom={Mathf.Min(bottom,lowest)}. Upper source vertices retained; source asset unchanged.");
    }
}
