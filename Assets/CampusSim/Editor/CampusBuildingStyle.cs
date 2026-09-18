using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;
using CampusSim;

public static class CampusBuildingStyle
{
    [MenuItem("Campus/Map/Style Buildings With Flat Kit")]
    public static void Apply()
    {
        if(EditorApplication.isPlayingOrWillChangePlaymode||SceneManager.GetActiveScene().path!="Assets/CampusSim/Scenes/CampusTerrain.unity")throw new Exception("Open working scene.");
        const string folder="Assets/CampusSim/Generated/BuildingStyle";Directory.CreateDirectory(folder);AssetDatabase.Refresh();
        var shader=Shader.Find("FlatKit/Stylized Surface");if(!shader)throw new Exception("Flat Kit missing.");
        var replacements=new Dictionary<Material,Material>();int count=0;
        foreach(var renderer in UnityEngine.Object.FindObjectsByType<CampusTerrainBinding>(FindObjectsSortMode.None).Where(b=>b.kind==CampusTerrainBinding.BindingKind.Building).SelectMany(b=>b.GetComponentsInChildren<MeshRenderer>()).Distinct())
        {
            if(renderer.GetComponent<TextMesh>())continue;
            renderer.sharedMaterials=renderer.sharedMaterials.Select(original=>{
                if(!original)return original;
                if(replacements.TryGetValue(original,out var cached))return cached;
                string path=folder+"/"+original.name.Replace("/","_")+".mat";
                var material=AssetDatabase.LoadAssetAtPath<Material>(path);
                if(!material){material=new Material(shader){name=original.name};AssetDatabase.CreateAsset(material,path);}
                var color=original.HasProperty("_BaseColor")?original.GetColor("_BaseColor"):original.color;
                material.SetColor("_BaseColor",color);
                material.SetFloat("_CelPrimaryMode",1);material.EnableKeyword("_CELPRIMARYMODE_SINGLE");
                material.SetColor("_ColorDim",Color.Lerp(color*.72f,new Color(.29f,.37f,.43f),.22f));
                material.SetFloat("_SelfShadingSize",.5f);material.SetFloat("_ShadowEdgeSize",.16f);material.SetFloat("_Flatness",.8f);
                material.SetFloat("_UnityShadowMode",1);material.EnableKeyword("_UNITYSHADOWMODE_MULTIPLY");material.SetFloat("_UnityShadowPower",.32f);material.SetFloat("_UnityShadowSharpness",1);
                material.SetFloat("_SpecularEnabled",0);material.DisableKeyword("DR_SPECULAR_ON");material.SetFloat("_RimEnabled",0);material.DisableKeyword("DR_RIM_ON");
                // Retain geometry and material partitions; drop noisy tiled facade maps.
                material.SetTexture("_BaseMap",null);EditorUtility.SetDirty(material);replacements[original]=material;return material;
            }).ToArray();
            renderer.shadowCastingMode=UnityEngine.Rendering.ShadowCastingMode.On;renderer.receiveShadows=true;count++;
        }
        foreach(var light in UnityEngine.Object.FindObjectsByType<Light>(FindObjectsSortMode.None).Where(l=>l.type==LightType.Directional))
        {light.shadows=LightShadows.Soft;light.shadowStrength=.75f;light.shadowBias=.04f;light.shadowNormalBias=.25f;EditorUtility.SetDirty(light);}
        QualitySettings.shadowDistance=180;QualitySettings.shadowResolution=ShadowResolution.High;
        AssetDatabase.SaveAssets();EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());EditorSceneManager.SaveScene(SceneManager.GetActiveScene());
        File.WriteAllText("Docs/MapResearch/Iterations/building-style.txt","Building renderers="+count+"; owned Flat Kit materials="+replacements.Count+". Soft cel transitions; cool shaded colors; cast and receive shadows enabled. Original materials preserved.\n");
    }
}
