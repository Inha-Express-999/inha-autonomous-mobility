using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

public static class MobileClientBuild
{
    public static readonly string[] Scenes = {
        "Assets/CampusSim/Scenes/Mobile_Bootstrap.unity",
        "Assets/CampusSim/Scenes/CampusWorld.unity",
        "Assets/CampusSim/Scenes/CampusTerrain.unity",
        "Assets/CampusSim/Scenes/Mobile_Passenger.unity"
    };

    [MenuItem("InhaExpress/Build/Mobile Android APK")]
    public static void BuildAndroid()
    {
        if (!BuildPipeline.IsBuildTargetSupported(BuildTargetGroup.Android, BuildTarget.Android))
            throw new BuildFailedException("Install Android Build Support, SDK/NDK and OpenJDK for Unity " + Application.unityVersion + " in Unity Hub.");
        Build("artifacts/mobile-client/InhaMobil.apk", BuildTarget.Android);
    }

    [MenuItem("InhaExpress/Build/Mobile Windows Preview")]
    public static void BuildWindowsPreview() => Build(".utmp/mobile-client/InhaMobilMobile.exe", BuildTarget.StandaloneWindows64);

    [MenuItem("InhaExpress/Build/PC LAN Demo")]
    public static void BuildPcDemo() => Build("artifacts/pc_lan_demo/InhaMobil.exe", BuildTarget.StandaloneWindows64, false);

    private static void Build(string path, BuildTarget target, bool mobile = true)
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Stop Play before building.");
        Directory.CreateDirectory(Path.GetDirectoryName(path));
        var previousHttp = PlayerSettings.insecureHttpOption;
        bool previousInternet = PlayerSettings.Android.forceInternetPermission;
        var previousOrientation = PlayerSettings.defaultInterfaceOrientation;
        int previousWidth = PlayerSettings.defaultScreenWidth, previousHeight = PlayerSettings.defaultScreenHeight;
        bool previousResizable = PlayerSettings.resizableWindow;
        var previousFullscreen = PlayerSettings.fullScreenMode;
        var graphicsFiles = new Dictionary<string, byte[]>();
        foreach (var file in new[] { "Assets/Settings/PC_RPAsset.asset", "Assets/Settings/Mobile_RPAsset.asset", "Assets/Settings/UniversalRenderPipelineGlobalSettings.asset" })
            if (File.Exists(file)) graphicsFiles.Add(file, File.ReadAllBytes(file));
        try
        {
            // The demonstration server is HTTP on a private LAN. Keep this explicit in the mobile build.
            PlayerSettings.insecureHttpOption = InsecureHttpOption.AlwaysAllowed;
            PlayerSettings.Android.forceInternetPermission = true;
            if (mobile) PlayerSettings.defaultInterfaceOrientation = UIOrientation.Portrait;
            PlayerSettings.bundleVersion = File.ReadAllText("VERSION").Trim();
            if (target == BuildTarget.StandaloneWindows64 && mobile)
            {
                PlayerSettings.defaultScreenWidth = 390; PlayerSettings.defaultScreenHeight = 844;
                PlayerSettings.fullScreenMode = FullScreenMode.Windowed; PlayerSettings.resizableWindow = true;
            }
            var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions { scenes = mobile ? Scenes : new[] {
                "Assets/CampusSim/Scenes/PC_Bootstrap.unity", "Assets/CampusSim/Scenes/CampusWorld.unity",
                "Assets/CampusSim/Scenes/CampusTerrain.unity", "Assets/CampusSim/Scenes/PC_Operator.unity" },
                locationPathName = path, target = target, subtarget = (int)StandaloneBuildSubtarget.Player, options = BuildOptions.Development });
            if (report.summary.result != BuildResult.Succeeded)
                throw new BuildFailedException("Mobile build failed: " + report.summary.result);
            Debug.Log((mobile ? "CAMPUS_MOBILE_BUILD_SUCCEEDED " : "CAMPUS_PC_LAN_BUILD_SUCCEEDED ") + report.summary.outputPath);
        }
        finally
        {
            PlayerSettings.insecureHttpOption = previousHttp;
            PlayerSettings.Android.forceInternetPermission = previousInternet;
            PlayerSettings.defaultInterfaceOrientation = previousOrientation;
            PlayerSettings.defaultScreenWidth = previousWidth; PlayerSettings.defaultScreenHeight = previousHeight;
            PlayerSettings.fullScreenMode = previousFullscreen; PlayerSettings.resizableWindow = previousResizable;
            AssetDatabase.DisallowAutoRefresh();
            try
            {
                foreach (var pair in graphicsFiles)
                {
                    // Keep temporary files outside Assets so Unity does not create companion metadata.
                    string temporary = Path.Combine("Temp", "InhaMobil-build-" + Guid.NewGuid().ToString("N"));
                    File.WriteAllBytes(temporary, pair.Value);
                    File.Replace(temporary, pair.Key, null);
                    AssetDatabase.ImportAsset(pair.Key, ImportAssetOptions.ForceUpdate);
                    EditorUtility.ClearDirty(AssetDatabase.LoadMainAssetAtPath(pair.Key));
                }
            }
            finally { AssetDatabase.AllowAutoRefresh(); }
        }
    }
}
