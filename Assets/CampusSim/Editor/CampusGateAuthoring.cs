using System;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;
using CampusSim;

public static class CampusGateAuthoring
{
    [Serializable] class PassageSet { public Passage[] entrances; }
    [Serializable] class Passage { public string id; public float x,z,roadClearance; }
    [MenuItem("Campus/Map/Clear Gate Passages From Roads")]
    public static void ClearRoads()
    {
        if(SceneManager.GetActiveScene().path!="Assets/CampusSim/Scenes/CampusTerrain.unity")throw new Exception("Open working scene.");
        var passages=JsonUtility.FromJson<PassageSet>(File.ReadAllText("Assets/CampusSim/Data/gate_passage_drafts.json"));
        var map=GameObject.Find("INHA UNIVERSITY").transform;
        var gates=UnityEngine.Object.FindObjectsByType<CampusLandmarkEntrances>(FindObjectsSortMode.None).Where(g=>g.landmarkId=="main_gate"||g.landmarkId=="rear_gate").ToArray();
        foreach(var gate in gates)
        {
            foreach(var portal in new[]{gate.generalEntrance,gate.accessibleEntrance})
            {
                var p=passages.entrances.Single(e=>e.id==portal.name);
                portal.position=map.TransformPoint(new Vector3(p.x,0,p.z))+Vector3.up*gate.transform.position.y;
                foreach(var label in portal.GetComponentsInChildren<TextMesh>())label.transform.localRotation=Quaternion.Euler(0,180,0);
            }
            foreach(Transform child in gate.transform)
                if(child.name=="Gate identity plinth"||child.GetComponent<TextMesh>())child.gameObject.SetActive(false);
        }
        CampusEntranceAuthoring.GradePads();
    }
    [MenuItem("Campus/Map/Build Main And Rear Gate Drafts")]
    public static void Build()
    {
        if(SceneManager.GetActiveScene().path!="Assets/CampusSim/Scenes/CampusTerrain.unity" || EditorApplication.isPlayingOrWillChangePlaymode)throw new Exception("Open working scene.");
        if(GameObject.Find("Campus gate drafts"))throw new Exception("Gate drafts already exist.");
        var map=GameObject.Find("INHA UNIVERSITY").transform;
        var root=new GameObject("Campus gate drafts").transform;
        var white=AssetDatabase.LoadAssetAtPath<Material>("Assets/CampusSim/Generated/Hub/Pearl.mat");
        var dark=AssetDatabase.LoadAssetAtPath<Material>("Assets/CampusSim/Generated/Hub/Graphite.mat");
        var teal=AssetDatabase.LoadAssetAtPath<Material>("Assets/CampusSim/Generated/Hub/Signal teal.mat");
        if(!white||!dark||!teal)throw new Exception("Hub palette missing.");
        Create("main_gate","MAIN GATE","4741793208",126.6531848,37.4477024,0);
        Create("rear_gate","REAR GATE","4741793209",126.6564271,37.4511471,270);
        void Create(string id,string label,string node,double longitude,double latitude,float heading)
        {
            var gate=new GameObject(label+" | proposed layout").transform;gate.SetParent(root,false);
            var local=new Vector3((float)((longitude-126.6535)*111320*Math.Cos(37.4506*Math.PI/180)),0,(float)((latitude-37.4506)*110980));
            gate.position=map.TransformPoint(local);gate.rotation=map.rotation*Quaternion.Euler(0,heading,0);
            var binding=gate.gameObject.AddComponent<CampusTerrainBinding>();binding.kind=CampusTerrainBinding.BindingKind.Building;
            binding.originalPosition=gate.position;binding.samplePoint=gate.position;binding.sourceId="node/"+node;
            var landmark=gate.gameObject.AddComponent<CampusLandmarkEntrances>();landmark.landmarkId=id;
            landmark.verificationStatus="osm_gate_location_synthetic_layout_unverified_access";
            for(int i=0;i<2;i++)
            {
                bool access=i==1;float x=access?9:-9;
                var portal=new GameObject(id+(access?"_accessible":"_general")).transform;portal.SetParent(gate,false);portal.localPosition=new Vector3(x,0,0);
                foreach(float dx in new[]{-1.8f,1.8f})Box(portal,"Pillar",new Vector3(dx,1.55f,0),new Vector3(.35f,3.1f,.8f),white);
                Box(portal,"Entry canopy",new Vector3(0,3.2f,0),new Vector3(4.1f,.2f,1.8f),access?teal:white);
                Box(portal,"Walkway",new Vector3(0,.16f,0),new Vector3(3.6f,.12f,5),white);
                Label(portal,access?"ACCESS ONLY":"GENERAL",new Vector3(0,2.85f,-.45f),.035f);
                if(access)landmark.accessibleEntrance=portal;else landmark.generalEntrance=portal;
            }
            // Keep the middle clear for a future validated vehicle passage.
            Box(gate,"Gate identity plinth",new Vector3(-14,1.2f,0),new Vector3(4,2.4f,1),dark);
            Label(gate,label,new Vector3(-14,1.55f,-.56f),.032f);
            Label(gate,"INHA",new Vector3(-14,.8f,-.56f),.06f);
        }
        CampusTerrainAuthoring.Bake();EditorSceneManager.SaveScene(SceneManager.GetActiveScene());
        File.WriteAllText("Docs/MapResearch/Iterations/gate-drafts.txt","2 OSM-located gate landmarks, 4 separate passage anchors.\nNode 4741793208 main, 4741793209 rear.\nOrientation, structures and passage locations are synthetic; not verified access or vehicle routes.\n");
    }
    static void Box(Transform parent,string name,Vector3 p,Vector3 scale,Material material)
    {var g=GameObject.CreatePrimitive(PrimitiveType.Cube);g.name=name;g.transform.SetParent(parent,false);g.transform.localPosition=p;g.transform.localScale=scale;g.GetComponent<Renderer>().sharedMaterial=material;}
    static void Label(Transform parent,string text,Vector3 p,float size)
    {var g=new GameObject(text);g.transform.SetParent(parent,false);g.transform.localPosition=p;var t=g.AddComponent<TextMesh>();t.text=text;t.fontSize=64;t.characterSize=size;t.anchor=TextAnchor.MiddleCenter;t.color=Color.white;}
}
