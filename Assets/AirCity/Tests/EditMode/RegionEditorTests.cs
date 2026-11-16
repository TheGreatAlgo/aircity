using System.IO;
using UnityEditor;
using UnityEditor.TestTools.TestRunner.Api;
using UnityEngine;

namespace AirCity.Tests.Game
{
    /// <summary>Runs the affected suites in an already open editor without a second process or project lock.</summary>
    public static class RegionEditorTests
    {
        static TestRunnerApi runner;

        [MenuItem("AirCity/Game/Run Region Tests")]
        public static void Run()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                Debug.LogWarning("Stop Play mode before running the region EditMode tests.");
                return;
            }
            if (runner != null)
            {
                Debug.LogWarning("Wait for the current test run to finish before running region tests.");
                return;
            }
            Directory.CreateDirectory("Logs"); File.WriteAllText("Logs/region-tests.txt", "Running region, construction, save and clock tests.\n");
            runner = ScriptableObject.CreateInstance<TestRunnerApi>();
            runner.RegisterCallbacks(new Callbacks());
            runner.Execute(new ExecutionSettings(new Filter { testMode = TestMode.EditMode,
                groupNames = new[] { "^AirCity\\.Tests\\.(Game|World)\\." } }));
        }

        sealed class Callbacks : ICallbacks
        {
            public void RunStarted(ITestAdaptor testsToRun) { }
            public void TestStarted(ITestAdaptor test) { }
            public void TestFinished(ITestResultAdaptor result)
            {
                if (!result.HasChildren && result.FailCount > 0)
                    File.AppendAllText("Logs/region-tests.txt", result.FullName + ": " + result.Message + "\n" + result.StackTrace + "\n");
            }
            public void RunFinished(ITestResultAdaptor result)
            {
                TestRunnerApi.SaveResultToFile(result, Path.GetFullPath("Logs/region-tests.xml"));
                string summary = $"{result.ResultState}: {result.PassCount} passed, {result.FailCount} failed, {result.SkipCount} skipped ({result.Duration:0.0}s).";
                File.AppendAllText("Logs/region-tests.txt", summary + "\n"); Debug.Log("Region tests: " + summary);
                if (runner != null) Object.DestroyImmediate(runner); runner = null;
            }
        }
    }
}
