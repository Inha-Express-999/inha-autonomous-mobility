using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;
using CampusSim;

public static class CampusVegetationVariety
{
    const string Folder="Assets/CampusSim/Generated/VegetationVariety";
    static readonly Color[] GrassColors={new Color(.43f,.57f,.28f),new Color(.405f,.55f,.28f),new Color(.455f,.585f,.295f)};
    static readonly Color[] TreeColors={new Color(.39f,.57f,.25f),new Color(.47f,.63f,.30f),new Color(.31f,.49f,.29f),new Color(.53f,.62f,.29f)};
    static float Radius(Mesh mesh)=>new Vector2(Mathf.Max(Mathf.Abs(mesh.bounds.min.x),Mathf.Abs(mesh.bounds.max.x)),Mathf.Max(Mathf.Abs(mesh.bounds.min.z),Mathf.Abs(mesh.bounds.max.z))).magnitude;
    [MenuItem("Campus/Map/Vary Grass And Tree Species")]
    public static void Apply()
    {
        if(EditorApplication.isPlayingOrWillChangePlaymode||SceneManager.GetActiveScene().path!="Assets/CampusSim/Scenes/CampusTerrain.unity")throw new Exception("Open working scene.");
        if(Directory.Exists(Folder))throw new Exception("Variety assets already exist. Inspect before rerunning.");
        var old=GameObject.Find("Campus dense grass cover");if(!old)throw new Exception("Dense grass missing.");
        var patches=old.GetComponentsInChildren<CampusTerrainBinding>().OrderBy(b=>b.name,StringComparer.Ordinal).ToArray();
        var blades=Enumerable.Range(1,3).Select(i=>AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Idyllic Fantasy Nature/Prefabs/Grass_0"+i+".prefab").GetComponent<MeshFilter>().sharedMesh).ToArray();
        var original=blades[0].vertices;int distant=Enumerable.Range(1,original.Length-1).OrderByDescending(i=>new Vector2(original[i].x-original[0].x,original[i].z-original[0].z).sqrMagnitude).First();
        var referenceDelta=original[distant]-original[0];referenceDelta.y=0;
        if(referenceDelta.sqrMagnitude<.0001f||patches.Any(p=>p.sourceMesh.vertexCount%original.Length!=0))throw new Exception("Grass source layout unsupported.");
        Directory.CreateDirectory(Folder);AssetDatabase.Refresh();
        var mats=new Material[3,3];var template=patches[0].GetComponent<Renderer>().sharedMaterial;
        var root=new GameObject("Campus varied grass staging");root.transform.SetPositionAndRotation(old.transform.position,old.transform.rotation);root.transform.localScale=old.transform.localScale;
        var rng=new System.Random(20260918);int[] counts=new int[3];int changed=0;
        AssetDatabase.StartAssetEditing();
        try
        {
            for(int species=0;species<3;species++)for(int tone=0;tone<3;tone++)
            {
                var m=new Material(template){name="Meadow species "+(species+1)+" tone "+tone,enableInstancing=true};
                m.SetTexture("_BaseMap",AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/Idyllic Fantasy Nature/Textures/Grass/Grass_0"+(species+1)+".png"));
                m.SetColor("_BaseColor",GrassColors[tone]);m.SetColor("_ColorDim",GrassColors[tone]*.76f);mats[species,tone]=m;
                AssetDatabase.CreateAsset(m,Folder+"/Grass_"+species+"_"+tone+".mat");
            }
            for(int patch=0;patch<patches.Length;patch++)
            {
                var prior=patches[patch];var input=prior.sourceMesh;int roll=rng.Next(100),species=roll<55?0:roll<82?1:2;
                int clumps=input.vertexCount/original.Length;counts[species]+=clumps;
                Mesh source=input,baked=prior.bakedMesh;
                if(species!=0)
                {
                    var v=input.vertices;var mesh=blades[species];var instances=new CombineInstance[clumps];
                    for(int i=0;i<clumps;i++)
                    {
                        int start=i*original.Length;var delta=v[start+distant]-v[start];delta.y=0;
                        float scale=delta.magnitude/referenceDelta.magnitude;
                        float yaw=(Mathf.Atan2(delta.x,delta.z)-Mathf.Atan2(referenceDelta.x,referenceDelta.z))*Mathf.Rad2Deg;
                        var rotation=Quaternion.Euler(0,yaw,0);var center=v[start]-rotation*(original[0]*scale);
                        float fit=Mathf.Min(Radius(blades[0])/Radius(mesh),blades[0].bounds.size.y/mesh.bounds.size.y);
                        float fittedScale=scale*fit;center.y=-mesh.bounds.min.y*fittedScale;
                        instances[i]=new CombineInstance{mesh=mesh,transform=Matrix4x4.TRS(center,rotation,Vector3.one*fittedScale)};
                    }
                    source=new Mesh{name="Varied grass source "+patch,indexFormat=UnityEngine.Rendering.IndexFormat.UInt32};source.CombineMeshes(instances,true,true);source.RecalculateBounds();
                    baked=UnityEngine.Object.Instantiate(source);AssetDatabase.CreateAsset(source,Folder+"/Source_"+patch+".asset");AssetDatabase.CreateAsset(baked,Folder+"/Baked_"+patch+".asset");changed++;
                }
                var go=new GameObject(prior.name);go.transform.SetParent(root.transform,false);go.transform.localPosition=prior.transform.localPosition;go.transform.localRotation=prior.transform.localRotation;go.transform.localScale=prior.transform.localScale;
                go.AddComponent<MeshFilter>().sharedMesh=baked;var renderer=go.AddComponent<MeshRenderer>();renderer.sharedMaterial=mats[species,rng.Next(3)];renderer.shadowCastingMode=UnityEngine.Rendering.ShadowCastingMode.Off;
                var binding=go.AddComponent<CampusTerrainBinding>();EditorUtility.CopySerialized(prior,binding);binding.sourceMesh=source;binding.bakedMesh=baked;
            }
        }
        finally{AssetDatabase.StopAssetEditing();}
        old.name="Campus grass cover | before species variety";old.SetActive(false);old.tag="EditorOnly";root.name="Campus dense grass cover";root.AddComponent<CampusVegetationQuality>();
        // Keep water-side willows; broaden the upland grove silhouettes with two additional native species.
        var trees=UnityEngine.Object.FindObjectsByType<CampusTerrainBinding>(FindObjectsSortMode.None).Where(b=>b.kind==CampusTerrainBinding.BindingKind.Vegetation&&b.GetComponent<LODGroup>()).OrderBy(b=>b.transform.position.x).ThenBy(b=>b.transform.position.z).ToArray();
        var archive=new GameObject("Tree variety authoring archive");archive.tag="EditorOnly";archive.SetActive(false);
        var leafMaterials=new Dictionary<string,Material>();int replaced=0,colored=0;
        for(int i=0;i<trees.Length;i++)
        {
            var tree=trees[i];bool willow=tree.name.IndexOf("Willow",StringComparison.OrdinalIgnoreCase)>=0;
            if(!willow&&i%5==0)
            {
                var oldBounds=BoundsOf(tree.gameObject);string species=i%2==0?"BroadleafTree_02_Green":"BroadleafTree_05_Green";
                var prefab=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Idyllic Fantasy Nature/Prefabs/"+species+".prefab");
                var next=(GameObject)PrefabUtility.InstantiatePrefab(prefab,tree.transform.parent);
                next.transform.SetPositionAndRotation(tree.transform.position,tree.transform.rotation);
                var newBounds=BoundsOf(next);float factor=Mathf.Min(oldBounds.size.y/newBounds.size.y,Mathf.Min(oldBounds.size.x/newBounds.size.x,oldBounds.size.z/newBounds.size.z));next.transform.localScale*=factor;
                var binding=next.AddComponent<CampusTerrainBinding>();EditorUtility.CopySerialized(tree,binding);binding.sourceId+=" | variety "+species;
                tree.transform.SetParent(archive.transform,true);tree.gameObject.SetActive(false);tree=binding;replaced++;
            }
            int tone=rng.Next(TreeColors.Length);
            foreach(var renderer in tree.GetComponentsInChildren<Renderer>())
            {
                renderer.sharedMaterials=renderer.sharedMaterials.Select(m=>
                {
                    bool bark=m.name.IndexOf("Bark",StringComparison.OrdinalIgnoreCase)>=0;
                    if(bark)return AssetDatabase.LoadAssetAtPath<Material>("Assets/CampusSim/Generated/Materials/CampusBark.mat");
                    var texture=m.HasProperty("_BaseMap")?m.GetTexture("_BaseMap"):m.mainTexture;
                    if(!texture)texture=AssetDatabase.LoadAssetAtPath<Texture2D>(willow?"Assets/Idyllic Fantasy Nature/Textures/Trees/WillowTree_Branch.png":"Assets/Idyllic Fantasy Nature/Textures/Trees/BroadleafTree_Leaves.png");
                    string key=texture.GetInstanceID()+"_"+tone;
                    if(!leafMaterials.TryGetValue(key,out var material))
                    {
                        material=new Material(AssetDatabase.LoadAssetAtPath<Material>("Assets/CampusSim/Generated/Materials/CampusLeaves.mat")){name="Grove leaf "+key,enableInstancing=true};
                        material.SetTexture("_BaseMap",texture);material.SetColor("_BaseColor",TreeColors[tone]);material.SetColor("_ColorDim",TreeColors[tone]*.65f);material.SetFloat("_UnityShadowPower",.25f);
                        AssetDatabase.CreateAsset(material,Folder+"/Tree_"+leafMaterials.Count+".mat");leafMaterials[key]=material;
                    }
                    return material;
                }).ToArray();
            }
            colored++;
        }
        CampusWillowCanopy.Apply();
        CampusTerrainAuthoring.Bake();AssetDatabase.SaveAssets();EditorSceneManager.SaveScene(SceneManager.GetActiveScene());
        File.WriteAllText("Docs/MapResearch/Iterations/vegetation-variety.txt","Grass clumps by species 1/2/3="+string.Join("/",counts)+"; changed patch meshes="+changed+"; total patches="+patches.Length+". New blades fit within the previously accepted circular footprint and height. Three grass tones, four tree tones. Trees recolored="+colored+"; broadleaf 02/05 replacements="+replaced+". Existing willows and native LOD preserved. Prior grass/tree objects archived EditorOnly. Mobile performance not measured.\n");
    }
    static Bounds BoundsOf(GameObject go){var rs=go.GetComponentsInChildren<Renderer>();var b=rs[0].bounds;foreach(var r in rs)b.Encapsulate(r.bounds);return b;}
}
