using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;
using CampusSim;
public static class CampusDormitoryAuthoring
{
    [MenuItem("Campus/Map/Capture Dormitory Massing")]
    public static void Capture()
    {
        var camera=Camera.main;var position=camera.transform.position;var rotation=camera.transform.rotation;
        try
        {
            foreach(var landmark in UnityEngine.Object.FindObjectsByType<CampusLandmarkEntrances>(FindObjectsSortMode.None).Where(l=>l.landmarkId=="dorm_1"||l.landmarkId=="dorm_2"))
            {
                var bounds=landmark.GetComponent<Renderer>().bounds;float span=Mathf.Max(bounds.size.x,bounds.size.z,bounds.size.y);
                camera.transform.position=bounds.center+new Vector3(-span*.85f,span*.7f,-span);camera.transform.LookAt(bounds.center);
                CampusTerrainAuthoring.Capture();File.Copy("Docs/MapResearch/Iterations/terrain-overview.png","Docs/MapResearch/Iterations/"+landmark.landmarkId+".png",true);
            }
        }
        finally{camera.transform.SetPositionAndRotation(position,rotation);}
    }
    [Serializable] class Set { public Building[] buildings; }
    [Serializable] class Building { public string id,osmId; public int floors; public Point[] points; public int[] triangles; }
    [Serializable] class Point { public float x,z; }
    [MenuItem("Campus/Map/Build Dormitory One And Two")]
    public static void Build()
    {
        if(SceneManager.GetActiveScene().path!="Assets/CampusSim/Scenes/CampusTerrain.unity"||EditorApplication.isPlayingOrWillChangePlaymode)throw new Exception("Open working scene.");
        if(GameObject.Find("Dormitory drafts"))throw new Exception("Dormitory drafts already exist.");
        const string folder="Assets/CampusSim/Generated/Dormitories";Directory.CreateDirectory(folder);AssetDatabase.Refresh();
        var white=Material("Ivory",new Color(.8f,.81f,.77f));var beige=Material("Sand",new Color(.63f,.57f,.44f));var blue=Material("Windows",new Color(.15f,.32f,.37f));var roof=Material("Roof",new Color(.3f,.34f,.34f));
        Material Material(string name,Color color){var m=new Material(Shader.Find("FlatKit/Stylized Surface")){name="Dorm "+name};m.SetColor("_BaseColor",color);m.EnableKeyword("_CELPRIMARYMODE_SINGLE");AssetDatabase.CreateAsset(m,folder+"/"+name+".mat");return m;}
        var map=GameObject.Find("INHA UNIVERSITY").transform;var parent=new GameObject("Dormitory drafts").transform;
        var terrain=UnityEngine.Object.FindObjectsByType<Terrain>(FindObjectsSortMode.None).Single();
        foreach(var item in JsonUtility.FromJson<Set>(File.ReadAllText("Assets/CampusSim/Data/dormitory_drafts.json")).buildings)
        {
            var center=new Vector3(item.points.Average(p=>p.x),0,item.points.Average(p=>p.z));
            var root=new GameObject(item.id=="dorm_1"?"제1생활관 웅비재 | draft":"제2생활관 비룡재 | draft").transform;root.SetParent(parent,false);root.position=map.TransformPoint(center);root.rotation=map.rotation;
            var points=item.points.Select(p=>new Vector3(p.x-center.x,0,p.z-center.z)).ToArray();float height=item.floors*3.2f;
            var vertices=new List<Vector3>();var subs=new[]{new List<int>(),new List<int>(),new List<int>(),new List<int>()};
            void Quad(Vector3 a,Vector3 b,Vector3 c,Vector3 d,int material){int i=vertices.Count;vertices.AddRange(new[]{a,b,c,d});subs[material].AddRange(new[]{i,i+1,i+2,i,i+2,i+3});}
            int longest=0;float maxLength=0;
            for(int e=0;e<points.Length;e++)
            {
                var a=points[e];var b=points[(e+1)%points.Length];var delta=b-a;float length=delta.magnitude;var direction=delta.normalized;var outward=new Vector3(direction.z,0,-direction.x);
                if(length>maxLength){maxLength=length;longest=e;}
                Quad(a,a+Vector3.up*height,b+Vector3.up*height,b,item.id=="dorm_2"&&e%3==0?3:0);
                for(int floor=0;floor<item.floors;floor++)
                {
                    for(float s=2;s<length-1.5f;s+=3.6f)
                    {
                        var p=a+direction*s+outward*.08f+Vector3.up*(floor*3.2f+1);
                        Quad(p-direction*.65f,p-direction*.65f+Vector3.up*1.65f,p+direction*.65f+Vector3.up*1.65f,p+direction*.65f,2);
                    }
                    if(floor>0)Quad(a+outward*.04f+Vector3.up*(floor*3.2f),a+outward*.04f+Vector3.up*(floor*3.2f+.12f),b+outward*.04f+Vector3.up*(floor*3.2f+.12f),b+outward*.04f+Vector3.up*(floor*3.2f),0);
                }
            }
            for(int i=0;i<item.triangles.Length;i+=3){int offset=vertices.Count;vertices.Add(points[item.triangles[i]]+Vector3.up*height);vertices.Add(points[item.triangles[i+2]]+Vector3.up*height);vertices.Add(points[item.triangles[i+1]]+Vector3.up*height);subs[1].AddRange(new[]{offset,offset+1,offset+2});}
            var mesh=new Mesh{name=item.id+" photograph guided massing",indexFormat=UnityEngine.Rendering.IndexFormat.UInt32};mesh.SetVertices(vertices);mesh.subMeshCount=4;for(int s=0;s<4;s++)mesh.SetTriangles(subs[s],s);mesh.RecalculateNormals();mesh.RecalculateBounds();AssetDatabase.CreateAsset(mesh,folder+"/"+item.id+".asset");
            root.gameObject.AddComponent<MeshFilter>().sharedMesh=mesh;root.gameObject.AddComponent<MeshRenderer>().sharedMaterials=new[]{white,roof,blue,beige};root.gameObject.AddComponent<MeshCollider>().sharedMesh=mesh;
            var binding=root.gameObject.AddComponent<CampusTerrainBinding>();binding.kind=CampusTerrainBinding.BindingKind.Building;binding.originalPosition=root.position;binding.samplePoint=root.position;binding.sourceId="way/"+item.osmId;
            var metadata=root.gameObject.AddComponent<CampusLandmarkEntrances>();metadata.landmarkId=item.id;metadata.verificationStatus="osm_footprint_photo_guided_synthetic_height_and_entrances";
            var edgeA=points[longest];var edgeB=points[(longest+1)%points.Length];var normal=new Vector3((edgeB-edgeA).z,0,-(edgeB-edgeA).x).normalized;
            for(int i=0;i<2;i++)
            {
                var entry=new GameObject(item.id+(i==0?"_general":"_accessible")).transform;entry.SetParent(root,false);entry.localPosition=Vector3.Lerp(edgeA,edgeB,i==0?.3f:.7f)+normal*.3f;entry.localRotation=Quaternion.LookRotation(normal);
                Box(entry,"Door",new Vector3(0,1.2f,0),new Vector3(2.3f,2.4f,.2f),blue);
                Box(entry,"Canopy",new Vector3(0,2.7f,.5f),new Vector3(3.3f,.2f,1.5f),i==0?white:blue);
                Box(entry,"Landing",new Vector3(0,.15f,1),new Vector3(3.3f,.2f,2),white);
                if(i==0)metadata.generalEntrance=entry;else metadata.accessibleEntrance=entry;
            }
            var bounds=root.GetComponent<Renderer>().bounds;var data=terrain.terrainData;int n=data.heightmapResolution;var h=data.GetHeights(0,0,n,n);var origin=terrain.transform.position;
            float target=terrain.SampleHeight(root.position)/data.size.y;
            for(int z=0;z<n;z++)for(int x=0;x<n;x++){float wx=origin.x+x*data.size.x/(n-1),wz=origin.z+z*data.size.z/(n-1);float dx=Mathf.Max(bounds.min.x-wx,0,wx-bounds.max.x),dz=Mathf.Max(bounds.min.z-wz,0,wz-bounds.max.z);float d=Mathf.Sqrt(dx*dx+dz*dz);if(d<12)h[z,x]=Mathf.Lerp(target,h[z,x],Mathf.SmoothStep(0,1,d/12));}data.SetHeights(0,0,h);
        }
        CampusTerrainAuthoring.Bake();AssetDatabase.SaveAssets();EditorSceneManager.SaveScene(SceneManager.GetActiveScene());
        File.WriteAllText("Docs/MapResearch/Iterations/dormitories.txt","Dorm 1 and 2 added using cached OSM outlines and reviewed official photos. Above-ground floors 5/13; 3.2m per floor is synthetic. Four proposed entrances; access and campus road connections unverified. Dorm 3 not created.\n");
    }
    static void Box(Transform parent,string name,Vector3 p,Vector3 size,Material m){var g=GameObject.CreatePrimitive(PrimitiveType.Cube);g.name=name;g.transform.SetParent(parent,false);g.transform.localPosition=p;g.transform.localScale=size;g.GetComponent<Renderer>().sharedMaterial=m;}
}
