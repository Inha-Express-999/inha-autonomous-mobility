using System;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Sockets;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using Debug = UnityEngine.Debug;

// Editor-owned development process only. No process launcher is shipped in Players.
[InitializeOnLoad]
public static class LocalDevelopmentServer
{
    private const string EnabledKey = "CampusSim.Editor.AutoStartServer";
    private const string PidKey = "CampusSim.Editor.ServerPid";
    private const string StartKey = "CampusSim.Editor.ServerStartTicks";
    private const string MenuPath = "InhaExpress/Development/Auto Start Local Server";

    static LocalDevelopmentServer()
    {
        EditorApplication.playModeStateChanged += OnPlayModeChanged;
        EditorApplication.quitting += StopOwnedServer;
    }

    [MenuItem(MenuPath)]
    private static void Toggle() => EditorPrefs.SetBool(EnabledKey, !EditorPrefs.GetBool(EnabledKey, true));

    [MenuItem(MenuPath, true)]
    private static bool ValidateToggle()
    {
        Menu.SetChecked(MenuPath, EditorPrefs.GetBool(EnabledKey, true));
        return true;
    }

    private static void OnPlayModeChanged(PlayModeStateChange state)
    {
        if (state == PlayModeStateChange.ExitingEditMode && EditorPrefs.GetBool(EnabledKey, true)
            && SceneManager.GetActiveScene().path == "Assets/CampusSim/Scenes/PC_Bootstrap.unity")
            StartServer();
        if (state == PlayModeStateChange.EnteredPlayMode
            && SceneManager.GetActiveScene().path == "Assets/CampusSim/Scenes/PC_Bootstrap.unity")
            Application.runInBackground = true;
        if (state == PlayModeStateChange.ExitingPlayMode) StopOwnedServer();
    }

    private static void StartServer()
    {
        string endpoint = PlayerPrefs.GetString("CampusSim.ServerWebSocketUrl", "ws://127.0.0.1:8765/v1/client/ws");
        if (!Uri.TryCreate(endpoint, UriKind.Absolute, out var uri)
            || (uri.Scheme != "ws" && uri.Scheme != "wss") || !uri.IsLoopback)
        {
            Debug.Log("[CampusSim] Remote endpoint selected; local server auto-start skipped.");
            return;
        }
        int port = uri.Port;
        if (PortIsOpen(port))
        {
            Debug.Log($"[CampusSim] Port {port} already has a listener. Reusing configured endpoint; no process will be stopped.");
            return;
        }
        // The built-in Python development service speaks plain HTTP/WebSocket.
        if (uri.Scheme != "ws")
        {
            Debug.LogWarning("[CampusSim] Automatic local server requires ws://. Start a TLS server separately for wss://.");
            return;
        }
        try
        {
            string root = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            string python = FindPython(root);
            string logFolder = Path.Combine(root, "artifacts", "editor-server");
            Directory.CreateDirectory(logFolder);
            var info = new ProcessStartInfo
            {
                FileName = python,
                Arguments = $"\"{Path.Combine(root, "AgentScripts", "RunEditorServer.py")}\" --port {port}",
                WorkingDirectory = root,
                UseShellExecute = false,
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden
            };
            info.EnvironmentVariables["PYTHONPATH"] = Path.Combine(root, "backend", "src");
            using (var process = Process.Start(info))
            {
                if (process == null) throw new InvalidOperationException("Python process did not start.");
                SessionState.SetInt(PidKey, process.Id);
                SessionState.SetString(StartKey, process.StartTime.ToUniversalTime().Ticks.ToString());
            }
            Debug.Log($"[CampusSim] Starting development server on port {port}. Log: artifacts/editor-server/server-{port}.log. Submit the connection form once the server is ready.");
        }
        catch (Exception error)
        {
            Debug.LogError("[CampusSim] Could not start Python server. Prepare backend dependencies or start it manually. " + error.Message);
        }
    }

    private static string FindPython(string root)
    {
        foreach (string relative in new[] { "backend/.venv/Scripts/python.exe", ".venv/Scripts/python.exe", "tmp/mvp-venv/Scripts/python.exe" })
        {
            string candidate = Path.Combine(root, relative);
            if (File.Exists(candidate)) return candidate;
        }
        return "python";
    }

    private static bool PortIsOpen(int port)
    {
        using (var socket = new TcpClient())
        {
            try { return socket.ConnectAsync(IPAddress.Loopback, port).Wait(250) && socket.Connected; }
            catch { return false; }
        }
    }

    private static void StopOwnedServer()
    {
        int pid = SessionState.GetInt(PidKey, 0);
        string started = SessionState.GetString(StartKey, "");
        SessionState.EraseInt(PidKey);
        SessionState.EraseString(StartKey);
        if (pid <= 0) return;
        try
        {
            using (var process = Process.GetProcessById(pid))
                if (!process.HasExited && process.StartTime.ToUniversalTime().Ticks.ToString() == started)
                    process.Kill();
        }
        catch (ArgumentException) { } // Server already exited.
        catch (Exception error) { Debug.LogWarning("[CampusSim] Could not stop owned development server: " + error.Message); }
    }
}
