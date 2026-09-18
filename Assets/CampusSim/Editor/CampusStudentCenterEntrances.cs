using System;
using System.Linq;
using CampusSim;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

public static class CampusStudentCenterEntrances
{
    [MenuItem("Campus/Map/Create Student center Entrance Drafts")]
    public static void Apply()
    {
        if(EditorApplication.isPlayingOrWillChangePlaymode||SceneManager.GetActiveScene().path!="Assets/CampusSim/Scenes/CampusTerrain.unity")throw new InvalidOperationException("Open working scene in edit mode.");
        var building=UnityEngine.Object.FindObjectsByType<CampusTerrainBinding>(FindObjectsSortMode.None).Single(b=>b.name.StartsWith("학생회관 | OSM "));
        var existing=UnityEngine.Object.FindObjectsByType<CampusLandmarkEntrances>(FindObjectsSortMode.None).SingleOrDefault(l=>l.landmarkId=="student_center");
        if(existing)throw new InvalidOperationException("Student center entrances already exist; edit instead of duplicating.");
        var front=building.GetComponentsInChildren<MeshCollider>().Single(c=>c.name=="Facades");
        var n=new Vector3(-.956f,0,-.292f).normalized;var axis=new Vector3(.292f,0,-.956f).normalized;
        var positions=new Vector3[2];
        for(int i=0;i<2;i++)
        {
            var origin=building.transform.TransformPoint(new Vector3(266.127f,1.2f,-69.5345f)+axis*(i==0?-4:4)+n*15);
            if(!front.Raycast(new Ray(origin,building.transform.TransformDirection(-n)),out var hit,40))throw new InvalidOperationException("Student center facade not found.");
            positions[i]=hit.point+building.transform.TransformDirection(n)*.12f-Vector3.up*1.2f;
        }
        var stone=AssetDatabase.LoadAssetAtPath<Material>("Assets/CampusSim/Generated/Dormitories/Ivory.mat");
        var glass=AssetDatabase.LoadAssetAtPath<Material>("Assets/CampusSim/Generated/Dormitories/Windows.mat");
        if(!stone||!glass)throw new InvalidOperationException("Entrance materials unavailable.");
        var landmark=Undo.AddComponent<CampusLandmarkEntrances>(building.gameObject);landmark.landmarkId="student_center";
        landmark.verificationStatus="OSM 218038027 building; synthetic separated facade portals; actual doors and access unverified";landmark.routeValidated=false;
        for(int i=0;i<2;i++)
        {
            var portal=new GameObject(i==0?"student_center_general":"student_center_accessible").transform;Undo.RegisterCreatedObjectUndo(portal.gameObject,"Create student_center portal");portal.SetParent(building.transform,false);portal.SetPositionAndRotation(positions[i],Quaternion.LookRotation(building.transform.TransformDirection(n)));
            void Box(string name,Vector3 p,Vector3 size,Material mat){var g=GameObject.CreatePrimitive(PrimitiveType.Cube);Undo.RegisterCreatedObjectUndo(g,"Create student_center entrance element");g.name=name;g.transform.SetParent(portal,false);g.transform.localPosition=p;g.transform.localScale=size;g.GetComponent<Renderer>().sharedMaterial=mat;GameObjectUtility.SetStaticEditorFlags(g,StaticEditorFlags.BatchingStatic|StaticEditorFlags.OccludeeStatic);}
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

