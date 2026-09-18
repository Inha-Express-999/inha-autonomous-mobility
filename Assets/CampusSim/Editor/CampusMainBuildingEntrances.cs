using System;
using System.Linq;
using CampusSim;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

public static class CampusMainBuildingEntrances
{
    [MenuItem("Campus/Map/Create Main building Entrance Drafts")]
    public static void Apply()
    {
        if(EditorApplication.isPlayingOrWillChangePlaymode||SceneManager.GetActiveScene().path!="Assets/CampusSim/Scenes/CampusTerrain.unity")throw new InvalidOperationException("Open working scene in edit mode.");
        var building=UnityEngine.Object.FindObjectsByType<CampusTerrainBinding>(FindObjectsSortMode.None).Single(b=>b.name.StartsWith("본관 | OSM "));
        var existing=UnityEngine.Object.FindObjectsByType<CampusLandmarkEntrances>(FindObjectsSortMode.None).SingleOrDefault(l=>l.landmarkId=="main_building");
        if(existing)throw new InvalidOperationException("Main building entrances already exist; edit instead of duplicating.");
        var front=building.GetComponentsInChildren<MeshCollider>().Single(c=>c.name=="Facades");
        var n=new Vector3(-.48f,0,-.88f).normalized;var axis=new Vector3(.8728f,0,-.488f).normalized;
        var positions=new Vector3[2];
        for(int i=0;i<2;i++)
        {
            var origin=building.transform.TransformPoint(new Vector3(76.4485f,1.2f,-142.8925f)+axis*(i==0?-22.736f:22.736f)+n*15);
            if(!front.Raycast(new Ray(origin,building.transform.TransformDirection(-n)),out var hit,40))throw new InvalidOperationException("Main building facade not found.");
            positions[i]=hit.point+building.transform.TransformDirection(n)*1.12f-Vector3.up*1.2f;
        }
        var stone=AssetDatabase.LoadAssetAtPath<Material>("Assets/CampusSim/Generated/Dormitories/Ivory.mat");
        var glass=AssetDatabase.LoadAssetAtPath<Material>("Assets/CampusSim/Generated/Dormitories/Windows.mat");
        if(!stone||!glass)throw new InvalidOperationException("Entrance materials unavailable.");
        var landmark=Undo.AddComponent<CampusLandmarkEntrances>(building.gameObject);landmark.landmarkId="main_building";
        landmark.verificationStatus="OSM 217958071 building; synthetic separated facade portals; actual doors and access unverified";landmark.routeValidated=false;
        for(int i=0;i<2;i++)
        {
            var portal=new GameObject(i==0?"main_building_general":"main_building_accessible").transform;Undo.RegisterCreatedObjectUndo(portal.gameObject,"Create main_building portal");portal.SetParent(building.transform,false);portal.SetPositionAndRotation(positions[i],Quaternion.LookRotation(building.transform.TransformDirection(n)));
            void Box(string name,Vector3 p,Vector3 size,Material mat){var g=GameObject.CreatePrimitive(PrimitiveType.Cube);Undo.RegisterCreatedObjectUndo(g,"Create main_building entrance element");g.name=name;g.transform.SetParent(portal,false);g.transform.localPosition=p;g.transform.localScale=size;g.GetComponent<Renderer>().sharedMaterial=mat;GameObjectUtility.SetStaticEditorFlags(g,StaticEditorFlags.BatchingStatic|StaticEditorFlags.OccludeeStatic);}
            Box("Door",new Vector3(0,1.2f,0),new Vector3(2.3f,2.4f,.16f),glass);
            Box("Canopy",new Vector3(0,2.7f,.6f),new Vector3(3.3f,.18f,1.8f),stone);
            Box("Landing",new Vector3(0,.05f,1),new Vector3(3.3f,.1f,2),stone);
            if(i==0)landmark.generalEntrance=portal;else landmark.accessibleEntrance=portal;
        }
        EditorUtility.SetDirty(landmark);
        CampusEntranceApproaches.FitAfterTerrainBake(true);
        CampusEntranceInventory.Export();
    }
}

