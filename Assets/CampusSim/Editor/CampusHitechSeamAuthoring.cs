using System;
using System.Collections.Generic;
using System.Linq;
using CampusSim;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>Remove the former road-end sidewalk/kerb caps inside the restored carriageway.</summary>
public static class CampusHitechSeamAuthoring
{
    private struct Vertex { public Vector3 p; public Vector2 uv; }
    private const string Folder="Assets/CampusSim/Generated/HitechRoadRepair/";

    public static void Apply()
    {
        if(EditorApplication.isPlayingOrWillChangePlaymode)throw new InvalidOperationException("Use Edit Mode.");
        var repair=GameObject.Find("Hitech missing OSM road").GetComponentInChildren<CampusTerrainBinding>();
        var source=repair.originalMesh.vertices;
        var centres=new[]{repair.roadGradeStart,repair.roadGradeEnd};
        var directions=new[]{(source[2]+source[3]-source[0]-source[1]).normalized,
            (source[source.Length-2]+source[source.Length-1]-source[source.Length-4]-source[source.Length-3]).normalized};
        foreach(string name in new[]{"Continuous pedestrian network","Junction aware kerbs"})
        {
            var binding=UnityEngine.Object.FindObjectsByType<CampusTerrainBinding>(FindObjectsSortMode.None).Single(b=>b.name==name);
            if(AssetDatabase.GetAssetPath(binding.originalMesh).StartsWith(Folder))
                throw new InvalidOperationException("Seams already trimmed; retain original assets for review.");
            var original=Clip(binding.originalMesh,binding.transform,centres,directions);
            var baked=Clip(binding.bakedMesh,binding.transform,centres,directions);
            var dense=Clip(binding.sourceMesh,binding.transform,centres,directions);
            AssetDatabase.CreateAsset(original,Folder+name+"_original.asset");
            AssetDatabase.CreateAsset(baked,Folder+name+"_baked.asset");
            AssetDatabase.CreateAsset(dense,Folder+name+"_source.asset");
            Undo.RecordObject(binding,"Open former road-end caps");
            binding.originalMesh=original;binding.sourceMesh=dense;binding.bakedMesh=baked;
            binding.GetComponent<MeshFilter>().sharedMesh=baked;
            var collider=binding.GetComponent<MeshCollider>();if(collider)collider.sharedMesh=baked;
            EditorUtility.SetDirty(binding);
            Debug.Log("HITECH_SEAM_TRIM "+name+" source assets preserved; render/collider matched");
        }
        AssetDatabase.SaveAssets();
        EditorSceneManager.MarkSceneDirty(repair.gameObject.scene);
        EditorSceneManager.SaveScene(repair.gameObject.scene);
    }

    private static Mesh Clip(Mesh mesh,Transform transform,Vector3[] centres,Vector3[] directions)
    {
        var old=mesh.vertices;var uv=mesh.uv;
        var vertices=new List<Vector3>();var uvs=new List<Vector2>();var submeshes=new List<int[]>();
        for(int sub=0;sub<mesh.subMeshCount;sub++)
        {
            var output=new List<int>();var indices=mesh.GetTriangles(sub);
            for(int i=0;i<indices.Length;i+=3)
            {
                var fragments=new List<List<Vertex>> { Enumerable.Range(0,3).Select(j=>new Vertex {
                    p=old[indices[i+j]],uv=uv.Length==old.Length?uv[indices[i+j]]:Vector2.zero }).ToList() };
                for(int cut=0;cut<centres.Length;cut++)
                {
                    var next=new List<List<Vertex>>();
                    var forward=directions[cut];forward.y=0;forward.Normalize();
                    var side=new Vector3(-forward.z,0,forward.x);
                    // Three metres either side of the old cap, within the six-metre road width.
                    foreach(var polygon in fragments)
                    {
                        var inside=polygon;
                        foreach(var normal in new[]{forward,-forward,side,-side})
                        {
                            float Distance(Vertex v)=>Vector3.Dot(transform.TransformPoint(v.p)-centres[cut],normal)-3f;
                            var outside=Half(inside,Distance,false);
                            if(outside.Count>=3)next.Add(outside);
                            inside=Half(inside,Distance,true);
                            if(inside.Count<3)break;
                        }
                        // Remaining inside polygon is the old road cap and is omitted.
                    }
                    fragments=next;
                }
                foreach(var polygon in fragments)
                    for(int j=1;j<polygon.Count-1;j++)
                        foreach(var v in new[]{polygon[0],polygon[j],polygon[j+1]})
                        { output.Add(vertices.Count);vertices.Add(v.p);uvs.Add(v.uv); }
            }
            submeshes.Add(output.ToArray());
        }
        var result=new Mesh { name=mesh.name+" Hitech open seams",indexFormat=UnityEngine.Rendering.IndexFormat.UInt32 };
        result.SetVertices(vertices);result.SetUVs(0,uvs);result.subMeshCount=submeshes.Count;
        for(int i=0;i<submeshes.Count;i++)result.SetTriangles(submeshes[i],i);
        result.RecalculateNormals();result.RecalculateBounds();return result;
    }

    private static List<Vertex> Half(List<Vertex> polygon,Func<Vertex,float> distance,bool inside)
    {
        var result=new List<Vertex>();if(polygon.Count==0)return result;
        var a=polygon[polygon.Count-1];float da=distance(a);bool keepA=inside?da<=0:da>0;
        foreach(var b in polygon)
        {
            float db=distance(b);bool keepB=inside?db<=0:db>0;
            if(keepA!=keepB)
            {
                float t=da/(da-db);
                result.Add(new Vertex{p=Vector3.Lerp(a.p,b.p,t),uv=Vector2.Lerp(a.uv,b.uv,t)});
            }
            if(keepB)result.Add(b);
            a=b;da=db;keepA=keepB;
        }
        return result;
    }
}
