using System;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;
using CampusSim;

public static class CampusRoadExtensions
{
    [Serializable] class Set { public Road[] roads; }
    [Serializable] class Road { public string id,osmId,name,kind;public float width;public bool pedestrian,accessVerified;public Vector3[] points; }
    [MenuItem("Campus/Map/Build OSM Road Extensions")]
    public static void Build() => BuildCore(false);
    [MenuItem("Campus/Map/Rebuild Exact Road Boundary Joins")]
    public static void Rebuild() => BuildCore(true);
    [MenuItem("Campus/Map/Keep Campus Landmark Connection Roads Only")]
    public static void BuildScoped() => BuildCore(true,true);
    static void BuildCore(bool corrected,bool scoped=false)
    {
        if(EditorApplication.isPlayingOrWillChangePlaymode||SceneManager.GetActiveScene().path!="Assets/CampusSim/Scenes/CampusTerrain.unity")throw new Exception("Open working scene.");
        string rootName=scoped?"Campus scoped connections":corrected?"OSM road extensions exact clips":"OSM road extensions";
        if(GameObject.Find(rootName))throw new Exception("Extensions already exist; preserve edits.");
        string folder="Assets/CampusSim/Generated/"+(scoped?"RoadConnectionsScoped":corrected?"RoadExtensionsExact":"RoadExtensions");Directory.CreateDirectory(folder);AssetDatabase.Refresh();
        Material Mat(string name,Color color){var m=new Material(Shader.Find("FlatKit/Stylized Surface")){name=name};m.SetColor("_BaseColor",color);m.SetColor("_ColorDim",color*.8f);m.EnableKeyword("_CELPRIMARYMODE_SINGLE");AssetDatabase.CreateAsset(m,folder+"/"+name+".mat");return m;}
        var asphalt=Mat("Extension asphalt",new Color(.23f,.27f,.28f));var paving=Mat("Extension walkway",new Color(.58f,.59f,.53f));
        var map=GameObject.Find("INHA UNIVERSITY").transform;var root=new GameObject(rootName).transform;
        var set=JsonUtility.FromJson<Set>(File.ReadAllText("Assets/CampusSim/Data/"+(scoped?"road_connections_scoped.json":"road_extensions.json")));
        foreach(var road in set.roads)
        {
            var points=road.points.Select(map.TransformPoint).ToArray();var vertices=new Vector3[points.Length*2];var triangles=new int[(points.Length-1)*6];
            for(int i=0;i<points.Length;i++)
            {
                var tangent=points[Mathf.Min(i+1,points.Length-1)]-points[Mathf.Max(i-1,0)];tangent.y=0;tangent.Normalize();var side=new Vector3(-tangent.z,0,tangent.x)*road.width*.5f;
                vertices[i*2]=points[i]-side;vertices[i*2+1]=points[i]+side;
                if(i==points.Length-1)continue;int v=i*2,k=i*6;triangles[k]=v;triangles[k+1]=v+1;triangles[k+2]=v+2;triangles[k+3]=v+1;triangles[k+4]=v+3;triangles[k+5]=v+2;
            }
            var source=new Mesh{name=road.id+" OSM road",indexFormat=UnityEngine.Rendering.IndexFormat.UInt32};source.vertices=vertices;source.triangles=triangles;source.RecalculateNormals();source.RecalculateBounds();AssetDatabase.CreateAsset(source,folder+"/"+road.id+"_source.asset");
            var original=UnityEngine.Object.Instantiate(source);AssetDatabase.CreateAsset(original,folder+"/"+road.id+"_original.asset");
            var baked=UnityEngine.Object.Instantiate(source);AssetDatabase.CreateAsset(baked,folder+"/"+road.id+"_baked.asset");
            var g=new GameObject((road.pedestrian?"Walkway ":"Road ")+road.osmId+" "+road.name);g.transform.SetParent(root,false);g.AddComponent<MeshFilter>().sharedMesh=baked;g.AddComponent<MeshRenderer>().sharedMaterial=road.pedestrian?paving:asphalt;g.AddComponent<MeshCollider>().sharedMesh=baked;
            GameObjectUtility.SetStaticEditorFlags(g,StaticEditorFlags.BatchingStatic|StaticEditorFlags.OccludeeStatic);
            var binding=g.AddComponent<CampusTerrainBinding>();binding.kind=CampusTerrainBinding.BindingKind.Surface;binding.originalMesh=original;binding.sourceMesh=source;binding.bakedMesh=baked;binding.sourceId="way/"+road.osmId;binding.surfaceOffset=.19f;binding.verifiedAccess=false;
        }
        if(corrected){var old=GameObject.Find("OSM road extensions");if(old){old.SetActive(false);old.tag="EditorOnly";}}
        if(scoped){var old=GameObject.Find("OSM road extensions exact clips");if(old){old.SetActive(false);old.tag="EditorOnly";}}
        if(scoped)foreach(var old in SceneManager.GetActiveScene().GetRootGameObjects().Where(g=>g.name=="OSM road extensions"||g.name=="OSM road extensions exact clips")){old.SetActive(false);old.tag="EditorOnly";}
        CampusTerrainAuthoring.Bake();AssetDatabase.SaveAssets();EditorSceneManager.SaveScene(SceneManager.GetActiveScene());
        File.WriteAllText("Docs/MapResearch/Iterations/"+(scoped?"scoped-road-application.txt":"road-extensions.txt"),"OSM road pieces="+set.roads.Length+"; width synthetic; access unverified. Geometry follows current Terrain through Bake. No runtime graph or complete entrance connection claimed.\n");
    }
}
