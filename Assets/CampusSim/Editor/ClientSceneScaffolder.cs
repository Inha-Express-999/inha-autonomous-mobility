using System;
using InhaExpress.Client.Presentation;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

public static class ClientSceneScaffolder
{
    private const string SceneFolder = "Assets/CampusSim/Scenes";
    private const string CampusTerrainScene = SceneFolder + "/CampusTerrain.unity";
    private const string CampusWorldScene = SceneFolder + "/CampusWorld.unity";
    private const string PcBootstrapScene = SceneFolder + "/PC_Bootstrap.unity";
    private const string PcRoleScene = SceneFolder + "/PC_Operator.unity";
    private const string MobileBootstrapScene = SceneFolder + "/Mobile_Bootstrap.unity";
    private const string MobileRoleScene = SceneFolder + "/Mobile_Passenger.unity";

    [MenuItem("InhaExpress/Client/Create Scene Scaffold")]
    public static void CreateSceneScaffold()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            throw new InvalidOperationException("Create the client scene scaffold in Edit Mode.");
        }

        if (!AssetDatabase.IsValidFolder(SceneFolder))
        {
            throw new InvalidOperationException($"Scene folder does not exist: {SceneFolder}");
        }

        if (AssetDatabase.LoadAssetAtPath<SceneAsset>(CampusTerrainScene) == null)
        {
            throw new InvalidOperationException($"Campus terrain scene does not exist: {CampusTerrainScene}");
        }

        CreateCampusWorldScene();
        CreateRoleScene(PcRoleScene, "PC Operator Root");
        CreateRoleScene(MobileRoleScene, "Mobile Passenger Root");
        CreateBootstrapScene(PcBootstrapScene, "PC Client Bootstrap", PcRoleScene);
        CreateBootstrapScene(MobileBootstrapScene, "Mobile Client Bootstrap", MobileRoleScene);
        AssetDatabase.SaveAssets();

        Debug.Log("Created InhaExpress client scene scaffold without changing Build Settings.");
    }

    private static void CreateCampusWorldScene()
    {
        var scene = CreateEmptyScene(CampusWorldScene);
        var root = new GameObject("Campus World Root");
        SceneManager.MoveGameObjectToScene(root, scene);
        root.AddComponent<AdditiveSceneLoader>().Configure(CampusTerrainScene);
        SaveScene(scene, CampusWorldScene);
    }

    private static void CreateRoleScene(string scenePath, string rootName)
    {
        var scene = CreateEmptyScene(scenePath);
        var root = new GameObject(rootName);
        SceneManager.MoveGameObjectToScene(root, scene);
        SaveScene(scene, scenePath);
    }

    private static void CreateBootstrapScene(string scenePath, string rootName, string roleScenePath)
    {
        var scene = CreateEmptyScene(scenePath);
        var root = new GameObject(rootName);
        SceneManager.MoveGameObjectToScene(root, scene);
        root.AddComponent<ClientBootstrap>().Configure(CampusWorldScene, roleScenePath);
        SaveScene(scene, scenePath);
    }

    private static Scene CreateEmptyScene(string scenePath)
    {
        var existing = AssetDatabase.LoadAssetAtPath<SceneAsset>(scenePath);
        if (existing != null)
        {
            throw new InvalidOperationException($"Refusing to overwrite existing scene: {scenePath}");
        }

        return EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
    }

    private static void SaveScene(Scene scene, string scenePath)
    {
        if (!EditorSceneManager.SaveScene(scene, scenePath))
        {
            throw new InvalidOperationException($"Failed to save scene: {scenePath}");
        }

        EditorSceneManager.CloseScene(scene, true);
    }
}
