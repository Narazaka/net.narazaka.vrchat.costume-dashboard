using System.Collections.Generic;
using System.Linq;

namespace Narazaka.VRChat.CostumeDashboard.Editor
{
    public enum LayerPatternKind { BoolToggle, IntSelect, FloatContinuous, Complex, Empty }

    public class ClassificationResult
    {
        public LayerPatternKind Kind;
        /// <summary>機械的日本語要約。Complex のときは複雑理由（LLM 生成対象である旨は UI 側で付す）</summary>
        public string Summary;
        public string TargetSummary;
    }

    /// <summary>グラフモデル上のパターン判定。boolトグル各形 / int排他選択 / float連続 のみ機械分類し、他は Complex とする</summary>
    public static class LayerPatternClassifier
    {
        public static ClassificationResult Classify(AnimatorLayerModel model)
        {
            var real = model.States.Where(s => s.Pseudo == PseudoNodeKind.None).ToList();
            var allBindings = real.SelectMany(s => s.Bindings).ToList();
            var targetSummary = BuildTargetSummary(allBindings);
            var behaviours = real.SelectMany(s => s.Behaviours).ToList();

            if (model.Weight <= 0f) return new ClassificationResult { Kind = LayerPatternKind.Empty, Summary = "weight 0 のため無効", TargetSummary = targetSummary };
            if (real.Count == 0 || (allBindings.Count == 0 && behaviours.Count == 0))
                return new ClassificationResult { Kind = LayerPatternKind.Empty, Summary = "アニメーション対象なし", TargetSummary = targetSummary };

            var conditionParams = model.Transitions.SelectMany(t => t.Conditions).Select(c => c.Parameter).Distinct().ToList();
            var reasons = ComplexReasons(real, behaviours, conditionParams);
            if (reasons.Count > 0)
                return new ClassificationResult { Kind = LayerPatternKind.Complex, Summary = "複雑な構造: " + string.Join("、", reasons), TargetSummary = targetSummary };

            // float連続: 単一状態の Motion Time / float 1個の Simple1D BlendTree
            if (real.Count == 1)
            {
                var s = real[0];
                var param = s.MotionTimeParameter
                    ?? (s.Motion != null && s.Motion.IsBlendTree && s.Motion.BlendType == "Simple1D" ? s.Motion.BlendParameter : null);
                if (param != null && conditionParams.Count == 0)
                    return new ClassificationResult { Kind = LayerPatternKind.FloatContinuous, Summary = $"`{param}` の値で {JoinTargets(allBindings)} を連続変化", TargetSummary = targetSummary };
            }

            if (conditionParams.Count == 1)
            {
                var paramName = conditionParams[0];
                var paramInfo = model.Parameters.FirstOrDefault(p => p.Name == paramName);
                var paramType = paramInfo != null ? paramInfo.Type : null;
                if (paramType == "Bool" && real.Count == 2)
                {
                    var toggle = TryBoolToggle(model, real, paramName);
                    if (toggle != null) { toggle.TargetSummary = targetSummary; return toggle; }
                }
                if (paramType == "Int")
                {
                    var select = TryIntSelect(model, real, paramName);
                    if (select != null) { select.TargetSummary = targetSummary; return select; }
                }
            }

            return new ClassificationResult { Kind = LayerPatternKind.Complex, Summary = "複雑な構造: 既知パターンに一致しない", TargetSummary = targetSummary };
        }

        static List<string> ComplexReasons(List<StateNode> real, List<BehaviourInfo> behaviours, List<string> conditionParams)
        {
            var reasons = new List<string>();
            if (real.Any(s => s.Scope != "")) reasons.Add("sub-state machine");
            if (real.Any(s => s.Motion != null && s.Motion.IsBlendTree && s.Motion.BlendType == "Direct")) reasons.Add("Direct BlendTree");
            if (behaviours.Count > 0) reasons.Add(string.Join("/", behaviours.Select(b => b.Type).Distinct()));
            if (conditionParams.Count > 1) reasons.Add("複数パラメーター");
            return reasons;
        }

