using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using CampusSim;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// Native willow cards are vertical. Small horizontal cards retain the crown
// in the operator's top view; this is visual geometry, not a collision model.
public static class CampusWillowCanopy
{
    const string Folder="Assets/CampusSim/Generated/WillowCanopy";
    [MenuItem("Campus/Map/Rebuild Willow Overhead Canopies")]
    public static void Apply()
    {
        if(EditorApplication.isPlayingOrWillChangePlaymode || EditorSceneManager.GetActiveScene().path!="Assets/CampusSim/Scenes/CampusTerrain.unity")
            throw new InvalidOperationException("Open the working scene in edit mode.");
        var originals=Enumerable.Range(1,3).SelectMany(i=>AssetDatabase.LoadAllAssetsAtPath(
            "Assets/Idyllic Fantasy Nature/Models/Trees/WillowTree_0"+i+".fbx").OfType<Mesh>()).ToArray();
        var bindings=UnityEngine.Object.FindObjectsByType<CampusTerrainBinding>(FindObjectsSortMode.None)
            .Where(b=>b.name.StartsWith("Lakeside willow ",StringComparison.Ordinal)).ToArray();
        var assignments=new List<(MeshFilter filter,Mesh source)>();
        foreach(var b in bindings)foreach(var f in b.GetComponentsInChildren<MeshFilter>())
        {
            var source=originals.SingleOrDefault(m=>m==f.sharedMesh || m.name+"_Overhead"==f.sharedMesh.name);
            if(!source || source.subMeshCount!=2 || source.uv.Length!=source.vertexCount)
                throw new InvalidOperationException("Unsupported willow mesh: "+f.sharedMesh.name);
            assignments.Add((f,source));
        }
        Directory.CreateDirectory(Folder);
        var generated=new Dictionary<Mesh,Mesh>();
        var report=new List<string>{"source,original_triangles,added_triangles,unsupported_quad_groups"};
        foreach(var source in assignments.Select(a=>a.source).Distinct())
        {
            var v=source.vertices;var uv=source.uv;
            var vertices=v.ToList();var tex=uv.ToList();
            var leaves=source.GetTriangles(1);var indices=leaves.ToList();
            bool distant=source.name.EndsWith("LOD2",StringComparison.Ordinal);
            int accepted=0,skipped=0,added=0;
            for(int i=0;i+5<leaves.Length;i+=6)
            {
                var points=leaves.Skip(i).Take(6).Select(k=>v[k]).Distinct().ToArray();
                if(points.Length!=4){skipped++;continue;}
                // Preserve original hanging foliage in every LOD. Only the added
                // overhead layer is thinned, deterministically, at the last LOD.
                if(distant && accepted++%2!=0)continue;
                var center=(points[0]+points[1]+points[2]+points[3])*.25f;
                var n=Vector3.Cross(v[leaves[i+1]]-v[leaves[i]],v[leaves[i+2]]-v[leaves[i]]).normalized;
                if(n.sqrMagnitude<.5f)continue;
                var rotation=Quaternion.FromToRotation(n,Vector3.up);
                float height=points.Max(p=>p.y)-points.Min(p=>p.y);
                int start=vertices.Count;
                for(int j=0;j<6;j++)
                {
                    int id=leaves[i+j];
                    vertices.Add(center+Vector3.up*(height*.18f)+rotation*(v[id]-center)*.5f);
                    tex.Add(uv[id]);indices.Add(start+j);
                }
                added+=2;
            }
            string path=Folder+"/"+source.name+"_Overhead.asset";
            var mesh=AssetDatabase.LoadAssetAtPath<Mesh>(path);bool create=!mesh;
            if(create)mesh=new Mesh{name=source.name+"_Overhead"};
            else {Undo.RecordObject(mesh,"Update willow canopy");mesh.Clear();}
            mesh.indexFormat=UnityEngine.Rendering.IndexFormat.UInt32;
            mesh.SetVertices(vertices);mesh.SetUVs(0,tex);mesh.subMeshCount=2;
            mesh.SetTriangles(source.GetTriangles(0),0);mesh.SetTriangles(indices,1);
            mesh.RecalculateNormals();mesh.RecalculateBounds();
            if(create)AssetDatabase.CreateAsset(mesh,path);else EditorUtility.SetDirty(mesh);
            generated.Add(source,mesh);
            report.Add(source.name+","+source.triangles.Length/3+","+added+","+skipped);
        }
        foreach(var a in assignments)
        {
            Undo.RecordObject(a.filter,"Assign willow canopy");a.filter.sharedMesh=generated[a.source];
            EditorUtility.SetDirty(a.filter);
        }
        foreach(var b in bindings)foreach(var lod in b.GetComponentsInChildren<LODGroup>())
        {Undo.RecordObject(lod,"Refit canopy LOD bounds");lod.RecalculateBounds();}
        AssetDatabase.SaveAssets();EditorSceneManager.SaveScene(EditorSceneManager.GetActiveScene());
        File.WriteAllLines("Docs/MapResearch/Iterations/willow-canopy-meshes.csv",report);
        Debug.Log("Willow canopy regenerated: "+assignments.Count+" renderers / "+generated.Count+" shared meshes.");
    }
}
