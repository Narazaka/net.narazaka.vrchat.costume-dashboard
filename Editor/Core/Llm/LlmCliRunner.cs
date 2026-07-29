using System;
using System.Diagnostics;
using System.Text;
using System.Threading;

namespace Narazaka.VRChat.CostumeDashboard.Editor
{
    /// <summary>ローカルCLIコマンド（claude -p / codex exec 等）でLLMを呼ぶ実行器。
    /// プロンプトは引数でなくstdinで渡す（引数長・クォートのOS差を避ける）</summary>
    public static class LlmCliRunner
    {
        const int TimeoutMs = 600000;

        public static ProcessStartInfo BuildStartInfo(string command)
        {
            // PATH 解決と .cmd シム（npm 配布の claude 等）のためシェル経由で起動する
            var isWindows = Environment.OSVersion.Platform == PlatformID.Win32NT;
            return new ProcessStartInfo
            {
                FileName = isWindows ? "cmd.exe" : "/bin/sh",
                Arguments = isWindows ? "/c " + command : "-c \"" + command.Replace("\"", "\\\"") + "\"",
                UseShellExecute = false,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8,
            };
        }

        /// <summary>同期実行（ワーカースレッド・テスト用）。stdin にUTF-8で input を書いて stdout/stderr を回収する</summary>
        public static (string Output, string Error, int ExitCode) RunBlocking(string command, string input)
        {
            using (var process = Process.Start(BuildStartInfo(command)))
            {
                // StandardInputEncoding は .NET Framework に無いため BaseStream へ直接UTF-8で書く
                var bytes = Encoding.UTF8.GetBytes(input ?? "");
                process.StandardInput.BaseStream.Write(bytes, 0, bytes.Length);
                process.StandardInput.Close();
                var output = new StringBuilder();
                var error = new StringBuilder();
                process.OutputDataReceived += (_, e) => { if (e.Data != null) output.AppendLine(e.Data); };
                process.ErrorDataReceived += (_, e) => { if (e.Data != null) error.AppendLine(e.Data); };
                process.BeginOutputReadLine();
                process.BeginErrorReadLine();
                if (!process.WaitForExit(TimeoutMs))
                {
                    process.Kill();
                    return (output.ToString(), "タイムアウト (" + TimeoutMs / 1000 + "秒)", -1);
                }
                process.WaitForExit(); // 非同期読み取りのフラッシュ完了を待つ
                return (output.ToString(), error.ToString(), process.ExitCode);
            }
        }

        /// <summary>非同期実行。メインスレッドの SynchronizationContext 上で onDone(stdout, error) を呼ぶ。
        /// エラー時（起動失敗・非0終了・タイムアウト）は error 非 null</summary>
        public static void Run(string command, string input, Action<string, string> onDone)
        {
            var context = SynchronizationContext.Current;
            new Thread(() =>
            {
                string output = null;
                string error = null;
                try
                {
                    var result = RunBlocking(command, input);
                    if (result.ExitCode != 0)
                    {
                        error = $"CLI終了コード {result.ExitCode}: {result.Error}";
                        output = result.Output;
                    }
                    else
                    {
                        output = result.Output;
                    }
                }
                catch (Exception ex)
                {
                    error = "CLI起動失敗: " + ex.Message;
                }
                if (context != null) context.Post(_ => onDone(output, error), null);
                else onDone(output, error);
            })
            { IsBackground = true }.Start();
        }
    }
}
