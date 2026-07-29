using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using Newtonsoft.Json.Linq;

namespace Narazaka.VRChat.CostumeDashboard.Editor
{
    /// <summary>layer構造モデルの構造化JSON。これだけ読めば Animator の直接読解が不要になる自己完結表現
    /// （LLM入力・クリップボードコピー・キャッシュキーに共用）</summary>
    public static class LayerModelSerializer
    {
        // DefaultValueHandling.Ignore で既定値(0/false/null)を省略しJSONを小さくする（LLM入力サイズ対策）。
        // 「省略時は既定値」の契約は LayerExplainer のプロンプト側で補足する
        static readonly JsonSerializerSettings Settings = new JsonSerializerSettings
        {
            NullValueHandling = NullValueHandling.Ignore,
            DefaultValueHandling = DefaultValueHandling.Ignore,
            Converters = { new StringEnumConverter() },
        };

        public static string LayerId(AnimatorLayerModel model)
        {
            var source = model.Source;
            var prefix = source != null && source.Kind == AnimatorSourceKind.MergeAnimator
                ? $"{source.LayerType}[{source.ComponentPath}]"
                : source != null ? source.LayerType : "?";
            return $"{prefix}/{model.LayerName}";
        }

        public static string ToJson(AnimatorLayerModel model, bool indented)
        {
            var obj = JObject.FromObject(model, JsonSerializer.Create(Settings));
            obj.AddFirst(new JProperty("id", LayerId(model)));
            return obj.ToString(indented ? Formatting.Indented : Formatting.None);
        }
    }
}
