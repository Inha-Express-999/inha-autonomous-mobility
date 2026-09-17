using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using Newtonsoft.Json.Linq;
public static class ValidateInhaCampus
{
    public static string Main()
    {
        var root=GameObject.Find("INHA UNIVERSITY | Map only");if(!root)throw new Exception("Campus root missing.");
        var failures=new List<string>();int missing=0;foreach(var tr in root.GetComponentsInChildren<Transform>(true))missing+=GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(tr.gameObject);
        var renderers=root.GetComponentsInChildren<MeshRenderer>(true);foreach(var r in renderers)if(r.sharedMaterials.Any(m=>!m||!m.shader||!m.shader.isSupported))failures.Add("Invalid material: "+r.name);
        foreach(var mf in root.GetComponentsInChildren<MeshFilter>())if(!mf.sharedMesh||string.IsNullOrEmpty(AssetDatabase.GetAssetPath(mf.sharedMesh)))failures.Add("Unsaved mesh: "+mf.name);
        var data=JObject.Parse(File.ReadAllText("Assets/InhaCampus/Source/campus.json"));int tested=0,hits=0;Physics.SyncTransforms();
        foreach(var f in data["features"].Where(f=>(string)f["kind"]=="road"))
        {
            var ps=f["points"].ToArray();for(int i=0;i<ps.Length-1;i++){var a=ps[i];var b=ps[i+1];var p=new Vector3(((float)a["x"]+(float)b["x"])/2,2,((float)a["z"]+(float)b["z"])/2);tested++;RaycastHit hit;if(Physics.Raycast(p,Vector3.down,out hit,3)&&hit.collider.name.Contains("network"))hits++;else failures.Add("Road sample did not hit its road surface: "+(string)f["id"]+" segment "+i);}
        }
        int behaviourCount=root.GetComponentsInChildren<MonoBehaviour>(true).Length;int bodies=root.GetComponentsInChildren<Rigidbody>(true).Length;int canvases=root.GetComponentsInChildren<Canvas>(true).Length;
        if(missing!=0||behaviourCount!=0||bodies!=0||canvases!=0)failures.Add("Unexpected runtime behaviours, missing scripts, bodies, or UI.");
        var result=new {scene=EditorSceneManager.GetActiveScene().path,buildingGroups=root.transform.Find("03 Campus buildings").childCount,renderers=renderers.Length,colliders=root.GetComponentsInChildren<Collider>().Length,missingScripts=missing,runtimeBehaviours=behaviourCount,rigidbodies=bodies,canvases=canvases,roadSamples=tested,roadSurfaceHits=hits,failures=failures};
        string json=Newtonsoft.Json.JsonConvert.SerializeObject(result,Newtonsoft.Json.Formatting.Indented);File.WriteAllText("Docs/AI/CampusValidation.json",json);return json;
    }
}
