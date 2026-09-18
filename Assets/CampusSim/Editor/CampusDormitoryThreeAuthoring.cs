using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using CampusSim;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

// Official-map approximate massing, never a verified entrance or vehicle Stop.
public static class CampusDormitoryThreeAuthoring
{
    const string Folder="Assets/CampusSim/Generated/DormitoryThree";
    [Serializable] class Point { public float x,z; }
    [Serializable] class Draft { public Point[] points; public int floors; public float height_per_floor_m; public string status; }
    [MenuItem("Campus/Map/Build Official Map Dormitory Three Draft")]
    public static void Build()
    {
        if(SceneManager.GetActiveScene().path!="Assets/CampusSim/Scenes/CampusTerrain.unity"||EditorApplication.isPlayingOrWillChangePlaymode)
            throw new InvalidOperationException("Open working scene in edit mode.");
        if(UnityEngine.Object.FindObjectsByType<CampusLandmarkEntrances>(FindObjectsSortMode.None).Any(l=>l.landmarkId=="dorm_3"))
            throw new InvalidOperationException("Dormitory 3 already exists; edit the existing draft.");
        var draft=JsonUtility.FromJson<Draft>(File.ReadAllText("Assets/CampusSim/Data/dormitory_three_draft.json"));
        var map=GameObject.Find("INHA UNIVERSITY").transform;
        var terrain=UnityEngine.Object.FindFirstObjectByType<Terrain>();
        var center=new Vector3(draft.points.Average(p=>p.x),0,draft.points.Average(p=>p.z));
        var pts=draft.points.Select(p=>new Vector3(p.x-center.x,0,p.z-center.z)).ToArray();
        var worldPolygon=draft.points.Select(p=>map.TransformPoint(new Vector3(p.x,0,p.z))).ToArray();
        var obstacles=UnityEngine.Object.FindObjectsByType<MeshCollider>(FindObjectsSortMode.None)
            .Where(c=>c.enabled&&(c.name.StartsWith("Road ")||c.name=="Continuous asphalt network"||c.GetComponentInParent<CampusLandmarkEntrances>())).ToArray();
        float siteHigh=float.NegativeInfinity;
        for(float z=worldPolygon.Min(p=>p.z);z<=worldPolygon.Max(p=>p.z);z+=.5f)
        for(float x=worldPolygon.Min(p=>p.x);x<=worldPolygon.Max(p=>p.x);x+=.5f)
        {
            bool inside=true;
            for(int i=0;i<worldPolygon.Length;i++){var a=worldPolygon[i];var b=worldPolygon[(i+1)%worldPolygon.Length];if((b.x-a.x)*(z-a.z)-(b.z-a.z)*(x-a.x)<0){inside=false;break;}}
            if(!inside)continue;
            var p=new Vector3(x,1000,z);
            if(obstacles.Any(c=>c.Raycast(new Ray(p,Vector3.down),out _,2000)))throw new InvalidOperationException("Proposed dormitory overlaps a road or existing landmark.");
            siteHigh=Mathf.Max(siteHigh,terrain.SampleHeight(p)+terrain.transform.position.y);
        }
        if(float.IsNegativeInfinity(siteHigh))throw new InvalidOperationException("Invalid draft footprint winding.");
        var materials=new[]{"Ivory","Windows","Sand"}.Select(n=>AssetDatabase.LoadAssetAtPath<Material>("Assets/CampusSim/Generated/Dormitories/"+n+".mat")).ToArray();
        var roofMaterial=AssetDatabase.LoadAssetAtPath<Material>("Assets/CampusSim/Generated/Dormitories/Roof.mat");
        if(materials.Any(m=>!m)||!roofMaterial)throw new InvalidOperationException("Dormitory materials missing.");
        Directory.CreateDirectory(Folder);AssetDatabase.Refresh();
        var go=new GameObject("제3생활관 게스트하우스 | official map approximate draft");
        Undo.RegisterCreatedObjectUndo(go,"Create dormitory three draft");
        go.transform.SetParent(GameObject.Find("Dormitory drafts").transform,false);
        go.transform.SetPositionAndRotation(map.TransformPoint(center),map.rotation);
        float floor=Mathf.Max(siteHigh,pts.Max(p=>terrain.SampleHeight(go.transform.TransformPoint(p))+terrain.transform.position.y))+.1f;
        go.transform.position=new Vector3(go.transform.position.x,floor,go.transform.position.z);
        var vertices=new List<Vector3>();var sub=new[]{new List<int>(),new List<int>(),new List<int>()};
        void Quad(Vector3 a,Vector3 b,Vector3 c,Vector3 d,int material)
        {int k=vertices.Count;vertices.AddRange(new[]{a,b,c,d});sub[material].AddRange(new[]{k,k+1,k+2,k,k+2,k+3});}
        float height=draft.floors*draft.height_per_floor_m;
        for(int e=0;e<pts.Length;e++)
        {
            var a=pts[e];var b=pts[(e+1)%pts.Length];var dir=(b-a).normalized;var outward=new Vector3(dir.z,0,-dir.x);
            Quad(a,a+Vector3.up*height,b+Vector3.up*height,b,0);
            for(int level=0;level<draft.floors;level++)
            {
                for(float s=1.5f;s<Vector3.Distance(a,b)-2;s+=3.2f)
                {var p=a+dir*s+outward*.04f+Vector3.up*(level*draft.height_per_floor_m+.8f);Quad(p,p+Vector3.up*1.65f,p+Vector3.up*1.65f+dir*1.7f,p+dir*1.7f,1);}
                var band=a+Vector3.up*(level*draft.height_per_floor_m+.2f)+outward*.05f;
                Quad(band,band+Vector3.up*.12f,band+Vector3.up*.12f+(b-a),band+(b-a),2);
            }
        }
        var source=new Mesh{name="Dormitory three source"};source.SetVertices(vertices);source.subMeshCount=3;
        for(int i=0;i<3;i++)source.SetTriangles(sub[i],i);source.RecalculateNormals();source.RecalculateBounds();
        AssetDatabase.CreateAsset(source,Folder+"/source.asset");
        var mesh=UnityEngine.Object.Instantiate(source);mesh.name="Dormitory three grounded";AssetDatabase.CreateAsset(mesh,Folder+"/grounded.asset");
        go.AddComponent<MeshFilter>().sharedMesh=mesh;go.AddComponent<MeshRenderer>().sharedMaterials=materials;go.AddComponent<MeshCollider>().sharedMesh=mesh;
        var roof=new GameObject("Roof");roof.transform.SetParent(go.transform,false);
        var roofMesh=new Mesh{name="Dormitory three roof"};roofMesh.vertices=pts.Select(p=>p+Vector3.up*height).ToArray();roofMesh.triangles=new[]{0,2,1,0,3,2};roofMesh.RecalculateNormals();roofMesh.RecalculateBounds();
        AssetDatabase.CreateAsset(roofMesh,Folder+"/roof.asset");roof.AddComponent<MeshFilter>().sharedMesh=roofMesh;roof.AddComponent<MeshRenderer>().sharedMaterial=roofMaterial;
        var binding=go.AddComponent<CampusTerrainBinding>();binding.kind=CampusTerrainBinding.BindingKind.Building;binding.samplePoint=go.transform.position;
        binding.originalPosition=go.transform.position-Vector3.up*(terrain.SampleHeight(binding.samplePoint)+terrain.transform.position.y);
        binding.sourceId="official dormitory map approximate outline; synthetic 3.2m floor height";
        var landmark=go.AddComponent<CampusLandmarkEntrances>();landmark.landmarkId="dorm_3";landmark.verificationStatus=draft.status;
        // Two proposed portals on the southern facade; actual doors are not surveyed.
        var edge=pts[1]-pts[0];var normal=new Vector3(edge.z,0,-edge.x).normalized;
        for(int i=0;i<2;i++)
        {
            var portal=new GameObject(i==0?"dorm_3_general":"dorm_3_accessible").transform;portal.SetParent(go.transform,false);
            portal.localPosition=Vector3.Lerp(pts[0],pts[1],i==0?.3f:.7f)+normal*.12f;portal.localRotation=Quaternion.LookRotation(normal);
            void Box(string name,Vector3 p,Vector3 size,Material material){var o=GameObject.CreatePrimitive(PrimitiveType.Cube);o.name=name;o.transform.SetParent(portal,false);o.transform.localPosition=p;o.transform.localScale=size;o.GetComponent<Renderer>().sharedMaterial=material;}
            Box("Door",new Vector3(0,1.2f,0),new Vector3(2.3f,2.4f,.16f),materials[1]);
            Box("Canopy",new Vector3(0,2.7f,.6f),new Vector3(3.3f,.18f,1.8f),materials[i==0?0:1]);
            Box("Landing",new Vector3(0,.05f,1),new Vector3(3.3f,.1f,2),materials[0]);
            if(i==0)landmark.generalEntrance=portal;else landmark.accessibleEntrance=portal;
        }
        foreach(var tr in go.GetComponentsInChildren<Transform>())GameObjectUtility.SetStaticEditorFlags(tr.gameObject,StaticEditorFlags.BatchingStatic|StaticEditorFlags.OccludeeStatic);
        FitFoundation();CampusEntranceApproaches.FitAfterTerrainBake();CampusEntranceInventory.Export();
        AssetDatabase.SaveAssets();EditorSceneManager.SaveOpenScenes();
    }
    public static void FitFoundation()
    {
        var landmark=UnityEngine.Object.FindObjectsByType<CampusLandmarkEntrances>(FindObjectsSortMode.None).SingleOrDefault(l=>l.landmarkId=="dorm_3");
        if(!landmark)return;
        var source=AssetDatabase.LoadAssetAtPath<Mesh>(Folder+"/source.asset");var mesh=AssetDatabase.LoadAssetAtPath<Mesh>(Folder+"/grounded.asset");
        if(!source||!mesh)throw new InvalidOperationException("Dormitory three source geometry missing.");
        var t=UnityEngine.Object.FindFirstObjectByType<Terrain>();var vertices=source.vertices;var indices=source.GetTriangles(0);float bottom=0;
        for(int k=0;k<indices.Length;k+=3)for(int e=0;e<3;e++)
        {
            var a=vertices[indices[k+e]];var b=vertices[indices[k+(e+1)%3]];if(a.y!=0||b.y!=0)continue;
            int steps=Mathf.CeilToInt(Vector3.Distance(a,b)/.25f);
            for(int i=0;i<=steps;i++){var p=landmark.transform.TransformPoint(Vector3.Lerp(a,b,i/(float)steps));bottom=Mathf.Min(bottom,t.SampleHeight(p)+t.transform.position.y-landmark.transform.position.y-.05f);}
        }
        for(int i=0;i<vertices.Length;i++)if(vertices[i].y==0)vertices[i].y=bottom;
        // The procedural facade predates the two proposed doors. Remove whole
        // window quads that cross a doorway, retaining the immutable source.
        var windows=source.GetTriangles(1);var visibleWindows=new List<int>();int removed=0;
        var portals=new[]{landmark.generalEntrance,landmark.accessibleEntrance}.Where(p=>p).ToArray();
        if(windows.Length%6!=0)throw new InvalidOperationException("Expected facade window quads.");
        for(int k=0;k<windows.Length;k+=6)
        {
            bool overlaps=portals.Any(portal=>{
                var points=windows.Skip(k).Take(6).Distinct().Select(i=>portal.InverseTransformPoint(landmark.transform.TransformPoint(vertices[i]))).ToArray();
                return points.Min(p=>p.x)<1.6f&&points.Max(p=>p.x)>-1.6f&&points.Min(p=>p.y)<2.8f&&points.Max(p=>p.y)>0&&points.All(p=>Mathf.Abs(p.z)<.3f);
            });
            if(overlaps)removed++;else visibleWindows.AddRange(windows.Skip(k).Take(6));
        }
        Undo.RecordObject(mesh,"Refit dormitory three foundation");mesh.vertices=vertices;mesh.SetTriangles(visibleWindows,1);mesh.RecalculateNormals();mesh.RecalculateBounds();EditorUtility.SetDirty(mesh);
        File.WriteAllText("Docs/MapResearch/Iterations/dorm3-doorway-windows.txt",$"Removed overlapping window quads={removed}; retained window quads={visibleWindows.Count/6}; source mesh unchanged.");
        var c=landmark.GetComponent<MeshCollider>();c.sharedMesh=null;c.sharedMesh=mesh;
    }
}
