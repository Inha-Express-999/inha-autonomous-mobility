using System;
using System.IO;
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;
using CampusSim;

public static class CampusPlazaAuthoring
{
    [MenuItem("Campus/Map/Build Biryong Plaza Entrances")]
    public static void Build()
    {
        if(EditorApplication.isPlayingOrWillChangePlaymode||SceneManager.GetActiveScene().path!="Assets/CampusSim/Scenes/CampusTerrain.unity")throw new Exception("Open working scene.");
        if(GameObject.Find("Biryong plaza | mapped entrance drafts"))throw new Exception("Plaza entrance drafts exist.");
        var map=GameObject.Find("INHA UNIVERSITY").transform;
        var root=new GameObject("Biryong plaza | mapped entrance drafts").transform;
        var points=new[]{Point(126.6563688,37.4496712),Point(126.6563578,37.4491754)};
        root.position=map.TransformPoint((points[0]+points[1])*.5f);root.rotation=map.rotation;
        var binding=root.gameObject.AddComponent<CampusTerrainBinding>();binding.kind=CampusTerrainBinding.BindingKind.Building;binding.originalPosition=root.position;binding.samplePoint=root.position;binding.sourceId="node/9767122969;node/9767122970";
        var metadata=root.gameObject.AddComponent<CampusLandmarkEntrances>();metadata.landmarkId="biryong_plaza";metadata.verificationStatus="osm_entrance_positions_synthetic_roles_and_pavilions_access_unknown";
        var white=AssetDatabase.LoadAssetAtPath<Material>("Assets/CampusSim/Generated/Hub/Pearl.mat");var dark=AssetDatabase.LoadAssetAtPath<Material>("Assets/CampusSim/Generated/Hub/Graphite.mat");var teal=AssetDatabase.LoadAssetAtPath<Material>("Assets/CampusSim/Generated/Hub/Signal teal.mat");
        for(int i=0;i<2;i++)
        {
            var portal=new GameObject(i==0?"biryong_plaza_general":"biryong_plaza_accessible").transform;portal.SetParent(root,false);portal.position=map.TransformPoint(points[i]);portal.rotation=map.rotation*Quaternion.Euler(0,270,0);
            Box(portal,"Landing",new Vector3(0,.1f,0),new Vector3(4,.2f,3),white);
            foreach(float x in new[]{-1.8f,1.8f})Box(portal,"Column",new Vector3(x,1.65f,0),new Vector3(.22f,3.1f,.3f),dark);
            Box(portal,"Canopy",new Vector3(0,3.25f,0),new Vector3(4.3f,.22f,3.5f),i==0?white:teal);
            Box(portal,"Sign backing",new Vector3(0,2.83f,1.35f),new Vector3(3.5f,.55f,.1f),dark);
            var sign=new GameObject("Entrance role").transform;sign.SetParent(portal,false);sign.localPosition=new Vector3(0,2.83f,1.42f);sign.localRotation=Quaternion.Euler(0,180,0);var text=sign.gameObject.AddComponent<TextMesh>();text.text=i==0?"BIRYONG / GENERAL":"BIRYONG / ACCESS ONLY";text.fontSize=64;text.characterSize=.03f;text.anchor=TextAnchor.MiddleCenter;text.color=Color.white;
            if(i==0)metadata.generalEntrance=portal;else metadata.accessibleEntrance=portal;
        }
        CampusTerrainAuthoring.Bake();EditorSceneManager.SaveScene(SceneManager.GetActiveScene());
        File.WriteAllText("Docs/MapResearch/Iterations/biryong-plaza.txt","OSM entrance nodes 9767122969 / 9767122970; attached to building way 218038027. General/access role split and pavilion design are synthetic. Actual step-free access, plaza boundary and vehicle Stops unverified.\n");
    }
    static Vector3 Point(double lon,double lat)=>new Vector3((float)((lon-126.6535)*111320*Math.Cos(37.4506*Math.PI/180)),0,(float)((lat-37.4506)*110980));
    static void Box(Transform root,string name,Vector3 p,Vector3 size,Material material){var g=GameObject.CreatePrimitive(PrimitiveType.Cube);g.name=name;g.transform.SetParent(root,false);g.transform.localPosition=p;g.transform.localScale=size;g.GetComponent<Renderer>().sharedMaterial=material;}
}
