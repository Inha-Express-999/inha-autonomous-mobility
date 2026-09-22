using System;
using System.Linq;
using InhaExpress.Client.Domain;
using InhaExpress.Client.Mobile;
using InhaExpress.Client.PC;
using InhaExpress.Client.Presentation;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

public static class ClientFixtureSetup
{
    private const string Folder = "Assets/CampusSim/Scenes/";

    [MenuItem("InhaExpress/Client/Configure Fixture HUD")]
    public static void Configure()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
            throw new InvalidOperationException("Configure fixture scenes in Edit Mode.");
        string[] names = { "PC_Bootstrap", "Mobile_Bootstrap", "PC_Operator", "Mobile_Passenger" };
        // Refuse unsaved target edits; never save the terrain or other unrelated scenes.
        foreach (string name in names)
        {
            string path = Folder + name + ".unity";
            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(path) == null)
                throw new InvalidOperationException("Missing scaffold: " + path);
            if (SceneManager.GetSceneByPath(path).isDirty)
                throw new InvalidOperationException("Save or revert the target scene first: " + path);
        }
        var active = SceneManager.GetActiveScene();
        try
        {
            foreach (string name in names)
            {
                string path = Folder + name + ".unity";
                var scene = SceneManager.GetSceneByPath(path);
                bool openedHere = !scene.isLoaded;
                if (openedHere) scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Additive);
                try
                {
                    bool mobile = name.StartsWith("Mobile", StringComparison.Ordinal);
                    if (name.EndsWith("Bootstrap", StringComparison.Ordinal))
                    {
                        var bootstrap = scene.GetRootGameObjects().SelectMany(x => x.GetComponentsInChildren<ClientBootstrap>(true)).Single();
                        Undo.RecordObject(bootstrap, "Configure fixture role");
                        var serialized = new SerializedObject(bootstrap);
                        serialized.FindProperty("clientRole").enumValueIndex = (int)(mobile ? ClientRole.Mobile_Passenger : ClientRole.PC_Operator);
                        serialized.FindProperty("useFixture").boolValue = true;
                        serialized.ApplyModifiedProperties();
                        // Undo dirty propagation can be deferred until the next editor update.
                        // This command saves immediately, including fields newly added to old scenes.
                        EditorSceneManager.MarkSceneDirty(scene);
                    }
                    else
                    {
                        var presenters = scene.GetRootGameObjects().SelectMany(x => x.GetComponentsInChildren<FixtureStatusPresenter>(true)).ToArray();
                        if (presenters.Length == 0)
                        {
                            var root = new GameObject("Fixture Status HUD");
                            SceneManager.MoveGameObjectToScene(root, scene);
                            Undo.RegisterCreatedObjectUndo(root, "Create fixture HUD");
                            if (mobile) Undo.AddComponent<PassengerStatusPresenter>(root);
                            else Undo.AddComponent<OperatorStatusPresenter>(root);
                        }
                        else if (presenters.Length != 1 || presenters[0].Role != (mobile ? ClientRole.Mobile_Passenger : ClientRole.PC_Operator))
                            throw new InvalidOperationException("Unexpected role presenters in " + path);
                    }
                    if (scene.isDirty && !EditorSceneManager.SaveScene(scene))
                        throw new InvalidOperationException("Could not save " + path);
                }
                finally { if (openedHere) EditorSceneManager.CloseScene(scene, true); }
            }
        }
        finally { if (active.IsValid() && active.isLoaded) SceneManager.SetActiveScene(active); }
    }
}
