using System.Globalization;
using System.Linq;
using System.Text;
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

        /// <summary>LLM入力用の行指向コンパクト形式（JSONの括弧・引用符・キー反復を省いてトークン数を圧縮。
        /// 書式の説明は LayerExplainer のsystemプロンプトに同梱される）</summary>
        public static string ToCompact(AnimatorLayerModel model)
        {
            var sb = new StringBuilder();
            sb.AppendLine($"# layer {LayerId(model)}");
            var source = model.Source;
            if (source != null && source.Kind == AnimatorSourceKind.MergeAnimator)
                sb.AppendLine($"source: MergeAnimator {source.LayerType} @{source.ComponentPath} controller={source.ControllerName}");
            else if (source != null)
                sb.AppendLine($"source: Descriptor {source.LayerType} controller={source.ControllerName}");
            var layerFlags = (model.IsAdditive ? " additive" : "") + (model.MaskName != null ? $" mask={model.MaskName}" : "");
            sb.AppendLine($"layer: {model.LayerName} index={model.LayerIndex} weight={F(model.Weight)}{layerFlags}");
            foreach (var p in model.Parameters)
            {
                var ep = p.InExpressionParameters ? $" [EP{(p.Synced ? " synced" : "")}{(p.Saved ? " saved" : "")}]" : "";
                sb.AppendLine($"param: {p.Name} {p.Type} default={F(p.DefaultValue)}{ep}");
            }
            foreach (var s in model.States)
            {
                var kind = s.Pseudo != PseudoNodeKind.None ? " " + s.Pseudo : "";
                var flags = (s.IsDefault ? " default" : "")
                    + (s.Scope != "" ? $" scope={s.Scope}" : "")
                    + (s.WriteDefaults ? " wd" : "")
                    + (s.Speed != 0f && s.Speed != 1f ? $" speed={F(s.Speed)}" : "");
                sb.AppendLine($"node: {s.Id}{kind}{(s.Pseudo == PseudoNodeKind.None ? " " + s.Name : "")}{flags}");
                if (s.Motion != null) AppendMotion(sb, s.Motion, "  ");
                if (s.MotionTimeParameter != null) sb.AppendLine($"  motiontime: {s.MotionTimeParameter}");
                foreach (var b in s.Bindings)
                {
                    sb.AppendLine($"  bind: {CategoryShort(b.Category)} {(b.Path ?? "-")} {b.Property} = {b.ValueSummary}");
                }
                foreach (var behaviour in s.Behaviours)
                {
                    var details = behaviour.Details.Count > 0 ? ": " + string.Join("; ", behaviour.Details) : "";
                    sb.AppendLine($"  sb: {behaviour.Type}{details}");
                }
            }
            foreach (var t in model.Transitions)
            {
                var conditions = t.Conditions.Count > 0
                    ? " " + string.Join(" & ", t.Conditions.Select(c => $"{c.Parameter} {c.Mode} {F(c.Threshold)}"))
                    : "";
                var flags = (t.IsDefault ? " default" : "") + (t.HasExitTime ? $" exitTime={F(t.ExitTime)}" : "");
                sb.AppendLine($"edge: {t.FromId}->{t.ToId}{conditions}{flags}");
            }
            if (model.AffectedCostumes.Count > 0) sb.AppendLine("affects: " + string.Join(", ", model.AffectedCostumes));
            return sb.ToString();
        }

        static void AppendMotion(StringBuilder sb, MotionInfo motion, string indent)
        {
            if (!motion.IsBlendTree)
            {
                sb.AppendLine($"{indent}motion: clip {motion.ClipName}");
                return;
            }
            var param = motion.BlendParameter != null ? $" param={motion.BlendParameter}" : "";
            var paramY = motion.BlendParameterY != null ? $" paramY={motion.BlendParameterY}" : "";
            sb.AppendLine($"{indent}motion: tree {motion.BlendType}{param}{paramY}");
            if (motion.Children == null) return;
            foreach (var child in motion.Children)
            {
                var key = child.DirectParameter != null ? $"param={child.DirectParameter}"
                    : motion.BlendType == "Simple1D" ? F(child.Threshold)
                    : $"({F(child.PositionX)},{F(child.PositionY)})";
                if (child.Motion == null)
                {
                    sb.AppendLine($"{indent}  child: {key} (なし)");
                }
                else if (child.Motion.IsBlendTree)
                {
                    sb.AppendLine($"{indent}  child: {key}");
                    AppendMotion(sb, child.Motion, indent + "    ");
                }
                else
                {
                    sb.AppendLine($"{indent}  child: {key} clip {child.Motion.ClipName}");
                }
            }
        }

        static string CategoryShort(BindingCategory category)
        {
            switch (category)
            {
                case BindingCategory.GameObjectActive: return "active";
                case BindingCategory.BlendShape: return "blendshape";
                case BindingCategory.MaterialProperty: return "matprop";
                case BindingCategory.MaterialSwap: return "matswap";
                case BindingCategory.Transform: return "transform";
                case BindingCategory.Humanoid: return "humanoid";
                default: return "other";
            }
        }

        static string F(float value) => value.ToString("0.###", CultureInfo.InvariantCulture);
    }
}
