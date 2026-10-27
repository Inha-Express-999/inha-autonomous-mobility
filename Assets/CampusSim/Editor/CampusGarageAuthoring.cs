using System;
using System.IO;
using System.Linq;
using CampusSim;
using InhaExpress.Client.MapClient;
using InhaExpress.Client.Presentation;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

/// <summary>Simulation garages on existing hub roads; no invented access edges.</summary>
public static class CampusGarageAuthoring
{
    [Serializable] private class Layout { public string geometry_hash; public bool simulation_only; public Home[] homes; }
    [Serializable] private class Home { public string vehicle_id, hub_id, name; public float x,z,yaw,center_x,center_z,road_width; }
    private const string RootName="Campus hub garages";
    private const string Folder="Assets/CampusSim/Generated/Garages";

    private static RaycastHit Road(float x,float z)
    {
        var hits=Physics.RaycastAll(new Vector3(x,100,z),Vector3.down,300)
            .Where(h=>h.collider && h.collider.GetComponent<CampusTerrainBinding>() is CampusTerrainBinding b && b.roadHeightSource)
            .OrderByDescending(h=>h.point.y).ToArray();
        if(hits.Length==0)throw new InvalidOperationException("No authored asphalt at garage "+x+", "+z);
        return hits[0];
    }

    [MenuItem("Campus/Server Map/Fit Vehicle Spawn Heights To Roads")]
    public static void FitVehicleSpawnsToRoad()
    {
        if(EditorApplication.isPlayingOrWillChangePlaymode)
            throw new InvalidOperationException("Vehicle spawn fitting is edit-mode only.");
        Physics.SyncTransforms();
        foreach(var car in UnityEngine.Object.FindObjectsByType<VehicleController>(FindObjectsSortMode.None))
        {
            if(car.gameObject.scene.path!="Assets/CampusSim/Scenes/CampusTerrain.unity")continue;
            var wheels=car.GetComponentsInChildren<WheelCollider>();
            if(wheels.Length!=4)throw new InvalidOperationException("Expected four wheels: "+car.name);
            // Preserve X/Z and heading. Raise/lower only the authored spawn so
            // each tyre starts above the latest baked asphalt, not the old DSM.
            float delta=float.NegativeInfinity;
            foreach(var wheel in wheels)
            {
                var center=wheel.transform.TransformPoint(wheel.center);
                float radius=wheel.radius*wheel.transform.lossyScale.y;
                delta=Mathf.Max(delta,Road(center.x,center.z).point.y-(center.y-radius)+.05f);
            }
            Undo.RecordObject(car.transform,"Fit saved vehicle spawn to asphalt");
            car.transform.position+=Vector3.up*delta;
            PrefabUtility.RecordPrefabInstancePropertyModifications(car.transform);
            EditorUtility.SetDirty(car.transform);
            EditorSceneManager.MarkSceneDirty(car.gameObject.scene);
            Debug.Log("ROAD_SPAWN_FIT "+car.OperatorVehicleId+" deltaY="+delta);
        }
    }

