using System;
using System.IO;
using InhaExpress.Client.Domain;
using InhaExpress.Client.Presentation;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

// Exercises the real Bootstrap/Terrain/role assets without saving any scene edits.
[InitializeOnLoad]
public static class ClientCampusSmokeValidation
{
    private const string SessionPrefix = "CampusSim.CampusSmoke.";
    private const string AddressKey = "CampusSim.ServerWebSocketUrl";
    private const string IdentityKey = "CampusSim.PassengerSubscriberId";
    private static string roleName, originalScene, outputRoot, previousAddress, previousIdentity;
    private static bool hadAddress, hadIdentity, running, submitted, capturing;
    private static double startedAt, capturedAt;

    static ClientCampusSmokeValidation()
    {
        // Entering/stopping Play Mode reloads the Editor domain. Keep the
        // validation and preference cleanup alive across those reloads.
        if (!SessionState.GetBool(SessionPrefix + "cleanup", false)) return;
        roleName = SessionState.GetString(SessionPrefix + "role", "");
        originalScene = SessionState.GetString(SessionPrefix + "scene", "");
        outputRoot = SessionState.GetString(SessionPrefix + "output", "");
        previousAddress = SessionState.GetString(SessionPrefix + "address", "");
        previousIdentity = SessionState.GetString(SessionPrefix + "identity", "");
        hadAddress = SessionState.GetBool(SessionPrefix + "hadAddress", false);
        hadIdentity = SessionState.GetBool(SessionPrefix + "hadIdentity", false);
        startedAt = SessionState.GetFloat(SessionPrefix + "startedAt", 0);
        running = SessionState.GetBool(SessionPrefix + "running", false);
        if (running) EditorApplication.update += Poll;
        EditorApplication.playModeStateChanged += OnPlayModeChanged;
        if (!running && !EditorApplication.isPlayingOrWillChangePlaymode)
            EditorApplication.delayCall += () => OnPlayModeChanged(PlayModeStateChange.EnteredEditMode);
    }

    [Serializable]
    private sealed class Evidence
    {
        public bool success;
        public string scope, role, editorVersion, appVersion, mapVersion, runId, error;
        public long sequence, tick;
        public int screenWidth, screenHeight, runtimeHosts, eventSystems, vehicles, requests;
    }

    [MenuItem("InhaExpress/Validation/Run Campus PC Connection Smoke")]
    public static void RunPc() => Begin("PC");

    [MenuItem("InhaExpress/Validation/Run Campus Mobile Connection Smoke")]
    public static void RunMobile() => Begin("Mobile");

    private static void Begin(string role)
    {
        if (running || EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling)
            throw new InvalidOperationException("Run campus smoke in stable Edit Mode.");
        for (int i = 0; i < SceneManager.sceneCount; i++)
            if (SceneManager.GetSceneAt(i).isDirty)
                throw new InvalidOperationException("Save your open scene edits before running smoke validation.");
        originalScene = SceneManager.GetActiveScene().path;
        roleName = role;
        outputRoot = Path.GetFullPath("artifacts/validation/2026-10-08-campus-bootstrap/" + role.ToLowerInvariant());
        Directory.CreateDirectory(outputRoot);
        // Preserve normal app configuration; the server is a dedicated local test instance.
        hadAddress = PlayerPrefs.HasKey(AddressKey);
        hadIdentity = PlayerPrefs.HasKey(IdentityKey);
        previousAddress = PlayerPrefs.GetString(AddressKey);
        previousIdentity = PlayerPrefs.GetString(IdentityKey);
        EditorSceneManager.OpenScene("Assets/CampusSim/Scenes/" + role + "_Bootstrap.unity", OpenSceneMode.Single);
        running = true;
        submitted = capturing = false;
        startedAt = EditorApplication.timeSinceStartup;
        SessionState.SetBool(SessionPrefix + "cleanup", true);
        SessionState.SetBool(SessionPrefix + "running", true);
        SessionState.SetString(SessionPrefix + "role", roleName);
        SessionState.SetString(SessionPrefix + "scene", originalScene);
        SessionState.SetString(SessionPrefix + "output", outputRoot);
        SessionState.SetString(SessionPrefix + "address", previousAddress);
        SessionState.SetString(SessionPrefix + "identity", previousIdentity);
        SessionState.SetBool(SessionPrefix + "hadAddress", hadAddress);
        SessionState.SetBool(SessionPrefix + "hadIdentity", hadIdentity);
        SessionState.SetFloat(SessionPrefix + "startedAt", (float)startedAt);
        EditorApplication.update += Poll;
        EditorApplication.playModeStateChanged += OnPlayModeChanged;
        EditorApplication.isPlaying = true;
    }

