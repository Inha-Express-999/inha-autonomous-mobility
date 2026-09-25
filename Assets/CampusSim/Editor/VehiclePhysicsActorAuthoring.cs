using System;
using System.IO;
using InhaExpress.Client.Presentation;
using UnityEditor;
using UnityEngine;

public static class VehiclePhysicsActorAuthoring
{
    private const string PrefabFolder = "Assets/CampusSim/Prefabs/Vehicles";
    private static readonly VehicleVariant[] Variants =
    {
        new VehicleVariant("Annyoung", "Assets/CampusSim/Prefabs/Annyoung Car.prefab"),
        new VehicleVariant("Default", "Assets/CampusSim/Prefabs/DefaultCar.prefab"),
        new VehicleVariant("Induck", "Assets/CampusSim/Prefabs/InduckCar.prefab")
    };

    [MenuItem("InhaExpress/Vehicles/Create Physics Actor Prefabs")]
    public static void CreatePhysicsActorPrefabs()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
            throw new InvalidOperationException("Create vehicle actor prefabs in Edit Mode.");

        EnsureFolder("Assets/CampusSim/Prefabs");
        EnsureFolder(PrefabFolder);
        foreach (var variant in Variants) CreateActorPrefab(variant);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
    }

    [MenuItem("InhaExpress/Vehicles/Add Ego Localization Reporter")]
    public static void AddEgoLocalizationReporter()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
            throw new InvalidOperationException("Update vehicle actor prefabs in Edit Mode.");

        foreach (var variant in Variants)
        {
            string path = PrefabFolder + "/VehicleActor_" + variant.Name + ".prefab";
            var actor = PrefabUtility.LoadPrefabContents(path);
            if (actor == null) throw new FileNotFoundException("Vehicle actor prefab is missing.", path);
            try
            {
                if (actor.GetComponent<VehicleEgoLocalizationReporter>() == null)
                    actor.AddComponent<VehicleEgoLocalizationReporter>();
                PrefabUtility.SaveAsPrefabAsset(actor, path);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(actor);
            }
        }
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
    }

    private static void CreateActorPrefab(VehicleVariant variant)
    {
        var visualPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(variant.SourcePath);
        if (visualPrefab == null)
            throw new FileNotFoundException("Vehicle visual prefab is missing.", variant.SourcePath);

        string outputPath = PrefabFolder + "/VehicleActor_" + variant.Name + ".prefab";
        if (AssetDatabase.LoadAssetAtPath<GameObject>(outputPath) != null)
            throw new InvalidOperationException("Refusing to overwrite an existing vehicle actor: " + outputPath);

        var actor = new GameObject("VehicleActor_" + variant.Name);
        try
        {
            actor.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            actor.transform.localScale = Vector3.one;

            var body = actor.AddComponent<Rigidbody>();
            body.isKinematic = true;
            body.useGravity = false;
            body.interpolation = RigidbodyInterpolation.Interpolate;
            body.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
            actor.AddComponent<VehicleEgoLocalizationReporter>();

            var visualRoot = new GameObject("Visual");
            visualRoot.transform.SetParent(actor.transform, false);
            visualRoot.transform.localPosition = Vector3.zero;
            visualRoot.transform.localRotation = Quaternion.identity;
            visualRoot.transform.localScale = Vector3.one;

            var visual = PrefabUtility.InstantiatePrefab(visualPrefab) as GameObject;
            if (visual == null) throw new InvalidOperationException("Could not instantiate " + variant.SourcePath);
            visual.name = variant.Name + "Visual";
            visual.transform.SetParent(visualRoot.transform, false);
            visual.transform.localPosition = Vector3.zero;
            visual.transform.localRotation = visualPrefab.transform.localRotation;
            visual.transform.localScale = visualPrefab.transform.localScale;

            var renderers = visual.GetComponentsInChildren<Renderer>(true);
            if (renderers.Length == 0)
                throw new InvalidOperationException("Vehicle visual has no renderers: " + variant.SourcePath);

            Bounds visualBounds = renderers[0].bounds;
            for (int i = 1; i < renderers.Length; i++) visualBounds.Encapsulate(renderers[i].bounds);
            if (visualBounds.size.x <= 0f || visualBounds.size.y <= 0f || visualBounds.size.z <= 0f)
                throw new InvalidOperationException("Vehicle visual bounds are invalid: " + variant.SourcePath);

            var collider = actor.AddComponent<BoxCollider>();
            collider.center = visualBounds.center;
            collider.size = visualBounds.size;

            var saved = PrefabUtility.SaveAsPrefabAsset(actor, outputPath);
            if (saved == null) throw new InvalidOperationException("Could not save " + outputPath);
            Debug.LogFormat("Created {0}: measured render envelope {1} m; kinematic Rigidbody shell.",
                outputPath, visualBounds.size);
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(actor);
        }
    }

    private static void EnsureFolder(string path)
    {
        if (AssetDatabase.IsValidFolder(path)) return;
        int separator = path.LastIndexOf('/');
        if (separator <= 0) throw new InvalidOperationException("Invalid asset folder: " + path);
        string parent = path.Substring(0, separator);
        string leaf = path.Substring(separator + 1);
        EnsureFolder(parent);
        AssetDatabase.CreateFolder(parent, leaf);
    }

    private sealed class VehicleVariant
    {
        public string Name { get; }
        public string SourcePath { get; }
        public VehicleVariant(string name, string sourcePath)
        {
            Name = name;
            SourcePath = sourcePath;
        }
    }
}
