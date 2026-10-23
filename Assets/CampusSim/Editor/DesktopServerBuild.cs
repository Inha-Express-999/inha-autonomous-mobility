using System;
using System.Diagnostics;
using System.IO;
using InhaExpress.Client.MapClient;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

public sealed class DesktopServerBuild : IPostprocessBuildWithReport
{
    public int callbackOrder => 100;

    public static void BuildForVerification()
    {
        var profile = AssetDatabase.LoadMainAssetAtPath("Assets/Settings/Build Profiles/Windows.asset");
        var serialized = new SerializedObject(profile);
        serialized.FindProperty("m_Subtarget").intValue = (int)StandaloneBuildSubtarget.Player;
        serialized.ApplyModifiedPropertiesWithoutUndo();
        AssetDatabase.SaveAssets();
        Directory.CreateDirectory(".utmp/pc-client");
        var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
        {
            scenes = new[] { "Assets/CampusSim/Scenes/PC_Bootstrap.unity", "Assets/CampusSim/Scenes/CampusWorld.unity",
                "Assets/CampusSim/Scenes/CampusTerrain.unity", "Assets/CampusSim/Scenes/PC_Operator.unity" },
            locationPathName = ".utmp/pc-client/InhaMobil.exe", target = BuildTarget.StandaloneWindows64,
            subtarget = (int)StandaloneBuildSubtarget.Player, options = BuildOptions.Development
        });
        if (report.summary.result != BuildResult.Succeeded)
            throw new BuildFailedException("PC verification build failed: " + report.summary.result);
        UnityEngine.Debug.Log("CAMPUS_PC_BUILD_SUCCEEDED " + report.summary.outputPath);
    }

    public void OnPostprocessBuild(BuildReport report)
    {
        if (report.summary.platform != BuildTarget.StandaloneWindows64) return;
        Package(Path.GetDirectoryName(report.summary.outputPath));
    }

    public static void Package(string playerDirectory)
    {
        string root = Directory.GetParent(Application.dataPath).FullName;
        string destination = Path.Combine(playerDirectory, "CampusServer");
        var info = new ProcessStartInfo
        {
            FileName = DesktopServerLauncher.ResolvePython(root, null),
            Arguments = "\"" + Path.Combine(root, "scripts/package_pc_server.py") + "\" --project-root \"" + root +
                "\" --destination \"" + destination + "\" --include-local-dependencies",
            WorkingDirectory = root, UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardOutput = true, RedirectStandardError = true
        };
        using (var process = Process.Start(info))
        {
            if (process == null) throw new BuildFailedException("Could not start server packaging.");
            string output = process.StandardOutput.ReadToEnd(), error = process.StandardError.ReadToEnd();
            process.WaitForExit();
            if (process.ExitCode != 0) throw new BuildFailedException("Server packaging failed: " + error);
            UnityEngine.Debug.Log("PC server bundle: " + output);
        }
    }
}
