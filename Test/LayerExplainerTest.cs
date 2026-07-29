using System.Collections.Generic;
using System.IO;
using NUnit.Framework;

namespace Narazaka.VRChat.CostumeDashboard.Editor.Test
{
    public class LayerExplainerTest
    {
        [Test]
        public void CacheKey_DiffersByContentAndModel()
        {
            var a = LayerExplainer.CacheKey("{\"x\":1}", "model-a");
            Assert.That(a, Is.EqualTo(LayerExplainer.CacheKey("{\"x\":1}", "model-a"))); // 決定的
            Assert.That(a, Is.Not.EqualTo(LayerExplainer.CacheKey("{\"x\":2}", "model-a")));
            Assert.That(a, Is.Not.EqualTo(LayerExplainer.CacheKey("{\"x\":1}", "model-b")));
            Assert.That(a, Does.Match("^[0-9a-f]{40}$"));
        }

        [Test]
        public void UserPrompt_ContainsIdsAndJson()
        {
            var prompt = LayerExplainer.UserPrompt(new List<(string, string)>
            {
                ("FX/L1", "{\"id\":\"FX/L1\"}"),
                ("FX/L2", "{\"id\":\"FX/L2\"}"),
            });
            Assert.That(prompt, Does.Contain("FX/L1"));
            Assert.That(prompt, Does.Contain("{\"id\":\"FX/L2\"}"));
        }

        [Test]
        public void Chunk_SplitsBySize()
        {
            var layers = new List<(string, string)>
            {
                ("a", new string('x', 60)),
                ("b", new string('x', 60)),
                ("c", new string('x', 200)), // 単独で上限超過でも単独チャンクとして送る
            };
            var chunks = LayerExplainer.Chunk(layers, 100);
            Assert.That(chunks.Count, Is.EqualTo(3));
            Assert.That(chunks[0][0].Id, Is.EqualTo("a"));
            Assert.That(chunks[2][0].Id, Is.EqualTo("c"));
        }

        [Test]
        public void Chunk_PacksWithinLimit()
        {
            var layers = new List<(string, string)> { ("a", "12345"), ("b", "12345"), ("c", "12345") };
            var chunks = LayerExplainer.Chunk(layers, 12);
            Assert.That(chunks.Count, Is.EqualTo(2)); // 5+5 <= 12、次の5で超過
        }

        [Test]
        public void ParseResponse_PlainAndFenced()
        {
            var expected = new Dictionary<string, string> { { "FX/L1", "説明文" } };
            Assert.That(LayerExplainer.ParseResponse("{\"FX/L1\":\"説明文\"}"), Is.EqualTo(expected));
            Assert.That(LayerExplainer.ParseResponse("```json\n{\"FX/L1\":\"説明文\"}\n```"), Is.EqualTo(expected));
            Assert.That(LayerExplainer.ParseResponse("これは説明: ```json\n{\"FX/L1\":\"説明文\"}\n``` 以上"), Is.EqualTo(expected));
            Assert.That(LayerExplainer.ParseResponse("not json"), Is.Null);
        }

        [Test]
        public void Cache_SaveAndLoadRoundtrip()
        {
            var path = "Library/CostumeDashboardLlmCacheTest.json";
            if (File.Exists(path)) File.Delete(path);
            Assert.That(LayerExplainer.LoadCache(path), Is.Empty); // ファイル無し → 空
            var cache = new Dictionary<string, string> { { "key1", "説明1" }, { "key2", "説明2" } };
            LayerExplainer.SaveCache(path, cache);
            Assert.That(LayerExplainer.LoadCache(path), Is.EqualTo(cache));
            File.Delete(path);
        }
    }
}
