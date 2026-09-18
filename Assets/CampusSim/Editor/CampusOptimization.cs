using System;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;
using CampusSim;

public static class CampusOptimization
{
    [MenuItem("Campus/Map/Configure And Audit Build Batching")]
    public static void AuditBatching()
    {
        var report = new System.Text.StringBuilder();
        report.AppendLine("Scene=" + SceneManager.GetActiveScene().path);
        foreach (var target in new[] { BuildTarget.StandaloneWindows64, BuildTarget.Android, BuildTarget.iOS })
        {
            PlayerSettings.SetStaticBatchingForPlatform(target, true);
            report.AppendLine(target + ": static batching=" + PlayerSettings.GetStaticBatchingForPlatform(target));
        }
        var renderers = UnityEngine.Object.FindObjectsByType<MeshRenderer>(FindObjectsSortMode.None);
        var materials = renderers.SelectMany(r => r.sharedMaterials).Where(m => m && AssetDatabase.GetAssetPath(m).StartsWith("Assets/CampusSim/")).Distinct().ToArray();
        report.AppendLine("Unique owned materials=" + materials.Length + "; instancing enabled=" + materials.Count(m => m.enableInstancing));
        report.AppendLine("Active BatchingStatic renderers=" + renderers.Count(r => (GameObjectUtility.GetStaticEditorFlags(r.gameObject) & StaticEditorFlags.BatchingStatic) != 0));
        report.AppendLine("SRP batching=" + UnityEngine.Rendering.GraphicsSettings.useScriptableRenderPipelineBatching);
        report.AppendLine("SRP Batcher may take priority over material instancing. Actual instanced draws require Frame Debugger verification. No measured FPS or draw-call savings claimed.");
        AssetDatabase.SaveAssets();
        File.WriteAllText("Docs/MapResearch/Iterations/optimization-audit.txt", report.ToString());
        Debug.Log(report.ToString());
    }
    [MenuItem("Campus/Map/Verify Grass Distance Culling")]
    public static void VerifyCulling()
    {
        var root=GameObject.Find("Campus dense grass cover");var control=root.GetComponent<CampusVegetationQuality>();
        var renderers=root.GetComponentsInChildren<Renderer>();var enabled=renderers.Select(r=>r.enabled).ToArray();
        var previousCamera=control.viewCamera;var previousQuality=control.quality;
        var go=new GameObject("Temporary culling verification camera");var camera=go.AddComponent<Camera>();camera.enabled=false;
        int low=0,high=0;
        try
        {
            control.viewCamera=camera;camera.transform.position=renderers[0].bounds.center;
            control.SetQuality(CampusVegetationQuality.Detail.Low);low=renderers.Count(r=>r.enabled);
            if(low==0)throw new Exception("Near grass hidden.");
            control.SetQuality(CampusVegetationQuality.Detail.High);high=renderers.Count(r=>r.enabled);
            if(high<low)throw new Exception("Higher quality hides near patches.");
            camera.transform.position+=Vector3.up*10000;control.RefreshVisibility();
            if(renderers.Any(r=>r.enabled))throw new Exception("Distant patches not culled: "+string.Join(";",renderers.Where(r=>r.enabled).Take(3).Select(r=>r.name+" bounds="+r.bounds+" distance="+r.bounds.SqrDistance(camera.transform.position)))+" camera="+camera.transform.position+" total="+renderers.Length);
            camera.transform.position=renderers[0].bounds.center;control.SetQuality(CampusVegetationQuality.Detail.Low);
            if(renderers.Count(r=>r.enabled)!=low)throw new Exception("Near patches not restored.");
        }
        finally
        {
            control.viewCamera=previousCamera;control.quality=previousQuality;
            for(int i=0;i<renderers.Length;i++)renderers[i].enabled=enabled[i];
            UnityEngine.Object.DestroyImmediate(go);
        }
        File.WriteAllText("Docs/MapResearch/Iterations/grass-culling-test.txt","PASS: real scene renderer visibility toggled by runtime component methods. Low nearby="+low+"; high nearby="+high+"; distant=0; return count restored. Camera, quality and renderer states restored. Editor functional test; not a mobile FPS benchmark.\n");
    }
    [MenuItem("Campus/Map/Optimize Static World And Vegetation")]
    public static void Apply()
    {
        if(EditorApplication.isPlayingOrWillChangePlaymode||SceneManager.GetActiveScene().path!="Assets/CampusSim/Scenes/CampusTerrain.unity")throw new Exception("Open working scene.");
        int statics=0,instances=0,lods=0;var bindings=UnityEngine.Object.FindObjectsByType<CampusTerrainBinding>(FindObjectsSortMode.None);
        foreach(var b in bindings)
        foreach(var renderer in b.GetComponentsInChildren<MeshRenderer>())
        {
            if(renderer.GetComponent<TextMesh>())continue;
            var flags=GameObjectUtility.GetStaticEditorFlags(renderer.gameObject);
            bool vegetation=b.kind==CampusTerrainBinding.BindingKind.Vegetation;
            bool grass=b.sourceId!=null&&b.sourceId.StartsWith("synthetic grass");
            if(vegetation||grass) flags &= ~StaticEditorFlags.BatchingStatic;
            else if(b.kind!=CampusTerrainBinding.BindingKind.Water){flags|=StaticEditorFlags.BatchingStatic;statics++;}
            GameObjectUtility.SetStaticEditorFlags(renderer.gameObject,flags);
            foreach(var material in renderer.sharedMaterials.Where(m=>m&&AssetDatabase.GetAssetPath(m).StartsWith("Assets/CampusSim/")))
            {
                material.enableInstancing=true;EditorUtility.SetDirty(material);instances++;
                if(vegetation && b.GetComponentsInChildren<LODGroup>().Length>0 && material.shader.name=="FlatKit/Stylized Surface" && !AssetDatabase.GetAssetPath(material).Contains("/VegetationVariety/"))
                {
                    bool bark=material.name.IndexOf("bark",StringComparison.OrdinalIgnoreCase)>=0;
                    var color=bark?new Color(.31f,.24f,.16f):new Color(.42f,.62f,.27f);
                    material.SetColor("_BaseColor",color);material.SetColor("_ColorDim",bark?new Color(.18f,.15f,.11f):new Color(.22f,.37f,.18f));
                    material.SetFloat("_ShadowEdgeSize",.2f);material.SetFloat("_UnityShadowMode",1);material.EnableKeyword("_UNITYSHADOWMODE_MULTIPLY");material.SetFloat("_UnityShadowPower",.25f);
                }
            }
        }
        foreach(var group in UnityEngine.Object.FindObjectsByType<LODGroup>(FindObjectsSortMode.None))
        {
            if(!group.GetComponentInParent<CampusTerrainBinding>())continue;
            var levels=group.GetLODs();if(levels.Length<2)continue;
            group.fadeMode=LODFadeMode.None;group.enabled=true;lods++;EditorUtility.SetDirty(group);
        }
        var grassRoot=GameObject.Find("Campus dense grass cover");
        if(grassRoot&&!grassRoot.GetComponent<CampusVegetationQuality>())grassRoot.AddComponent<CampusVegetationQuality>();
        if(grassRoot)foreach(var renderer in grassRoot.GetComponentsInChildren<Renderer>())renderer.enabled=true;
        // Preserve prior authoring versions without packaging their meshes in the player.
        foreach(var root in SceneManager.GetActiveScene().GetRootGameObjects())
            if(!root.activeSelf&&(root.name=="Campus grass cover"||root.name=="Campus dense grass cover | prior bounds mask"))root.tag="EditorOnly";
        AssetDatabase.SaveAssets();EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());EditorSceneManager.SaveScene(SceneManager.GetActiveScene());
        File.WriteAllText("Docs/MapResearch/Iterations/optimization.txt","Static batching eligible renderer visits="+statics+"; owned material instancing visits="+instances+"; multi-level LOD groups="+lods+". Grass distance culling: low70/medium140/high450 metres; mobile defaults medium. Static batches are built by Unity build/runtime pipeline; flags are not measured draw-call savings. Native frustum culling retained. Occlusion bake and device profiling pending.\n");
    }
}
