using System;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEditor;
using UnityEngine.SceneManagement;
using UnityEditor.SceneManagement;
using CampusSim;

internal class CommandScript : IRunCommand
{
    [Serializable] class RoadSet { public Road[] roads; }
    [Serializable] class Road { public string id,osmId; public float width; public Vector3[] points; }
    public void Execute(ExecutionResult result)
    {
        if(EditorApplication.isPlayingOrWillChangePlaymode || SceneManager.GetActiveScene().path!="Assets/CampusSim/Scenes/CampusTerrain.unity")throw new Exception("Open working scene in edit mode.");
        const string rootName="Restored dormitory road connections";
        if(GameObject.Find(rootName) && GameObject.Find(rootName).transform.childCount>0)throw new Exception("Already applied; inspect existing objects.");
        var terrain=Resources.FindObjectsOfTypeAll<UnityEngine.Terrain>().First(t=>t.gameObject.scene==SceneManager.GetActiveScene());
        var map=GameObject.Find("INHA UNIVERSITY").transform;
        var material=GameObject.Find("Campus scoped connections").GetComponentsInChildren<MeshRenderer>().First(r=>r.name.StartsWith("Road ")).sharedMaterial;
        var culture=System.Globalization.CultureInfo.InvariantCulture;
        var lines=File.ReadAllLines("Assets/CampusSim/Data/road_connections_restored.txt");
        const string folder="Assets/CampusSim/Generated/RestoredConnections";
        Directory.CreateDirectory(folder);AssetDatabase.Refresh();
        var root=GameObject.Find(rootName);if(!root){root=new GameObject(rootName);result.RegisterObjectCreation(root);}
        result.Log("Road count="+lines.Length+" terrain="+terrain.name);
        foreach(var line in lines)
        {
            var cells=line.Split('|');
            var road=new Road();road.id=cells[0];road.osmId=cells[1];road.width=float.Parse(cells[2],culture);
            road.points=cells[3].Split(';').Select(p=>p.Split(',')).Select(p=>new Vector3(float.Parse(p[0],culture),0,float.Parse(p[1],culture))).ToArray();
            result.Log("Begin "+road.id+" points="+road.points.Length);
            var points=road.points.Select(map.TransformPoint).ToArray();
            var vertices=new Vector3[points.Length*2];var triangles=new int[(points.Length-1)*6];
            for(int i=0;i<points.Length;i++)
            {
                var tangent=points[Mathf.Min(i+1,points.Length-1)]-points[Mathf.Max(0,i-1)];tangent.y=0;tangent.Normalize();
                var side=new Vector3(-tangent.z,0,tangent.x)*road.width*.5f;
                vertices[2*i]=points[i]-side;vertices[2*i+1]=points[i]+side;
                if(i==points.Length-1)continue;
                int v=i*2,k=i*6;triangles[k]=v;triangles[k+1]=v+1;triangles[k+2]=v+2;triangles[k+3]=v+1;triangles[k+4]=v+3;triangles[k+5]=v+2;
            }
            var source=new UnityEngine.Mesh{name=road.id+" source",indexFormat=UnityEngine.Rendering.IndexFormat.UInt32};
            source.vertices=vertices;source.triangles=triangles;source.RecalculateNormals();source.RecalculateBounds();
            AssetDatabase.CreateAsset(source,folder+"/"+road.id+"_source.asset");
            var original=UnityEngine.Object.Instantiate(source);original.name=road.id+" original";
            AssetDatabase.CreateAsset(original,folder+"/"+road.id+"_original.asset");
            var baked=UnityEngine.Object.Instantiate(source);baked.name=road.id+" baked";
            for(int i=0;i<vertices.Length;i++)vertices[i].y+=terrain.SampleHeight(vertices[i])+terrain.transform.position.y+.19f;
            baked.vertices=vertices;baked.RecalculateNormals();baked.RecalculateBounds();
            AssetDatabase.CreateAsset(baked,folder+"/"+road.id+"_baked.asset");
            var g=new GameObject("Road "+road.osmId+" restored connection");g.transform.SetParent(root.transform,false);result.RegisterObjectCreation(g);
            g.AddComponent<MeshFilter>().sharedMesh=baked;g.AddComponent<MeshRenderer>().sharedMaterial=material;g.AddComponent<MeshCollider>().sharedMesh=baked;
            var binding=g.AddComponent<CampusTerrainBinding>();binding.kind=CampusTerrainBinding.BindingKind.Surface;binding.originalMesh=original;binding.sourceMesh=source;binding.bakedMesh=baked;binding.surfaceOffset=.19f;binding.sourceId="way/"+road.osmId;binding.verifiedAccess=false;
            GameObjectUtility.SetStaticEditorFlags(g,StaticEditorFlags.BatchingStatic|StaticEditorFlags.OccludeeStatic);
            result.Log(g.name+" vertices="+vertices.Length);
        }
        AssetDatabase.SaveAssets();EditorSceneManager.SaveScene(SceneManager.GetActiveScene());
        result.Log("Restored cached OSM geometry; terrain bindings and colliders saved. Access remains unverified.");
    }
}