    private static void Poll()
    {
        if (!running) return;
        try
        {
            if (EditorApplication.timeSinceStartup - startedAt > 90)
                throw new TimeoutException("Campus Bootstrap did not reach a valid server snapshot within 90 seconds.");
            if (!EditorApplication.isPlaying) return;
            if (!submitted)
            {
                var panel = UnityEngine.Object.FindFirstObjectByType<ClientConnectionPanel>();
                if (panel == null) return;
                panel.GetComponentInChildren<InputField>().text = "ws://127.0.0.1:18776/v1/client/ws";
                panel.GetComponentInChildren<Button>().onClick.Invoke();
                if (!panel.Submitted) throw new InvalidOperationException("Connection form rejected the test endpoint.");
                submitted = true;
            }
            var bootstrap = UnityEngine.Object.FindFirstObjectByType<ClientBootstrap>();
            var runtime = bootstrap != null ? bootstrap.Runtime : null;
            if (runtime == null || runtime.Store.Current == null || runtime.ConnectionState != ConnectionState.Connected) return;
            if (!SceneManager.GetSceneByPath("Assets/CampusSim/Scenes/CampusTerrain.unity").isLoaded ||
                !SceneManager.GetSceneByPath("Assets/CampusSim/Scenes/" + roleName + "_" +
                    (roleName == "PC" ? "Operator" : "Passenger") + ".unity").isLoaded)
                throw new InvalidOperationException("The real terrain and expected role scene must both be loaded.");
            int hosts = UnityEngine.Object.FindObjectsByType<ClientRuntimeHost>(FindObjectsSortMode.None).Length;
            int events = UnityEngine.Object.FindObjectsByType<EventSystem>(FindObjectsSortMode.None).Length;
            if (hosts != 1 || events != 1)
                throw new InvalidOperationException($"Expected one session and EventSystem, got {hosts}/{events}.");
            if (runtime.Role != (roleName == "PC" ? ClientRole.PC_Operator : ClientRole.Mobile_Passenger))
                throw new InvalidOperationException("Bootstrap initialized the wrong role.");
            if (!capturing)
            {
                EditorApplication.ExecuteMenuItem("Window/General/Game");
                ScreenCapture.CaptureScreenshot(Path.Combine(outputRoot, "game-view.png"));
                capturedAt = EditorApplication.timeSinceStartup;
                capturing = true;
                return;
            }
            if (EditorApplication.timeSinceStartup - capturedAt < 2 || !File.Exists(Path.Combine(outputRoot, "game-view.png"))) return;
            var snapshot = runtime.Store.Current;
            File.WriteAllText(Path.Combine(outputRoot, "result.json"), JsonUtility.ToJson(new Evidence
            {
                success = true,
                scope = "Real CampusTerrain and Bootstrap Editor connection; synthetic server graph; no campus vehicle driving",
                role = runtime.Role.ToString(), editorVersion = Application.unityVersion,
                appVersion = Application.version, mapVersion = snapshot.MapVersion, runId = snapshot.RunId,
                sequence = snapshot.Sequence, tick = snapshot.SimulationTick,
                screenWidth = Screen.width, screenHeight = Screen.height,
                runtimeHosts = hosts, eventSystems = events, vehicles = snapshot.Vehicles.Count,
                requests = snapshot.Requests.Count
            }, true));
            Debug.Log("Campus connection smoke passed: " + outputRoot);
            Stop();
        }
        catch (Exception error)
        {
            File.WriteAllText(Path.Combine(outputRoot, "result.json"), JsonUtility.ToJson(new Evidence
                { success = false, role = roleName, error = error.ToString() }, true));
            Debug.LogError("Campus connection smoke failed: " + error.Message);
            Stop();
        }
    }

    private static void Stop()
    {
        running = false;
        SessionState.SetBool(SessionPrefix + "running", false);
        EditorApplication.update -= Poll;
        EditorApplication.isPlaying = false;
    }

    private static void OnPlayModeChanged(PlayModeStateChange state)
    {
        if (state != PlayModeStateChange.EnteredEditMode) return;
        if (!SessionState.GetBool(SessionPrefix + "cleanup", false)) return;
        SessionState.SetBool(SessionPrefix + "cleanup", false);
        SessionState.SetBool(SessionPrefix + "running", false);
        EditorApplication.playModeStateChanged -= OnPlayModeChanged;
        EditorApplication.update -= Poll;
        running = false;
        if (hadAddress) PlayerPrefs.SetString(AddressKey, previousAddress); else PlayerPrefs.DeleteKey(AddressKey);
        if (hadIdentity) PlayerPrefs.SetString(IdentityKey, previousIdentity); else PlayerPrefs.DeleteKey(IdentityKey);
        PlayerPrefs.Save();
        if (!string.IsNullOrEmpty(originalScene)) EditorSceneManager.OpenScene(originalScene, OpenSceneMode.Single);
    }
}