        static ClassificationResult TryBoolToggle(AnimatorLayerModel model, List<StateNode> real, string param)
        {
            // 各状態の「滞在時のパラメーター値」を入出エッジから投票で決める。
            // 入エッジ If→true / IfNot→false、出エッジは逆（If で離れる = 滞在は false）
            var rest = new Dictionary<int, bool?>();
            foreach (var s in real) rest[s.Id] = null;
            foreach (var t in model.Transitions)
            {
                var cond = t.Conditions.FirstOrDefault(c => c.Parameter == param);
                if (cond == null) continue;
                bool? value = cond.Mode == "If" ? true : cond.Mode == "IfNot" ? (bool?)false : null;
                if (value == null) return null; // bool に Greater 等は想定外 → 複雑扱い
                if (rest.ContainsKey(t.ToId) && !Vote(rest, t.ToId, value.Value)) return null;
                if (rest.ContainsKey(t.FromId) && !Vote(rest, t.FromId, !value.Value)) return null;
            }
            var a = real[0];
            var b = real[1];
            if (rest[a.Id] == null || rest[b.Id] == null || rest[a.Id] == rest[b.Id]) return null;
            var onState = rest[a.Id] == true ? a : b;
            var offState = rest[a.Id] == true ? b : a;
            var diff = DiffBindings(onState, offState);
            var targets = diff.Count > 0 ? JoinTargets(diff) : JoinTargets(onState.Bindings.Concat(offState.Bindings).ToList());
            return new ClassificationResult
            {
                Kind = LayerPatternKind.BoolToggle,
                Summary = $"`{param}` で {targets} をON/OFF（ON時: `{onState.Name}`）",
            };
        }

        /// <summary>投票: 未決なら採用して true、決定済みなら一致するときのみ true</summary>
        static bool Vote(Dictionary<int, bool?> rest, int id, bool value)
        {
            if (rest[id] == null) { rest[id] = value; return true; }
            return rest[id] == value;
        }

        static ClassificationResult TryIntSelect(AnimatorLayerModel model, List<StateNode> real, string param)
        {
            // 各状態の対応値: 入エッジの Equals しきい値。Equals/NotEqual 以外が混ざれば対象外
            if (model.Transitions.SelectMany(t => t.Conditions).Any(c => c.Mode != "Equals" && c.Mode != "NotEqual")) return null;
            var values = new Dictionary<int, float>();
            foreach (var t in model.Transitions)
            {
                var cond = t.Conditions.FirstOrDefault(c => c.Parameter == param && c.Mode == "Equals");
                if (cond == null) continue;
                if (!real.Any(s => s.Id == t.ToId)) continue;
                if (!values.ContainsKey(t.ToId)) values[t.ToId] = cond.Threshold;
                else if (values[t.ToId] != cond.Threshold) return null;
            }
            if (values.Count < real.Count - 1) return null; // default 状態1つを除き全状態に値が要る
            var parts = values.OrderBy(kv => kv.Value)
                .Select(kv => $"{kv.Value:0.#}=`{real.First(s => s.Id == kv.Key).Name}`");
            return new ClassificationResult
            {
                Kind = LayerPatternKind.IntSelect,
                Summary = $"`{param}` の値で切替: " + string.Join(" / ", parts),
            };
        }

        /// <summary>ON/OFF 状態間で値が異なる（または片方にしかない）バインディングを抽出</summary>
        static List<BindingInfo> DiffBindings(StateNode a, StateNode b)
        {
            var result = new List<BindingInfo>();
            var bMap = new Dictionary<(string, string), BindingInfo>();
            foreach (var x in b.Bindings) bMap[(x.Path, x.Property)] = x;
            foreach (var x in a.Bindings)
            {
                if (!bMap.TryGetValue((x.Path, x.Property), out var y) || y.ValueSummary != x.ValueSummary) result.Add(x);
            }
            var aKeys = new HashSet<(string, string)>(a.Bindings.Select(x => (x.Path, x.Property)));
            result.AddRange(b.Bindings.Where(x => !aKeys.Contains((x.Path, x.Property))));
            return result;
        }

        /// <summary>対象の代表表示: 末端名の distinct 先頭2件 + ほかN件</summary>
        static string JoinTargets(List<BindingInfo> bindings)
        {
            var names = bindings
                .Select(x => x.Path != null ? x.Path.Substring(x.Path.LastIndexOf('/') + 1) : x.Property)
                .Distinct().ToList();
            if (names.Count == 0) return "(対象なし)";
            var head = names.Take(2).Select(n => $"`{n}`");
            var rest = names.Count - 2;
            return string.Join("、", head) + (rest > 0 ? $" ほか{rest}件" : "");
        }

        static readonly (BindingCategory Category, string Label)[] CategoryLabels =
        {
            (BindingCategory.GameObjectActive, "オブジェクト"),
            (BindingCategory.BlendShape, "BlendShape "),
            (BindingCategory.MaterialProperty, "マテリアル"),
            (BindingCategory.MaterialSwap, "マテリアル差し替え"),
            (BindingCategory.Transform, "Transform "),
            (BindingCategory.Humanoid, "Humanoid "),
            (BindingCategory.Other, "その他"),
        };

        public static string BuildTargetSummary(IEnumerable<BindingInfo> bindings)
        {
            var list = bindings.ToList();
            var parts = new List<string>();
            foreach (var (category, label) in CategoryLabels)
            {
                var count = list.Where(b => b.Category == category).Select(b => (b.Path, b.Property)).Distinct().Count();
                if (count > 0) parts.Add($"{label}{count}件");
            }
            return parts.Count == 0 ? "なし" : string.Join("、", parts);
        }
    }
}
