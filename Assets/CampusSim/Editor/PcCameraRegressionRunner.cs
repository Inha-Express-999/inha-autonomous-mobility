using System.IO;
using UnityEditor;
using UnityEditor.TestTools.TestRunner.Api;
using UnityEngine;

// Callback registration must survive the domain reload when Editor MCP starts PlayMode tests.
[InitializeOnLoad]
public static class PcCameraRegressionRunner
{
    private const string PendingKey = "CampusSim.CameraRegression.Pending";
    static PcCameraRegressionRunner()
    {
        if (SessionState.GetBool(PendingKey, false)) TestRunnerApi.RegisterTestCallback(new Results());
    }

    public static void Run()
    {
        SessionState.SetBool(PendingKey, true);
        var api = ScriptableObject.CreateInstance<TestRunnerApi>();
        api.RegisterCallbacks(new Results());
        api.Execute(new ExecutionSettings(new Filter
        {
            testMode = TestMode.PlayMode,
            testNames = new[] { "InhaExpress.Client.Tests.PcCameraNavigationTests" }
        }));
    }

    private sealed class Results : ICallbacks
    {
        public void RunStarted(ITestAdaptor test) { }
        public void TestStarted(ITestAdaptor test) { }
        public void TestFinished(ITestResultAdaptor result) { }
        public void RunFinished(ITestResultAdaptor result)
        {
            if (!SessionState.GetBool(PendingKey, false)) return;
            SessionState.SetBool(PendingKey, false);
            string folder = Path.Combine(Directory.GetParent(Application.dataPath).FullName, "artifacts", "pc_operator_" + Application.version);
            Directory.CreateDirectory(folder);
            TestRunnerApi.SaveResultToFile(result, Path.Combine(folder, "playmode.xml"));
            Debug.Log($"CAMPUS_CAMERA_TESTS passed={result.PassCount} failed={result.FailCount}");
        }
    }
}
