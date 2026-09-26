using System;
using System.IO;
using System.Xml;
using NUnit.Framework.Interfaces;
using UnityEngine;
using UnityEngine.TestRunner;

[assembly: TestRunCallback(typeof(PlayerResults))]

// Preserve the full NUnit result without requiring a running Editor connection.
public sealed class PlayerResults : ITestRunCallback
{
    public void RunStarted(ITest testsToRun) { }
    public void TestStarted(ITest test) { }
    public void TestFinished(ITestResult result) { }

    public void RunFinished(ITestResult result)
    {
        if (Application.isEditor) return;
        var path = Environment.GetEnvironmentVariable("INHA_UNITY_E2E_RESULTS");
        var document = new XmlDocument();
        var run = document.CreateElement("test-run");
        run.SetAttribute("result", result.ResultState.Status.ToString());
        run.SetAttribute("total", result.Test.TestCaseCount.ToString());
        run.SetAttribute("passed", result.PassCount.ToString());
        run.SetAttribute("failed", result.FailCount.ToString());
        run.SetAttribute("skipped", result.SkipCount.ToString());
        run.InnerXml = result.ToXml(true).OuterXml;
        document.AppendChild(run);
        File.WriteAllText(path, document.OuterXml);
        Application.Quit(result.ResultState.Status == TestStatus.Passed ? 0 : 1);
    }
}
