using System;
using System.IO;
using System.Linq;
using System.Globalization;
using System.Xml;
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;
using CampusSim;

public static class CampusStationExitExpansion
{
    [MenuItem("Campus/Map/Expand Inha Station OSM Exits")]
    public static void Apply()
    {
        if(EditorApplication.isPlayingOrWillChangePlaymode||SceneManager.GetActiveScene().path!="Assets/CampusSim/Scenes/CampusTerrain.unity")throw new Exception("Open working scene.");
        var template=GameObject.Find("Inha Station | Exit 7 draft");if(!template)throw new Exception("Existing station template missing.");
        var terrain=UnityEngine.Object.FindFirstObjectByType<Terrain>();var map=GameObject.Find("INHA UNIVERSITY").transform;
        var xml=new XmlDocument();xml.Load("Docs/MapResearch/2026-09-17/expanded_campus.osm");
        var nodes=xml.SelectNodes("//node[tag[@k='railway' and @v='subway_entrance'] and tag[contains(@v,'인하대역')]]").Cast<XmlNode>().ToArray();
        var report=new System.Text.StringBuilder("Inha station exits from cached OSM. Coordinates observed; dimensions and orientation synthetic. Unknown accessibility is not accessible=true. One station landmark, multiple entrance records. No vehicle Stops or underground routes approved.\n");
        foreach(var node in nodes)
        {
            var tags=node.SelectNodes("tag").Cast<XmlNode>().ToDictionary(t=>t.Attributes["k"].Value,t=>t.Attributes["v"].Value);var number=tags["ref"];var name="Inha Station | Exit "+number+" draft";
            var go=GameObject.Find(name);bool created=!go;
            if(created){go=UnityEngine.Object.Instantiate(template);go.name=name;var duplicate=go.GetComponent<CampusLandmarkEntrances>();if(duplicate)UnityEngine.Object.DestroyImmediate(duplicate);}
            var binding=go.GetComponent<CampusTerrainBinding>();
            double lon=double.Parse(node.Attributes["lon"].Value,CultureInfo.InvariantCulture),lat=double.Parse(node.Attributes["lat"].Value,CultureInfo.InvariantCulture);
            var p=map.TransformPoint(new Vector3((float)((lon-126.6535)*111320*Math.Cos(37.4506*Math.PI/180)),0,(float)((lat-37.4506)*110980)));
            binding.originalPosition=p;binding.samplePoint=p;binding.sourceId="node/"+node.Attributes["id"].Value;binding.heightAnchor=null;
            go.transform.position=p+Vector3.up*(terrain.SampleHeight(p)+terrain.transform.position.y+binding.surfaceOffset);
            var metadata=go.GetComponent<CampusStationEntrance>()??go.AddComponent<CampusStationEntrance>();metadata.exitNumber=number;metadata.osmNodeId=node.Attributes["id"].Value;metadata.wheelchair=tags.TryGetValue("wheelchair",out var access)?access:"unknown";
            var entry=go.GetComponentsInChildren<Transform>().First(t=>t.name.StartsWith("inha_station_general_exit_"));entry.name="inha_station_general_exit_"+number;metadata.entrance=entry;metadata.routeVerified=false;
            foreach(var text in go.GetComponentsInChildren<TextMesh>())text.text="INHA UNIV. / "+number;
            foreach(var r in go.GetComponentsInChildren<MeshRenderer>())if(!r.GetComponent<TextMesh>())GameObjectUtility.SetStaticEditorFlags(r.gameObject,StaticEditorFlags.BatchingStatic|StaticEditorFlags.OccludeeStatic);
            EditorUtility.SetDirty(binding);EditorUtility.SetDirty(metadata);
            report.AppendLine("Exit "+number+": "+binding.sourceId+" lon="+lon.ToString(CultureInfo.InvariantCulture)+" lat="+lat.ToString(CultureInfo.InvariantCulture)+" wheelchair="+metadata.wheelchair+" created="+created);
        }
        AssetDatabase.SaveAssets();EditorSceneManager.SaveScene(SceneManager.GetActiveScene());File.WriteAllText("Docs/MapResearch/Iterations/station-exits.txt",report.ToString());
    }
}
