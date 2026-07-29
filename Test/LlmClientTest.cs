using System.Linq;
using NUnit.Framework;
using Newtonsoft.Json.Linq;

namespace Narazaka.VRChat.CostumeDashboard.Editor.Test
{
    public class LlmClientTest
    {
        static LlmSettings OpenAi() => new LlmSettings { Provider = LlmProvider.OpenAiCompatible, Model = "gpt-x", ApiKey = "sk-test" };
        static LlmSettings Anthropic() => new LlmSettings { Provider = LlmProvider.Anthropic, Model = "claude-x", ApiKey = "sk-ant-test" };

        [Test]
        public void BuildRequest_OpenAiCompatible()
        {
            var req = LlmClient.BuildRequest(OpenAi(), "sys", "user text");
            Assert.That(req.Url, Is.EqualTo("https://api.openai.com/v1/chat/completions"));
            Assert.That(req.Headers.Single(h => h.Name == "Authorization").Value, Is.EqualTo("Bearer sk-test"));
            var body = JObject.Parse(req.Body);
            Assert.That((string)body["model"], Is.EqualTo("gpt-x"));
            Assert.That((string)body["messages"][0]["role"], Is.EqualTo("system"));
            Assert.That((string)body["messages"][0]["content"], Is.EqualTo("sys"));
            Assert.That((string)body["messages"][1]["role"], Is.EqualTo("user"));
        }

        [Test]
        public void BuildRequest_Anthropic()
        {
            var req = LlmClient.BuildRequest(Anthropic(), "sys", "user text");
            Assert.That(req.Url, Is.EqualTo("https://api.anthropic.com/v1/messages"));
            Assert.That(req.Headers.Single(h => h.Name == "x-api-key").Value, Is.EqualTo("sk-ant-test"));
            Assert.That(req.Headers.Single(h => h.Name == "anthropic-version").Value, Is.EqualTo("2023-06-01"));
            var body = JObject.Parse(req.Body);
            Assert.That((string)body["model"], Is.EqualTo("claude-x"));
            Assert.That((string)body["system"], Is.EqualTo("sys"));
            Assert.That((int)body["max_tokens"], Is.GreaterThan(0));
            Assert.That((string)body["messages"][0]["role"], Is.EqualTo("user"));
        }

        [Test]
        public void BuildRequest_CustomEndpoint()
        {
            var settings = OpenAi();
            settings.Endpoint = "http://localhost:1234/v1/chat/completions";
            Assert.That(LlmClient.BuildRequest(settings, "s", "u").Url, Is.EqualTo("http://localhost:1234/v1/chat/completions"));
        }

        [Test]
        public void ParseContent_OpenAiCompatible()
        {
            var content = LlmClient.ParseContent(LlmProvider.OpenAiCompatible,
                "{\"choices\":[{\"message\":{\"role\":\"assistant\",\"content\":\"hello\"}}]}");
            Assert.That(content, Is.EqualTo("hello"));
        }

        [Test]
        public void ParseContent_Anthropic()
        {
            var content = LlmClient.ParseContent(LlmProvider.Anthropic,
                "{\"content\":[{\"type\":\"text\",\"text\":\"hello\"}]}");
            Assert.That(content, Is.EqualTo("hello"));
        }

        [Test]
        public void ParseContent_Malformed_ReturnsNull()
        {
            Assert.That(LlmClient.ParseContent(LlmProvider.OpenAiCompatible, "{}"), Is.Null);
            Assert.That(LlmClient.ParseContent(LlmProvider.OpenAiCompatible, "not json"), Is.Null);
        }
    }
}
