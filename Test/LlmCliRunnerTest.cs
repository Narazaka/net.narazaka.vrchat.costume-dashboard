using System;
using NUnit.Framework;

namespace Narazaka.VRChat.CostumeDashboard.Editor.Test
{
    public class LlmCliRunnerTest
    {
        static bool IsWindows => Environment.OSVersion.Platform == PlatformID.Win32NT;

        [Test]
        public void BuildStartInfo_WrapsWithShell()
        {
            var info = LlmCliRunner.BuildStartInfo("claude -p");
            if (IsWindows)
            {
                Assert.That(info.FileName, Is.EqualTo("cmd.exe"));
                Assert.That(info.Arguments, Is.EqualTo("/c claude -p"));
            }
            else
            {
                Assert.That(info.FileName, Is.EqualTo("/bin/sh"));
                Assert.That(info.Arguments, Does.Contain("claude -p"));
            }
            Assert.That(info.RedirectStandardInput, Is.True);
            Assert.That(info.RedirectStandardOutput, Is.True);
            Assert.That(info.UseShellExecute, Is.False);
        }

        [Test]
        public void RunBlocking_EchoesOutput()
        {
            var result = LlmCliRunner.RunBlocking(IsWindows ? "echo hello" : "echo hello", "");
            Assert.That(result.ExitCode, Is.EqualTo(0));
            Assert.That(result.Output, Does.Contain("hello"));
        }

        [Test]
        public void RunBlocking_PassesStdin()
        {
            // Windows: findstr . は stdin の非空行をそのまま出力する。他OSは cat
            var result = LlmCliRunner.RunBlocking(IsWindows ? "findstr ." : "cat", "stdin-content-テスト");
            Assert.That(result.ExitCode, Is.EqualTo(0));
            Assert.That(result.Output, Does.Contain("stdin-content-テスト"));
        }

        [Test]
        public void RunBlocking_NonZeroExitCode()
        {
            var result = LlmCliRunner.RunBlocking(IsWindows ? "exit 3" : "exit 3", "");
            Assert.That(result.ExitCode, Is.EqualTo(3));
        }

        [Test]
        public void LlmSettings_CacheKeyModelAndIsReady()
        {
            var api = new LlmSettings { Provider = LlmProvider.Anthropic, Model = "claude-x", ApiKey = "k", Command = "claude -p" };
            Assert.That(api.CacheKeyModel, Is.EqualTo("claude-x"));
            Assert.That(api.IsReady, Is.True);
            api.ApiKey = "";
            Assert.That(api.IsReady, Is.False);

            var cli = new LlmSettings { Provider = LlmProvider.Cli, Command = "claude -p --model opus" };
            Assert.That(cli.CacheKeyModel, Is.EqualTo("claude -p --model opus"));
            Assert.That(cli.IsReady, Is.True);
            cli.Command = "";
            Assert.That(cli.IsReady, Is.False);
        }
    }
}
