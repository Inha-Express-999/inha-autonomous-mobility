using System;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEditor;
using CampusSim;
public static class CampusFacadeDiagnostics
{
    [MenuItem("Campus/Map/Separate Tower Facade Layers")]
    public static void Separate()
    {
        const string folder="Assets/CampusSim/Generated/FacadeSeparation";Directory.CreateDirectory(folder);AssetDatabase.Refresh();int count=0;
        foreach(var b in UnityEngine.Object.FindObjectsByType<CampusTerrainBinding>(FindObjectsSortMode.None).Where(b=>b.kind==CampusTerrainBinding.BindingKind.Building&&(b.name.Contains("60주년")||b.name.Contains("하이테크"))))
        foreach(var mf in b.GetComponentsInChildren<MeshFilter>().Where(m=>m.name=="Window glazing"||m.name=="Window reflections"||m.name=="Aluminium mullions"))
        {
            if(AssetDatabase.GetAssetPath(mf.sharedMesh).StartsWith(folder))continue;
            var mesh=UnityEngine.Object.Instantiate(mf.sharedMesh);var v=mesh.vertices;
            if(v.Length%36!=0)throw new Exception("Unexpected facade box topology.");
            for(int i=0;i<v.Length;i+=36)
            {
                var back=Vector3.zero;var front=Vector3.zero;for(int j=0;j<6;j++){back+=v[i+j];front+=v[i+6+j];}
                var normal=(front-back).normalized;float offset=mf.name=="Aluminium mullions"?.22f:.14f;
                for(int j=0;j<36;j++)v[i+j]+=normal*offset;
            }
            mesh.vertices=v;mesh.RecalculateBounds();AssetDatabase.CreateAsset(mesh,AssetDatabase.GenerateUniqueAssetPath(folder+"/"+mf.name+".asset"));mf.sharedMesh=mesh;count++;
        }
        AssetDatabase.SaveAssets();UnityEditor.SceneManagement.EditorSceneManager.SaveScene(UnityEngine.SceneManagement.SceneManager.GetActiveScene());
        File.WriteAllText("Docs/MapResearch/Iterations/facade-separation.txt","Separated tower mesh layers="+count+"; glass offset=.14m; frame offset=.22m. Source meshes preserved.\n");
    }
    [MenuItem("Campus/Map/Compare Facade Depth And Shadows")]
    public static void Compare()
    {
        var camera=Camera.main;float near=camera.nearClipPlane;
        var materials=UnityEngine.Object.FindObjectsByType<CampusTerrainBinding>(FindObjectsSortMode.None).Where(b=>b.kind==CampusTerrainBinding.BindingKind.Building).SelectMany(b=>b.GetComponentsInChildren<MeshRenderer>()).SelectMany(r=>r.sharedMaterials).Where(m=>m&&m.shader.name=="FlatKit/Stylized Surface").Distinct().ToArray();
        var powers=materials.Select(m=>m.GetFloat("_UnityShadowPower")).ToArray();
        try
        {
            camera.nearClipPlane=3;
            CampusTerrainAuthoring.CaptureLake();File.Copy("Docs/MapResearch/Iterations/lake.png","Docs/MapResearch/Iterations/facade-depth.png",true);
            foreach(var m in materials)m.SetFloat("_UnityShadowPower",0);
            CampusTerrainAuthoring.CaptureLake();File.Copy("Docs/MapResearch/Iterations/lake.png","Docs/MapResearch/Iterations/facade-no-shadow.png",true);
        }
        finally{camera.nearClipPlane=near;for(int i=0;i<materials.Length;i++)materials[i].SetFloat("_UnityShadowPower",powers[i]);}
    }
}
