using System;
using System.Linq;
using InhaExpress.Client.MapClient;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>Move legacy presentation roots below anchors without changing world poses.</summary>
public static class CampusServicePointVisuals
{
    [MenuItem("Campus/Server Map/Attach Models To Stops And Hubs")]
    public static void Attach()
    {
        if(EditorApplication.isPlayingOrWillChangePlaymode)throw new InvalidOperationException("Use Edit Mode.");
        var maps=UnityEngine.Object.FindObjectsByType<CampusServerMap>(FindObjectsSortMode.None)
            .Where(m=>m.gameObject.scene.path==CampusServerMapAuthoring.ScenePath).ToArray();
        if(maps.Length!=1)throw new InvalidOperationException("Open CampusTerrain with one server map.");
        Migrate(maps[0]);
        EditorSceneManager.MarkSceneDirty(maps[0].gameObject.scene);
        EditorSceneManager.SaveScene(maps[0].gameObject.scene);
        AssetDatabase.SaveAssets();
    }

    public static void Migrate(CampusServerMap map)
    {
        foreach(var spec in new[] {
            (name:"Stop shelters | visual only",kind:ServerMapMarker.MarkerKind.Stop),
            (name:"Campus hub garages",kind:ServerMapMarker.MarkerKind.Hub)})
        {
            var roots=map.gameObject.scene.GetRootGameObjects().Where(r=>r.name==spec.name).ToArray();
            if(roots.Length>1)throw new InvalidOperationException("Duplicate display model root: "+spec.name);
            if(roots.Length==0)continue;
            foreach(var model in roots[0].transform.Cast<Transform>().ToArray())
            {
                string id=model.name.Split(new[]{" | "},StringSplitOptions.None)[0];
                var marker=map.markers.SingleOrDefault(m=>m.kind==spec.kind && m.serverId==id);
                if(!marker)throw new InvalidOperationException("Display model has no matching point: "+model.name);
                Bind(model,marker);
            }
            if(roots[0].transform.childCount==0)Undo.DestroyObjectImmediate(roots[0]);
        }
    }

    public static void Bind(Transform model,ServerMapMarker marker)
    {
        if(model.GetComponentsInChildren<Rigidbody>(true).Length!=0 || model.GetComponentsInChildren<Collider>(true).Length!=0)
            throw new InvalidOperationException("Only display models can be parented to service points: "+model.name);
        if(marker.kind==ServerMapMarker.MarkerKind.RoadNode)throw new InvalidOperationException("Road nodes cannot own service models.");
        var tag=model.GetComponent<ServicePointVisual>();
        if(!tag)tag=Undo.AddComponent<ServicePointVisual>(model.gameObject);
        Undo.RecordObject(tag,"Bind service point model identity");
        tag.pointId=marker.serverId;tag.pointKind=marker.kind;
        Undo.SetTransformParent(model,marker.transform,"Attach display model to service point");
        EditorUtility.SetDirty(tag);
    }
}