    [MenuItem("Campus/Server Map/Build Home Garages")]
    public static void Build()
    {
        if(EditorApplication.isPlayingOrWillChangePlaymode)throw new InvalidOperationException("Use Edit Mode.");
        var scene=SceneManager.GetActiveScene();
        if(scene.path!="Assets/CampusSim/Scenes/CampusTerrain.unity")throw new InvalidOperationException("Open CampusTerrain.");
        var layout=JsonUtility.FromJson<Layout>(File.ReadAllText("Assets/CampusSim/Data/home_garages.json"));
        var map=UnityEngine.Object.FindFirstObjectByType<CampusServerMap>();
        if(layout?.homes==null || layout.homes.Length!=3 || !layout.simulation_only || map==null || layout.geometry_hash!=map.geometryHash)
            throw new InvalidOperationException("Garage layout/map mismatch.");
        var vehicles=UnityEngine.Object.FindObjectsByType<VehicleController>(FindObjectsSortMode.None);
        var template=vehicles.Single(v=>v.OperatorVehicleId=="V01");
        Physics.SyncTransforms();
        // Validate all surfaces before modifying the scene.
        var ground=layout.homes.Select(h=>Road(h.x,h.z)).ToArray();
        var centers=layout.homes.Select(h=>Road(h.center_x,h.center_z)).ToArray();
        CampusServicePointVisuals.Migrate(map);
        Directory.CreateDirectory(Folder);if(!AssetDatabase.IsValidFolder(Folder))AssetDatabase.Refresh();
        var dark=Material("Garage graphite",new Color(.08f,.12f,.18f));
        var white=Material("Garage white",new Color(.92f,.95f,.98f));
        var colors=new[]{new Color(.086f,.467f,1),new Color(.486f,.361f,.906f),new Color(.063f,.659f,.584f)};
        var font=Resources.Load<Font>("Freesentation-Bold");
        if(font)font.RequestCharactersInTexture(string.Join("",layout.homes.Select(h=>h.vehicle_id+"  "+h.name+" 차고")),64);
        for(int i=0;i<layout.homes.Length;i++)
        {
            var h=layout.homes[i];
            var marker=map.markers.Single(m=>m.kind==ServerMapMarker.MarkerKind.Hub && m.serverId==h.hub_id);
            var attached=marker.GetComponentsInChildren<ServicePointVisual>(true)
                .Where(v=>v.pointKind==marker.kind && v.pointId==marker.serverId).ToArray();
            if(attached.Length>1)throw new InvalidOperationException("Duplicate garage model: "+h.hub_id);
            if(attached.Length==1)continue;
            var car=vehicles.SingleOrDefault(v=>v.OperatorVehicleId==h.vehicle_id);
            if(car==null)
            {
                var copy=UnityEngine.Object.Instantiate(template.gameObject);
                copy.name=h.vehicle_id+" | Hub vehicle";
                Undo.RegisterCreatedObjectUndo(copy,"Create hub vehicle");
                car=copy.GetComponent<VehicleController>();
                var serialized=new SerializedObject(car);serialized.FindProperty("operatorVehicleId").stringValue=h.vehicle_id;serialized.ApplyModifiedProperties();
            }
            float wheelBottom=car.GetComponentsInChildren<WheelCollider>().Min(w=>
                car.transform.InverseTransformPoint(w.transform.TransformPoint(w.center)).y-w.radius);
            Undo.RecordObject(car.transform,"Set home bay");
            car.transform.SetPositionAndRotation(new Vector3(h.x,ground[i].point.y-wheelBottom+.05f,h.z),Quaternion.Euler(0,h.yaw,0));
            PrefabUtility.RecordPrefabInstancePropertyModifications(car.transform);
            // The legacy imported root also renders an unscaled copy of the
            // mesh. The fitted child is the visible vehicle; keep Physics intact.
            var duplicate=car.GetComponent<MeshRenderer>();
            if(duplicate && car.GetComponentsInChildren<MeshRenderer>().Any(r=>r!=duplicate && r.name=="Annyoung Car Mesh"))
            { Undo.RecordObject(duplicate,"Hide duplicate imported vehicle mesh");duplicate.enabled=false;PrefabUtility.RecordPrefabInstancePropertyModifications(duplicate); }
            var garage=new GameObject(h.hub_id+" | "+h.vehicle_id+" 차고").transform;
            Undo.RegisterCreatedObjectUndo(garage.gameObject,"Create Hub display model");
            garage.SetPositionAndRotation(new Vector3(h.center_x,centers[i].point.y,h.center_z),Quaternion.Euler(0,h.yaw,0));
            CampusServicePointVisuals.Bind(garage,marker);
            var accent=Material(h.vehicle_id+" garage accent",colors[i]);
            float half=h.road_width*.5f+.4f;
            // Open both ends so each legal arrival direction can park inside.
            // Roof/posts are visual-only; asphalt remains the sole drive surface.
            for(int side=-1;side<=1;side+=2)
            {
                for(int end=-1;end<=1;end+=2)
                    Box(garage,"Post",new Vector3(side*half,1.7f,end*4.2f),new Vector3(.18f,3.4f,.18f),dark);
                Box(garage,"Side panel",new Vector3(side*half,1.5f,0),new Vector3(.12f,2.2f,5.8f),white);
                Box(garage,"Bay line",new Vector3(side*half,.035f,0),new Vector3(.08f,.03f,8.8f),accent);
            }
            Box(garage,"Roof",new Vector3(0,3.48f,0),new Vector3(half*2+.5f,.16f,9.2f),white);
            for(int end=-1;end<=1;end+=2)
            {
                Box(garage,"Header",new Vector3(0,3.1f,end*4.5f),new Vector3(half*2,.55f,.12f),accent);
                var label=new GameObject("Garage label");label.transform.SetParent(garage,false);
                label.transform.localPosition=new Vector3(0,3.1f,end*4.58f);
                label.transform.localRotation=Quaternion.Euler(0,end>0?180:0,0);
                var text=label.AddComponent<TextMesh>();text.text=h.vehicle_id+"  "+h.name+" 차고";
                text.font=Resources.Load<Font>("Freesentation-Bold");text.fontSize=64;text.characterSize=.10f;text.anchor=TextAnchor.MiddleCenter;text.color=Color.white;
                if(text.font)
                {
                    text.font.RequestCharactersInTexture(text.text,text.fontSize);
                    var labelPath=Folder+"/"+h.vehicle_id+"-label-"+end+".mat";
                    var labelMaterial=AssetDatabase.LoadAssetAtPath<Material>(labelPath);
                    if(!labelMaterial){labelMaterial=new Material(text.font.material);AssetDatabase.CreateAsset(labelMaterial,labelPath);}
                    else labelMaterial.CopyPropertiesFromMaterial(text.font.material);
                    labelMaterial.shader=Shader.Find("CampusSim/Garage World Text");
                    labelMaterial.mainTexture=text.font.material.mainTexture;
                    labelMaterial.name=Path.GetFileNameWithoutExtension(labelPath);
                    labelMaterial.SetInt("unity_GUIZTestMode",(int)CompareFunction.LessEqual);
                    EditorUtility.SetDirty(labelMaterial);
                    label.GetComponent<MeshRenderer>().sharedMaterial=labelMaterial;
                }
            }
            Debug.Log("GARAGE_HOME "+h.vehicle_id+" "+car.transform.position+" node="+h.hub_id);
        }
        EditorSceneManager.MarkSceneDirty(scene);EditorSceneManager.SaveScene(scene);AssetDatabase.SaveAssets();
        Debug.Log("GARAGES_SAVED count=3 vehicles=3 colliders=0 existing_road_only=true");
    }
    private static Material Material(string name,Color color)
    {
        var path=Folder+"/"+name+".mat";var material=AssetDatabase.LoadAssetAtPath<Material>(path);
        if(!material){material=new Material(Shader.Find("Universal Render Pipeline/Unlit"));AssetDatabase.CreateAsset(material,path);}
        material.SetColor("_BaseColor",color);EditorUtility.SetDirty(material);return material;
    }
    private static void Box(Transform parent,string name,Vector3 position,Vector3 size,Material material)
    {
        var box=GameObject.CreatePrimitive(PrimitiveType.Cube);box.name=name;box.transform.SetParent(parent,false);
        box.transform.localPosition=position;box.transform.localScale=size;UnityEngine.Object.DestroyImmediate(box.GetComponent<Collider>());
        var renderer=box.GetComponent<MeshRenderer>();renderer.sharedMaterial=material;renderer.shadowCastingMode=ShadowCastingMode.Off;
        GameObjectUtility.SetStaticEditorFlags(box,StaticEditorFlags.BatchingStatic);
    }
}
