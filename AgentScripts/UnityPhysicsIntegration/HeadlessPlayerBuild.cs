using System.IO;
using UnityEditor;
using UnityEditor.TestTools;
using UnityEngine;
using UnityEngine.TestTools;

[assembly: TestPlayerBuildModifier(typeof(HeadlessPlayerBuild))]
[assembly: PostBuildCleanup(typeof(HeadlessPlayerBuild))]

// Copied into an Editor-only assembly in the isolated validation project.
public sealed class HeadlessPlayerBuild : ITestPlayerBuildModifier, IPostBuildCleanup
{
    private static string executable;

    public BuildPlayerOptions ModifyOptions(BuildPlayerOptions options)
    {
        options.options &= ~(BuildOptions.AutoRunPlayer | BuildOptions.ConnectToHost);
        executable = Path.GetFullPath(Path.Combine(Application.dataPath, "../TestPlayer/Integration.exe"));
        Directory.CreateDirectory(Path.GetDirectoryName(executable));
        options.locationPathName = executable;
        return options;
    }

    public void Cleanup()
    {
        if (executable != null)
            EditorApplication.delayCall += () => EditorApplication.Exit(File.Exists(executable) ? 0 : 1);
    }
}
