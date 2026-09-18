using System;
using System.IO;
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;
using CampusSim;
public static class CampusStationAuthoring
{
    [MenuItem("Campus/Map/Build Inha Station Exit Seven Draft")]
    public static void Build()
    {
        if(SceneManager.GetActiveScene().path!="Assets/CampusSim/Scenes/CampusTerrain.unity"||EditorApplication.isPlayingOrWillChangePlaymode)throw new Exception("Open working scene.");
        if(GameObject.Find("Inha Station | Exit 7 draft"))throw new Exception("Station exists.");
        var map=GameObject.Find("INHA UNIVERSITY").transform;
        var root=new GameObject("Inha Station | Exit 7 draft").transform;
        var p=new Vector3((float)((126.6502630-126.6535)*111320*Math.Cos(37.4506*Math.PI/180)),0,(float)((37.4478074-37.4506)*110980));
        root.position=map.TransformPoint(p);root.rotation=map.rotation;
        var bind=root.gameObject.AddComponent<CampusTerrainBinding>();bind.kind=CampusTerrainBinding.BindingKind.Building;bind.originalPosition=root.position;bind.samplePoint=root.position;bind.sourceId="node/4741793213";
        var landmark=root.gameObject.AddComponent<CampusLandmarkEntrances>();landmark.landmarkId="inha_station";landmark.verificationStatus="osm_exit_7_wheelchair_no_accessible_entrance_unresolved";
        var entry=new GameObject("inha_station_general_exit_7").transform;entry.SetParent(root,false);entry.localPosition=new Vector3(0,.2f,-4);landmark.generalEntrance=entry;
        var white=AssetDatabase.LoadAssetAtPath<Material>("Assets/CampusSim/Generated/Hub/Pearl.mat");var dark=AssetDatabase.LoadAssetAtPath<Material>("Assets/CampusSim/Generated/Hub/Graphite.mat");var glass=AssetDatabase.LoadAssetAtPath<Material>("Assets/CampusSim/Generated/Hub/Blue glass.mat");
        Box("Foundation",new Vector3(0,.1f,0),new Vector3(5,.2f,9),white);
        Box("Covered passage",new Vector3(0,.22f,0),new Vector3(3.4f,.12f,8),dark);
        foreach(float x in new[]{-2f,2f}){Box("Side wall",new Vector3(x,.65f,0),new Vector3(.3f,1.1f,8),white);for(int i=0;i<3;i++)Box("Support",new Vector3(x,1.8f,-3+i*3),new Vector3(.15f,3.4f,.15f),white);}
        Box("Canopy",new Vector3(0,3.6f,0),new Vector3(4.6f,.2f,9),glass);Box("Sign",new Vector3(0,3.2f,-4.55f),new Vector3(4.6f,.65f,.1f),dark);
        var label=new GameObject("Station name");label.transform.SetParent(root,false);label.transform.localPosition=new Vector3(0,3.2f,-4.62f);var text=label.AddComponent<TextMesh>();text.text="INHA UNIV. / 7";text.fontSize=64;text.characterSize=.04f;text.anchor=TextAnchor.MiddleCenter;text.color=Color.white;
        void Box(string name,Vector3 position,Vector3 size,Material material){var g=GameObject.CreatePrimitive(PrimitiveType.Cube);g.name=name;g.transform.SetParent(root,false);g.transform.localPosition=position;g.transform.localScale=size;g.GetComponent<Renderer>().sharedMaterial=material;}
        CampusTerrainAuthoring.Bake();EditorSceneManager.SaveScene(SceneManager.GetActiveScene());
        File.WriteAllText("Docs/MapResearch/Iterations/station-draft.txt","Exit 7 node/4741793213 at 126.6502630 37.4478074. General entrance pavilion only; dimensions/orientation synthetic. OSM wheelchair=no. Accessible entrance null/unresolved; no underground route or vehicle Stop.\n");
    }
    [MenuItem("Campus/Map/Capture Station Draft")]
    public static void Capture()
    {
        var root=GameObject.Find("Inha Station | Exit 7 draft").transform;var camera=Camera.main;var p=camera.transform.position;var r=camera.transform.rotation;
        try{camera.transform.position=root.TransformPoint(new Vector3(-10,8,-15));camera.transform.LookAt(root.position+Vector3.up);CampusTerrainAuthoring.Capture();File.Copy("Docs/MapResearch/Iterations/terrain-overview.png","Docs/MapResearch/Iterations/station.png",true);}
        finally{camera.transform.SetPositionAndRotation(p,r);}
    }
}
