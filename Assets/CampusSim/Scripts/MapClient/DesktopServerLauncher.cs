using System;
using System.Collections;
using System.Diagnostics;
using System.IO;
using UnityEngine;
using UnityEngine.Networking;

namespace InhaExpress.Client.MapClient
{
    /// <summary>PC Bootstrap owns this process; additive/map/mobile scenes never start another server.</summary>
    public sealed class DesktopServerLauncher : MonoBehaviour
    {
        public string serverUrl = "http://127.0.0.1:8765";
        public string serverRoot;
        public string pythonExecutable;
        public bool Ready { get; private set; }
        public bool OwnsServer => ownedProcess != null;
        public int OwnedProcessId => ownedProcess == null ? 0 : ownedProcess.Id;
        public string Status { get; private set; } = "Not started";
        public string RunId { get; private set; }
        private Process ownedProcess;
        private UnityWebRequest request;
        private bool starting;

        [Serializable] private sealed class Info
        {
            public string service_id, project_version, run_id, geometry_hash;
            public int schema_version, map_schema_version;
            public bool simulation_only;
        }

        public IEnumerator EnsureRunning()
        {
            if (starting || Ready) yield break;
            starting = true;
            Uri url;
            if (!Uri.TryCreate(serverUrl, UriKind.Absolute, out url) || url.Scheme != "http" ||
                !url.IsLoopback || url.AbsolutePath != "/")
            { Status = "Auto-start requires a loopback HTTP URL."; yield break; }
            yield return Probe();
            if (Ready) yield break;
            // An occupied endpoint with a different service must not be replaced.
            if (lastHttpResponse) { Status = "Port is occupied by an incompatible server: " + serverUrl; yield break; }
#if UNITY_EDITOR_WIN || UNITY_STANDALONE_WIN
            try
            {
                string root = ResolveRoot(serverRoot);
                string python = ResolvePython(root, pythonExecutable);
                var info = new ProcessStartInfo
                {
                    FileName = python,
                    Arguments = "-m campus_sim.desktop_server --parent-pipe --port " + url.Port,
                    WorkingDirectory = root,
                    UseShellExecute = false, CreateNoWindow = true,
                    RedirectStandardInput = true
                };
                string paths = Path.Combine(root, "backend", "src");
                string localDependencies = Path.Combine(root, ".utmp", "fifo-dijkstra-deps");
                if (Directory.Exists(localDependencies)) paths += Path.PathSeparator + localDependencies;
                string previous = Environment.GetEnvironmentVariable("PYTHONPATH");
                if (!string.IsNullOrEmpty(previous)) paths += Path.PathSeparator + previous;
                info.EnvironmentVariables["PYTHONPATH"] = paths;
                ownedProcess = Process.Start(info);
                if (ownedProcess == null) throw new InvalidOperationException("Python process did not start.");
                Status = "Starting local server (PID " + ownedProcess.Id + ")";
            }
            catch (Exception error) { Status = "Local server startup failed: " + error.Message; yield break; }
            double deadline = Time.realtimeSinceStartupAsDouble + 30;
            while (!Ready && Time.realtimeSinceStartupAsDouble < deadline)
            {
                if (ownedProcess.HasExited) { Status = "Python exited. Check server root, dependencies and map hashes."; yield break; }
                yield return new WaitForSecondsRealtime(.25f);
                yield return Probe();
            }
            if (!Ready) Status = "Server startup timed out. Check local Python/server configuration.";
#else
            Status = "Automatic local server launch is supported on Windows PC only.";
#endif
        }

        private bool lastHttpResponse;
        private IEnumerator Probe()
        {
            request = UnityWebRequest.Get(serverUrl.TrimEnd('/') + "/v1/desktop/info");
            request.timeout = 2;
            yield return request.SendWebRequest();
            lastHttpResponse = request.responseCode != 0;
            try
            {
                if (request.result == UnityWebRequest.Result.Success)
                {
                    var info = JsonUtility.FromJson<Info>(request.downloadHandler.text);
                    if (info != null && info.service_id == "inha-mobil-local-demo" && info.simulation_only &&
                        info.schema_version == 1 && info.map_schema_version == 1 && !string.IsNullOrEmpty(info.run_id))
                    {
#if UNITY_EDITOR
                        string snapshot = Path.Combine(Application.dataPath, "CampusSim/Data/server_map.json");
                        if (File.Exists(snapshot) && ServerMapDocument.Parse(File.ReadAllText(snapshot)).geometry_hash != info.geometry_hash)
                        { Status = "Server map differs from edited locations. Stop Play and apply locations again."; yield break; }
#endif
                        Ready = true; RunId = info.run_id; Status = ownedProcess == null ? "Reusing existing local server" : "Local server ready";
                    }
                }
            }
            catch (Exception error) { Status = "Server identity check failed: " + error.Message; }
            finally { request.Dispose(); request = null; }
        }

        public static string ResolveRoot(string configured)
        {
            string root = string.IsNullOrWhiteSpace(configured) ? Environment.GetEnvironmentVariable("CAMPUS_SIM_SERVER_ROOT") : configured;
            if (string.IsNullOrWhiteSpace(root))
                root = Application.isEditor ? Directory.GetParent(Application.dataPath).FullName :
                    Path.Combine(Directory.GetParent(Application.dataPath).FullName, "CampusServer");
            root = Path.GetFullPath(root);
            if (!File.Exists(Path.Combine(root, "backend", "src", "campus_sim", "desktop_server.py")) ||
                !File.Exists(Path.Combine(root, "maps", "inha_osm_runtime", "manifest.json")))
                throw new DirectoryNotFoundException("Set CAMPUS_SIM_SERVER_ROOT to the repository/server bundle root: " + root);
            return root;
        }

        public static string ResolvePython(string root, string configured)
        {
            string executable = string.IsNullOrWhiteSpace(configured) ? Environment.GetEnvironmentVariable("CAMPUS_SIM_PYTHON") : configured;
            if (!string.IsNullOrWhiteSpace(executable)) return executable;
            foreach (string path in new[] { ".venv/Scripts/python.exe", "backend/.venv/Scripts/python.exe" })
            {
                string candidate = Path.Combine(root, path);
                if (File.Exists(candidate)) return candidate;
            }
            return "python.exe";
        }

        private void OnDestroy()
        {
            if (request != null) { request.Abort(); request.Dispose(); request = null; }
            if (ownedProcess == null) return;
            try
            {
                if (!ownedProcess.HasExited)
                {
                    ownedProcess.StandardInput.WriteLine("shutdown"); ownedProcess.StandardInput.Flush();
                    ownedProcess.StandardInput.Close();
                }
            }
            catch (Exception error) { UnityEngine.Debug.LogWarning("Owned server shutdown: " + error.Message); }
            finally { ownedProcess.Dispose(); ownedProcess = null; }
        }

        private void OnGUI()
        {
            if (!Ready) GUI.Label(new Rect(12, 195, 850, 45), "PC server: " + Status);
        }
    }
}
