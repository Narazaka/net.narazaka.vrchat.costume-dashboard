using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Narazaka.VRChat.CostumeDashboard.Editor
{
    /// <summary>複雑layerのLLM説明生成。未生成分を一括で1リクエストに乗せ（上限超過時のみlayer境界で分割）、
    /// layer単位で内容ハッシュキャッシュする</summary>
    public static class LayerExplainer
    {
        /// <summary>プロンプト変更時にインクリメントして全キャッシュを無効化する</summary>
        public const string PromptVersion = "2";
        public const string DefaultCachePath = "UserSettings/CostumeDashboardLlmCache.json";

        public static string CacheKey(string layerJson, string model)
        {
            using (var sha1 = SHA1.Create())
            {
                var bytes = sha1.ComputeHash(Encoding.UTF8.GetBytes(PromptVersion + "\n" + model + "\n" + layerJson));
                var sb = new StringBuilder();
                foreach (var b in bytes) sb.Append(b.ToString("x2"));
                return sb.ToString();
            }
        }

        public static string SystemPrompt()
        {
            return
"あなたはVRChatアバター改変の専門家です。VRChatアバターのAnimator layerをコンパクトなテキスト形式で受け取り、改変ユーザー向けに日本語で説明します。\n" +
"入力書式（1行=1要素。状態遷移はノードIDで結ばれた平坦なグラフで、循環も表現される）:\n" +
"- `# layer <id>` レイヤーの開始\n" +
"- `source:` 由来（MA Merge Animator @コンポーネントパス / Avatar Descriptor）、`layer:` 名前・weight・additive・mask\n" +
"- `param: <名前> <型> default=<値> [EP synced saved]` レイヤーが参照するパラメーター。EP=Expression Parameters登録（メニュー等から操作されうる）\n" +
"- `node: <ID> [Entry|AnyState|Exit] [<状態名>] [default] [scope=<サブステートマシン>] [wd] [speed=..]` 状態ノード。Entry/AnyState/Exitは擬似ノード。インデント行は付属情報:\n" +
"  - `motion: clip <名前>` / `motion: tree <種別> param=..` と `child: <しきい値|param=..> clip <名前>` (BlendTree)\n" +
"  - `motiontime: <パラメーター>` パラメーター値でアニメ時間を直接駆動（ラジアル等）\n" +
"  - `bind: <種別> <パス> <プロパティ> = <値要約>` アニメーション対象。種別=active(オブジェクトON/OFF)/blendshape/matprop/matswap/transform/humanoid/other。`a→b` は時間0→終端の変化、単値は定数\n" +
"  - `sb: <型>: <要約>` StateMachineBehaviour（VRCAvatarParameterDriver等）\n" +
"- `edge: <from>-><to> [<param> <If|IfNot|Equals|NotEqual|Greater|Less> <値> [& ...]] [default] [exitTime=..]` 遷移。default=Entryの暗黙遷移\n" +
"- `affects:` 作用先の登録衣装名\n" +
"各layerについて以下を数行で説明してください:\n" +
"1. 何で動くか（どのパラメーターのどの値で。EP登録状況も考慮）\n" +
"2. 何を動かすか（どのオブジェクト/BlendShape/マテリアルを）\n" +
"3. 振る舞い（トグル・切替・連続変化・多段ギミック等、遷移構造の意味）\n" +
"複数layerが同じパラメーターを使う・VRCAvatarParameterDriver で連携する場合は、その関係にも触れてください。\n" +
"出力は入力の id をキーとするJSONオブジェクトのみを返します: {\"<id>\": \"説明文\", ...}";
        }

        public static string UserPrompt(List<(string Id, string Json)> layers)
        {
            var sb = new StringBuilder();
            sb.AppendLine("以下のAnimator layer群を説明してください。");
            foreach (var (id, json) in layers)
            {
                sb.AppendLine($"### {id}");
                sb.AppendLine(json);
            }
            return sb.ToString();
        }

        public static List<List<(string Id, string Json)>> Chunk(List<(string Id, string Json)> layers, int maxChars)
        {
            var result = new List<List<(string Id, string Json)>>();
            var current = new List<(string Id, string Json)>();
            var size = 0;
            foreach (var layer in layers)
            {
                var len = layer.Json.Length;
                if (current.Count > 0 && size + len > maxChars)
                {
                    result.Add(current);
                    current = new List<(string Id, string Json)>();
                    size = 0;
                }
                current.Add(layer);
                size += len;
            }
            if (current.Count > 0) result.Add(current);
            return result;
        }

        public static Dictionary<string, string> ParseResponse(string content)
        {
            if (content == null) return null;
            var text = content.Trim();
            // コードフェンスや前置きが混ざっても最初の { から最後の } までを取り出す
            var start = text.IndexOf('{');
            var end = text.LastIndexOf('}');
            if (start < 0 || end <= start) return null;
            try
            {
                var json = JObject.Parse(text.Substring(start, end - start + 1));
                var result = new Dictionary<string, string>();
                foreach (var prop in json.Properties()) result[prop.Name] = (string)prop.Value;
                return result;
            }
            catch (Exception)
            {
                return null;
            }
        }

        public static Dictionary<string, string> LoadCache(string path)
        {
            if (!File.Exists(path)) return new Dictionary<string, string>();
            try
            {
                return JsonConvert.DeserializeObject<Dictionary<string, string>>(File.ReadAllText(path))
                    ?? new Dictionary<string, string>();
            }
            catch (Exception)
            {
                return new Dictionary<string, string>();
            }
        }

        public static void SaveCache(string path, Dictionary<string, string> cache)
        {
            var dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            File.WriteAllText(path, JsonConvert.SerializeObject(cache, Formatting.Indented));
        }

        /// <summary>チャンクを逐次送信して id→説明 を集約。エラー時は中断し部分結果とエラーを返す</summary>
        public static void GenerateAsync(LlmSettings settings, List<(string Id, string Json)> layers, Action<int, int> onProgress, Action<Dictionary<string, string>, string> onDone)
        {
            var chunks = Chunk(layers, settings.MaxInputChars);
            var results = new Dictionary<string, string>();
            void Complete(int index, string content, string error)
            {
                if (error != null) { onDone(results, error); return; }
                var parsed = ParseResponse(content);
                if (parsed == null) { onDone(results, "応答のJSONパースに失敗: " + Truncate(content, 200)); return; }
                foreach (var kv in parsed) results[kv.Key] = kv.Value;
                SendChunk(index + 1);
            }

            void SendChunk(int index)
            {
                if (index >= chunks.Count) { onDone(results, null); return; }
                if (onProgress != null) onProgress(index, chunks.Count);
                if (settings.Provider == LlmProvider.Cli)
                {
                    // CLI は system/user の区別が無いので連結してstdinで渡す
                    var prompt = SystemPrompt() + "\n\n" + UserPrompt(chunks[index]);
                    LlmCliRunner.Run(settings.Command, prompt, (stdout, error) => Complete(index, stdout, error));
                    return;
                }
                var request = LlmClient.BuildRequest(settings, SystemPrompt(), UserPrompt(chunks[index]));
                LlmClient.Send(request, (rawJson, error) =>
                {
                    Complete(index, error == null ? LlmClient.ParseContent(settings.Provider, rawJson) : null, error);
                });
            }
            SendChunk(0);
        }

        static string Truncate(string text, int max)
        {
            if (text == null) return "(空応答)";
            return text.Length <= max ? text : text.Substring(0, max) + "…";
        }
    }
}
