using System;
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
        // Unity プロセスの cwd に依存しないようプロジェクトルート基準の絶対パスにする
        static string ResultPath => Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Library", "CostumeDashboardTestResults.json"));

        [MenuItem("Tools/Costume Dashboard/Run Tests")]
        public static void RunAll()
        {
            if (File.Exists(ResultPath)) File.Delete(ResultPath);
            var api = ScriptableObject.CreateInstance<TestRunnerApi>();
            var writer = new ResultWriter(api);
            api.RegisterCallbacks(writer);
            api.Execute(new ExecutionSettings(new Filter
            {
                testMode = TestMode.EditMode,
                assemblyNames = new[] { "Narazaka.VRChat.CostumeDashboard.Test" },
            }));
        }

        class ResultWriter : ICallbacks
        {
            readonly TestRunnerApi api;
            readonly List<string> failures = new List<string>();
            int passed;
            int failed;
            int inconclusive;
            int skipped;

            public ResultWriter(TestRunnerApi api)
            {
                this.api = api;
            }

            public void RunStarted(ITestAdaptor testsToRun)
            {
                // RunAll は毎回新しい ResultWriter を作るため通常は不要だが、防御的にゼロクリアしておく
                failures.Clear();
                passed = 0;
                failed = 0;
                inconclusive = 0;
                skipped = 0;
            }

            public void TestStarted(ITestAdaptor test) { }

            public void TestFinished(ITestResultAdaptor result)
            {
                if (result.HasChildren) return;
                switch (result.TestStatus)
                {
                    case TestStatus.Passed:
                        passed++;
                        break;
                    case TestStatus.Failed:
                        failed++;
                        failures.Add($"{result.FullName}: {result.Message}");
                        break;
                    case TestStatus.Inconclusive:
                        inconclusive++;
                        break;
                    case TestStatus.Skipped:
                        skipped++;
                        break;
                }
            }

            public void RunFinished(ITestResultAdaptor result)
            {
                var finishedAt = DateTime.UtcNow.ToString("o");
                var sb = new StringBuilder();
                sb.Append("{\"passed\":").Append(passed)
                    .Append(",\"failed\":").Append(failed)
                    .Append(",\"inconclusive\":").Append(inconclusive)
                    .Append(",\"skipped\":").Append(skipped)
                    .Append(",\"finishedAt\":\"").Append(finishedAt).Append('"')
                    .Append(",\"failures\":[");
                for (var i = 0; i < failures.Count; i++)
                {
                    if (i > 0) sb.Append(',');
                    sb.Append('"').Append(failures[i].Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n").Replace("\r", "")).Append('"');
                }
                sb.Append("]}");
                File.WriteAllText(ResultPath, sb.ToString());
                Debug.Log($"[CostumeDashboard] Tests finished: passed={passed} failed={failed} inconclusive={inconclusive} skipped={skipped}");

                // コールバック登録先 (CallbacksHolder) はドメイン単位のグローバルで、登録したままだと
                // 次回以降の RunAll() 呼び出しでも過去の ResultWriter が TestFinished を受け取り続け、
                // カウンタが累積したまま複数の書き手が同じ結果ファイルを取り合う事故になる。
                // 確実な対処が難しい内部挙動のため、素直に「実行終了時に自分の登録を解除する」形にする
                api.UnregisterCallbacks(this);
            }
        }
    }
}
