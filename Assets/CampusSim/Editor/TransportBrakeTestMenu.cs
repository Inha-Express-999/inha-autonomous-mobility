using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using UnityEditor;
using UnityEditor.TestTools.TestRunner.Api;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace InhaExpress.EditorTools
{
    // Editor-only, fixed test selection. A request file never supplies code or command arguments.
    [InitializeOnLoad]
    public static class TransportBrakeTestMenu
    {
        private const string ResultKey = "InhaExpress.TransportBrakeTests.Result";
        private static TestRunnerApi api;
        private static double nextPoll;
        private static string Root => Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
        static TransportBrakeTestMenu()
        {
            EditorApplication.delayCall += () =>
            {
                api = ScriptableObject.CreateInstance<TestRunnerApi>();
                api.RegisterCallbacks(new Results());
                EditorApplication.update += PollRequest;
            };
        }

        private static void PollRequest()
        {
            if (EditorApplication.timeSinceStartup < nextPoll) return;
            nextPoll = EditorApplication.timeSinceStartup + 1;
            if (EditorApplication.isCompiling || EditorApplication.isUpdating ||
                EditorApplication.isPlayingOrWillChangePlaymode || SessionState.GetString(ResultKey, "") != "") return;
            string request = Path.Combine(Root, "tmp", "run-transport-brake-editor.request");
            if (!File.Exists(request)) return;
            File.Delete(request);
            Run();
        }

        [MenuItem("InhaExpress/Validation/Run Transport Brake Tests")]
        public static void Run()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling ||
                SessionState.GetString(ResultKey, "") != "")
                throw new InvalidOperationException("Wait until Editor and the previous transport test are idle.");
            for (int i = 0; i < SceneManager.sceneCount; i++)
                if (SceneManager.GetSceneAt(i).isDirty)
                    throw new InvalidOperationException("Save your scene changes before running transport tests.");
            string folder = Path.Combine(Root, "tmp", "transport-editor-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(folder);
            string[] roots = { "Assets/CampusSim/Scripts", "Assets/CampusSim/Tests/PlayMode", "backend/src", "configs", "maps/fixtures", "Assets/CampusSim/Prefabs/Vehicles", "Assets/CampusSim/Models" };
            var paths = roots.SelectMany(p => Directory.GetFiles(Path.Combine(Root, p), "*", SearchOption.AllDirectories))
                .Where(p => p.EndsWith(".cs") || p.EndsWith(".py") || p.EndsWith(".json") || p.EndsWith(".asmdef") || p.EndsWith(".prefab") || p.EndsWith(".fbx") || p.EndsWith(".meta"))
                .Concat(new[] { Path.Combine(Root, "AgentScripts/brake_runtime_fixture.py"),
                    Path.Combine(Root, "AgentScripts/brake_socket_fixture.py"),
                    Path.Combine(Root, "Assets/CampusSim/Editor/TransportBrakeTestMenu.cs") });
            using (var sha = SHA256.Create())
            {
                var manifest = new Manifest { editor = Application.unityVersion,
                    scope = "Editor PlayMode transport tests; synthetic Physics setup, not campus driving",
                    sources = paths.Select(p => new Source { path = p.Substring(Root.Length + 1).Replace('\\', '/'),
                        sha256 = BitConverter.ToString(sha.ComputeHash(File.ReadAllBytes(p))).Replace("-", "") }).ToArray() };
                File.WriteAllText(Path.Combine(folder, "source-manifest.json"), JsonUtility.ToJson(manifest, true));
            }
            SessionState.SetString(ResultKey, Path.Combine(folder, "results.xml"));
            File.WriteAllText(Path.Combine(Root, "tmp", "transport-editor-latest.txt"), folder);
            try
            {
                api.Execute(new ExecutionSettings(new Filter { testMode = TestMode.PlayMode,
                    testNames = new[] { "InhaExpress.Client.Tests.TransportBrakePlayModeTests", "InhaExpress.Client.Tests.TeamVehicleBrakePlayModeTests" } }));
            }
            catch { SessionState.EraseString(ResultKey); throw; }
        }

        [Serializable] private class Source { public string path; public string sha256; }
        [Serializable] private class Manifest { public string editor; public string scope; public Source[] sources; }
        private sealed class Results : ICallbacks
        {
            public void RunStarted(ITestAdaptor tests) { }
            public void TestStarted(ITestAdaptor test) { }
            public void TestFinished(ITestResultAdaptor result) { }
            public void RunFinished(ITestResultAdaptor result)
            {
                string path = SessionState.GetString(ResultKey, "");
                if (path == "") return;
                try { TestRunnerApi.SaveResultToFile(result, path); Debug.Log("Transport test results: " + path); }
                finally { SessionState.EraseString(ResultKey); }
            }
        }
    }
}
