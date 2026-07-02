using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.TestTools.TestRunner.Api;
using UnityEngine;

namespace Narazaka.VRChat.CostumeDashboard.Editor.Test
{
    public static class TestRunnerCli
    {
        const string ResultPath = "Library/CostumeDashboardTestResults.json";

        [MenuItem("Tools/Costume Dashboard/Run Tests")]
        public static void RunAll()
        {
            if (File.Exists(ResultPath)) File.Delete(ResultPath);
            var api = ScriptableObject.CreateInstance<TestRunnerApi>();
            api.RegisterCallbacks(new ResultWriter());
            api.Execute(new ExecutionSettings(new Filter
            {
                testMode = TestMode.EditMode,
                assemblyNames = new[] { "Narazaka.VRChat.CostumeDashboard.Test" },
            }));
        }

        class ResultWriter : ICallbacks
        {
            readonly List<string> failures = new List<string>();
            int passed;
            int failed;

            public void RunStarted(ITestAdaptor testsToRun) { }
            public void TestStarted(ITestAdaptor test) { }

            public void TestFinished(ITestResultAdaptor result)
            {
                if (result.HasChildren) return;
                if (result.TestStatus == TestStatus.Passed) passed++;
                else if (result.TestStatus == TestStatus.Failed)
                {
                    failed++;
                    failures.Add($"{result.FullName}: {result.Message}");
                }
            }

            public void RunFinished(ITestResultAdaptor result)
            {
                var sb = new StringBuilder();
                sb.Append("{\"passed\":").Append(passed).Append(",\"failed\":").Append(failed).Append(",\"failures\":[");
                for (var i = 0; i < failures.Count; i++)
                {
                    if (i > 0) sb.Append(',');
                    sb.Append('"').Append(failures[i].Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n").Replace("\r", "")).Append('"');
                }
                sb.Append("]}");
                File.WriteAllText(ResultPath, sb.ToString());
                Debug.Log($"[CostumeDashboard] Tests finished: passed={passed} failed={failed}");
            }
        }
    }
}
