using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEditor;
using UnityEditor.SceneManagement;
using Newtonsoft.Json.Linq;

public static class FinalizeInhaCampus
{
    const string F="Assets/InhaCampus";static List<Vector3> v=new List<Vector3>();static List<int> t=new List<int>();
    static Vector3 P(JToken p){return new Vector3((float)p["x"],0,(float)p["z"]);}
    static void Tri(Vector3 a,Vector3 b,Vector3 c){int n=v.Count;v.AddRange(new[]{a,b,c});t.AddRange(new[]{n,n+1,n+2});}
    static void Q(Vector3 a,Vector3 b,Vector3 c,Vector3 d){Tri(a,b,c);Tri(a,c,d);}
    static void Box(Vector3 p,Vector3 size,Quaternion rot)
    {var x=rot*Vector3.right*size.x/2;var y=rot*Vector3.up*size.y/2;var z=rot*Vector3.forward*size.z/2;Q(p-x-y-z,p-x+y-z,p+x+y-z,p+x-y-z);Q(p+x-y+z,p+x+y+z,p-x+y+z,p-x-y+z);Q(p-x-y+z,p-x+y+z,p-x+y-z,p-x-y-z);Q(p+x-y-z,p+x+y-z,p+x+y+z,p+x-y+z);Q(p-x+y-z,p-x+y+z,p+x+y+z,p+x+y-z);Q(p-x-y+z,p-x-y-z,p+x-y-z,p+x-y+z);}
    static void Flat(JToken ps,float h){var a=ps.Select(P).ToArray();for(int i=0;i<a.Length;i+=3){var p=a[i]+Vector3.up*h;var q=a[i+1]+Vector3.up*h;var r=a[i+2]+Vector3.up*h;if(Vector3.Cross(q-p,r-p).y>0)Tri(p,q,r);else Tri(p,r,q);}}
    static GameObject Emit(string name,string mat,Transform parent,bool collide)
    {var mesh=new Mesh{name=name,indexFormat=IndexFormat.UInt32};mesh.SetVertices(v);mesh.SetTriangles(t,0);mesh.SetUVs(0,v.Select(p=>new Vector2(p.x,p.z)).ToList());mesh.RecalculateNormals();mesh.RecalculateBounds();AssetDatabase.CreateAsset(mesh,F+"/Meshes/Final_"+name+".asset");var go=new GameObject(name);go.transform.SetParent(parent);go.isStatic=true;go.AddComponent<MeshFilter>().sharedMesh=mesh;go.AddComponent<MeshRenderer>().sharedMaterial=AssetDatabase.LoadAssetAtPath<Material>(F+"/Materials/"+mat+".mat");if(collide)go.AddComponent<MeshCollider>().sharedMesh=mesh;v.Clear();t.Clear();return go;}
    public static string Main()
    {
        var root=GameObject.Find("INHA UNIVERSITY | Map only");var roads=root.transform.Find("02 Roads and pedestrian network");var data=JObject.Parse(File.ReadAllText(F+"/Source/road-surfaces.json"));
        foreach(string name in new[]{"Mapped asphalt carriageways","Pedestrian paths and sidewalks","Granite kerbs","Yellow edge markings"}){var o=roads.Find(name);if(o)UnityEngine.Object.DestroyImmediate(o.gameObject);}
        Flat(data["roads"],.081f);Emit("Continuous asphalt network","Asphalt",roads,true);Flat(data["walks"],.145f);Emit("Continuous pedestrian network","Paving",roads,true);
        foreach(var loop in data["kerbs"]){var ps=loop["points"].Select(P).ToArray();for(int i=0;i<ps.Length-1;i++){var a=ps[i];var b=ps[i+1];if(Vector3.Distance(a,b)<.05f)continue;Box((a+b)/2+Vector3.up*.10f,new Vector3(.15f,.20f,Vector3.Distance(a,b)),Quaternion.LookRotation(b-a));}}Emit("Junction aware kerbs","Light granite",roads,true);
        foreach(var loop in data["yellow"]){var ps=loop["points"].Select(P).ToArray();for(int i=0;i<ps.Length-1;i++){var a=ps[i];var b=ps[i+1];if(Vector3.Distance(a,b)<.05f)continue;Box((a+b)/2+Vector3.up*.091f,new Vector3(.10f,.012f,Vector3.Distance(a,b)),Quaternion.LookRotation(b-a));}}Emit("Continuous yellow road edges","Traffic yellow",roads,false);
        // The photographed main facade has tall stone pilasters across its central front.
        var feature=JObject.Parse(File.ReadAllText(F+"/Source/campus.json"))["features"].First(f=>(string)f["name"]=="본관");var points=feature["points"].Select(P).ToArray();var edges=points.Select((p,i)=>new[]{p,points[(i+1)%points.Length]}).Where(e=>(e[0].z+e[1].z)/2< -140).OrderByDescending(e=>Vector3.Distance(e[0],e[1])).ToArray();var aa=edges[0][0];var bb=edges[0][1];var normal=new Vector3(-.48f,0,-.88f);var rot=Quaternion.LookRotation(normal);float len=Vector3.Distance(aa,bb);
        for(float k=.22f;k<.82f;k+=.065f){var c=Vector3.Lerp(aa,bb,k)+normal*.95f;Box(c+Vector3.up*9.3f,new Vector3(.9f,18.6f,1.1f),rot);Box(c+Vector3.up*18,new Vector3(1.4f,.45f,1.4f),rot);}Emit("Main facade full height pilasters","Light granite",root.transform.Find("04 Architectural details"),true);
        var cam=Camera.main;cam.transform.position=new Vector3(-325,345,-545);cam.transform.LookAt(new Vector3(30,0,-95));cam.fieldOfView=53;
        PrefabUtility.SaveAsPrefabAsset(root,F+"/InhaCampusMap.prefab");EditorSceneManager.SaveScene(EditorSceneManager.GetActiveScene());AssetDatabase.SaveAssets();return "Road junctions and facade finished.";
    }
}
