using System;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;
using CampusSim;

public static class CampusEntranceAuthoring
{
    [Serializable] class Draft { public Landmark[] landmarks; }
    [Serializable] class Landmark { public string id,name,featureId; public Entrance[] entrances; }
    [Serializable] class Entrance { public string id,role; public float x,z,heading; }
    [MenuItem("Campus/Map/Relocate Anniversary Entrances")]
    public static void RelocateAnniversary()
    {
        if(SceneManager.GetActiveScene().path!="Assets/CampusSim/Scenes/CampusTerrain.unity" || EditorApplication.isPlayingOrWillChangePlaymode)throw new Exception("Open working scene.");
        var draft=JsonUtility.FromJson<Draft>(File.ReadAllText("Assets/CampusSim/Data/entrance_drafts.json")).landmarks.Single(d=>d.id=="anniversary_60");
        var landmark=UnityEngine.Object.FindObjectsByType<CampusLandmarkEntrances>(FindObjectsSortMode.None).Single(l=>l.landmarkId==draft.id);
        var map=GameObject.Find("INHA UNIVERSITY").transform;
        foreach(var entry in draft.entrances)
        {
            var portal=entry.role=="accessible"?landmark.accessibleEntrance:landmark.generalEntrance;
            Undo.RecordObject(portal,"Relocate entrance draft");
            portal.position=map.TransformPoint(new Vector3(entry.x,0,entry.z))+Vector3.up*landmark.transform.position.y;
            portal.rotation=map.rotation*Quaternion.Euler(0,entry.heading,0);
        }
        GradePads();
    }
    [MenuItem("Campus/Map/Grade Entrance Draft Pads")]
    public static void GradePads()
    {
        if(SceneManager.GetActiveScene().path!="Assets/CampusSim/Scenes/CampusTerrain.unity")throw new Exception("Open working scene.");
        var terrain=UnityEngine.Object.FindObjectsByType<Terrain>(FindObjectsSortMode.None).Single();
        var data=terrain.terrainData;int n=data.heightmapResolution;var heights=data.GetHeights(0,0,n,n);var origin=terrain.transform.position;
        foreach(var landmark in UnityEngine.Object.FindObjectsByType<CampusLandmarkEntrances>(FindObjectsSortMode.None))
        foreach(var entry in new[]{landmark.generalEntrance,landmark.accessibleEntrance})
        {
            if(!entry)continue;
            var p=entry.position+entry.forward*1.5f;float target=(entry.position.y-origin.y)/data.size.y;
            for(int z=0;z<n;z++)for(int x=0;x<n;x++)
            {
                float wx=origin.x+x*data.size.x/(n-1),wz=origin.z+z*data.size.z/(n-1);
                float distance=Vector2.Distance(new Vector2(wx,wz),new Vector2(p.x,p.z));
                if(distance<14)heights[z,x]=Mathf.Lerp(target,heights[z,x],Mathf.SmoothStep(0,1,Mathf.Max(0,distance-6)/8));
            }
        }
        data.SetHeights(0,0,heights);CampusTerrainAuthoring.Bake();EditorSceneManager.SaveScene(SceneManager.GetActiveScene());
    }
    [MenuItem("Campus/Map/Inspect Separate Entrances")]
    public static void Inspect()
    {
        var all=UnityEngine.Object.FindObjectsByType<CampusLandmarkEntrances>(FindObjectsSortMode.None);
        var report=new System.Text.StringBuilder();
        var camera=Camera.main;var pos=camera.transform.position;var rot=camera.transform.rotation;var previous=camera.targetTexture;var active=RenderTexture.active;
        var rt=new RenderTexture(1000,700,24);var image=new Texture2D(1000,700,TextureFormat.RGB24,false);
        try
        {
            foreach(var landmark in all)
            {
                if(!landmark.generalEntrance||!landmark.accessibleEntrance){report.AppendLine(landmark.landmarkId+" INCOMPLETE: missing entrance pair; "+landmark.verificationStatus);continue;}
                float distance=Vector3.Distance(landmark.generalEntrance.position,landmark.accessibleEntrance.position);
                if(distance<8)throw new Exception("Entrances insufficiently separated.");
                var binding=landmark.GetComponent<CampusTerrainBinding>();
                if(!binding.heightAnchor && !binding.sourceId.StartsWith("node/") && !binding.sourceId.StartsWith("way/"))throw new Exception("Missing building height anchor.");
                report.AppendLine(landmark.landmarkId+" separation="+distance+"m, heightAnchor="+(binding.heightAnchor?binding.heightAnchor.name:binding.sourceId)+", status="+landmark.verificationStatus);
                var center=(landmark.generalEntrance.position+landmark.accessibleEntrance.position)*.5f+Vector3.up;
                camera.transform.position=center+landmark.generalEntrance.forward*Mathf.Max(20,distance*1.1f)+Vector3.up*Mathf.Max(8,distance*.35f);
                camera.transform.LookAt(center);camera.targetTexture=rt;camera.Render();RenderTexture.active=rt;
                image.ReadPixels(new Rect(0,0,1000,700),0,0);image.Apply();
                File.WriteAllBytes("Docs/MapResearch/Iterations/entrances-"+landmark.landmarkId+".png",image.EncodeToPNG());
                if(landmark.landmarkId=="anniversary_60")
                foreach(var portal in new[]{landmark.generalEntrance,landmark.accessibleEntrance})
                {
                    camera.transform.position=portal.position+portal.forward*5+Vector3.up*3;
                    camera.transform.LookAt(portal.position+Vector3.up*1.4f);camera.Render();RenderTexture.active=rt;
                    image.ReadPixels(new Rect(0,0,1000,700),0,0);image.Apply();
                    File.WriteAllBytes("Docs/MapResearch/Iterations/"+portal.name+".png",image.EncodeToPNG());
                }
            }
        }
        finally{camera.transform.SetPositionAndRotation(pos,rot);camera.targetTexture=previous;RenderTexture.active=active;rt.Release();UnityEngine.Object.DestroyImmediate(rt);UnityEngine.Object.DestroyImmediate(image);}
        File.WriteAllText("Docs/MapResearch/Iterations/entrance-validation.txt",report.ToString());
    }
    [MenuItem("Campus/Map/Build Separate Landmark Entrances")]
    public static void Build()
    {
        if(SceneManager.GetActiveScene().path!="Assets/CampusSim/Scenes/CampusTerrain.unity" || EditorApplication.isPlayingOrWillChangePlaymode)throw new Exception("Open working scene.");
        if(GameObject.Find("Landmark entrance drafts"))throw new Exception("Entrance drafts already exist.");
        var draft=JsonUtility.FromJson<Draft>(File.ReadAllText("Assets/CampusSim/Data/entrance_drafts.json"));
        var map=GameObject.Find("INHA UNIVERSITY").transform;
        var bindings=UnityEngine.Object.FindObjectsByType<CampusTerrainBinding>(FindObjectsSortMode.None);
        var buildings=draft.landmarks.Select(d=>bindings.Single(b=>b.kind==CampusTerrainBinding.BindingKind.Building && b.sourceId.EndsWith("OSM "+d.featureId))).ToArray();
        var root=new GameObject("Landmark entrance drafts").transform;
        var white=AssetDatabase.LoadAssetAtPath<Material>("Assets/CampusSim/Generated/Hub/Pearl.mat");
        var dark=AssetDatabase.LoadAssetAtPath<Material>("Assets/CampusSim/Generated/Hub/Graphite.mat");
        var teal=AssetDatabase.LoadAssetAtPath<Material>("Assets/CampusSim/Generated/Hub/Signal teal.mat");
        if(!white||!dark||!teal)throw new Exception("Hub palette required.");
        int count=0;
        for(int i=0;i<draft.landmarks.Length;i++)
        {
            var item=draft.landmarks[i];var group=new GameObject(item.name+" | proposed entrances").transform;group.SetParent(root,false);
            var bind=group.gameObject.AddComponent<CampusTerrainBinding>();bind.kind=CampusTerrainBinding.BindingKind.Building;
            bind.originalPosition=Vector3.zero;bind.samplePoint=buildings[i].samplePoint;bind.heightAnchor=buildings[i];bind.sourceId=item.id+" proposed entrances";
            var metadata=group.gameObject.AddComponent<CampusLandmarkEntrances>();metadata.landmarkId=item.id;
            foreach(var entry in item.entrances)
            {
                var portal=new GameObject(entry.id).transform;portal.SetParent(group,false);
                portal.position=map.TransformPoint(new Vector3(entry.x,0,entry.z));portal.rotation=map.rotation*Quaternion.Euler(0,entry.heading,0);
                Box(portal,"Door",new Vector3(0,1.25f,0),new Vector3(2.4f,2.5f,.15f),dark);
                foreach(float x in new[]{-1.35f,1.35f})Box(portal,"Frame",new Vector3(x,1.4f,0),new Vector3(.2f,2.8f,.4f),white);
                bool accessible=entry.role=="accessible";
                Box(portal,"Canopy",new Vector3(0,2.9f,.7f),new Vector3(3.2f,.2f,2),accessible?teal:white);
                Box(portal,"Threshold",new Vector3(0,.08f,1.3f),new Vector3(3.2f,.12f,2.6f),white);
                var sign=new GameObject("Entrance role").transform;sign.SetParent(portal,false);sign.localPosition=new Vector3(0,2.55f,.3f);sign.localRotation=Quaternion.Euler(0,180,0);
                var label=sign.gameObject.AddComponent<TextMesh>();label.text=accessible?"ACCESS ONLY":"GENERAL";label.fontSize=64;label.characterSize=.035f;label.anchor=TextAnchor.MiddleCenter;label.color=Color.white;
                if(accessible)metadata.accessibleEntrance=portal;else metadata.generalEntrance=portal;count++;
            }
        }
        CampusTerrainAuthoring.Bake();EditorSceneManager.SaveScene(SceneManager.GetActiveScene());
        File.WriteAllText("Docs/MapResearch/Iterations/entrances.txt","Landmarks=4; separate entrances="+count+"; required landmarks pending=7.\nSynthetic facade proposals, not verified real entrances or usable vehicle stops.\n");
    }
    static void Box(Transform parent,string name,Vector3 position,Vector3 scale,Material material)
    {
        var g=GameObject.CreatePrimitive(PrimitiveType.Cube);g.name=name;g.transform.SetParent(parent,false);g.transform.localPosition=position;g.transform.localScale=scale;g.GetComponent<Renderer>().sharedMaterial=material;
    }
}
