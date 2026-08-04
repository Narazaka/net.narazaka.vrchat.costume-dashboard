using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using VRC.SDK3.Avatars.Components;
using VRC.SDK3.Avatars.ScriptableObjects;

namespace Narazaka.VRChat.CostumeDashboard.Editor
{
    public class SourceEntry
    {
        public GameObject AvatarRoot;
        public AnimatorSource Source;
        public List<LayerEntry> Layers = new List<LayerEntry>();
    }

    public class LayerEntry
    {
        public AnimatorLayerModel Model;
        public ClassificationResult Classification;
        public string Id;
        public string Json;
        /// <summary>LLM入力用のコンパクト形式（キャッシュキーもこちらから採る）</summary>
        public string Compact;
        /// <summary>Expression Menu の該当コントロール名を含むトリガー詳細（ベストエフォート）</summary>
        public string TriggerTooltip;
    }

    /// <summary>純ロジック部（UI非依存・テスト対象）: 衣装群からソース列挙 → モデル化 → 分類・注記</summary>
    public static class AnimatorEntryBuilder
    {
        /// <summary>costumeRoots を <see cref="AvatarUtil.FindAvatarRoot"/> でグループ化し、avatarRoot ごとに
        /// <see cref="Build(GameObject, List{GameObject}, bool)"/> へ委譲する（挙動は変更しない）。</summary>
        public static List<SourceEntry> Build(List<GameObject> costumeRoots, bool analyzeAvatar)
        {
            var result = new List<SourceEntry>();
            var byAvatar = costumeRoots
                .Where(c => c != null)
                .Select(c => (Costume: c, Avatar: AvatarUtil.FindAvatarRoot(c)))
                .Where(x => x.Avatar != null)
                .GroupBy(x => x.Avatar);
            foreach (var group in byAvatar)
            {
                var avatarRoot = group.Key;
                var costumes = group.Select(x => x.Costume).ToList();
                result.AddRange(Build(avatarRoot, costumes, analyzeAvatar));
            }
            return result;
        }

        /// <summary>avatarRoot を外から受け取る版。<see cref="VRCAvatarDescriptor"/> の無い costumeRoots
        /// （= <see cref="AvatarUtil.FindAvatarRoot"/> でグループ化できないもの）を解析する場合に使う。
        /// avatarRoot に Descriptor が無くても壊れない（Descriptor 依存箇所は expressionParameters/expressionsMenu
        /// が null 扱いになるだけで、Merge Animator 側の解析は avatarRoot 基準の相対パスで完結するため）。</summary>
        public static List<SourceEntry> Build(GameObject avatarRoot, List<GameObject> costumeRoots, bool analyzeAvatar)
        {
            var result = new List<SourceEntry>();
            var costumes = costumeRoots.Where(c => c != null).ToList();
            var sources = AnimatorSourceCollector.CollectCostumeSources(avatarRoot, costumes);
            if (analyzeAvatar) sources.AddRange(AnimatorSourceCollector.CollectAvatarSources(avatarRoot, costumes));
            var descriptor = avatarRoot.GetComponent<VRCAvatarDescriptor>();
            var parameters = descriptor != null ? descriptor.expressionParameters : null;
            var menuIndex = BuildMenuNameIndex(descriptor);
            foreach (var source in sources)
            {
                var entry = new SourceEntry { AvatarRoot = avatarRoot, Source = source };
                foreach (var model in AnimatorSourceCollector.BuildModels(source))
                {
                    AnimatorSourceCollector.AnnotateExpressionParameters(model, parameters);
                    AnimatorSourceCollector.AnnotateAffectedCostumes(model, avatarRoot, costumes);
                    entry.Layers.Add(new LayerEntry
                    {
                        Model = model,
                        Classification = LayerPatternClassifier.Classify(model),
                        Id = LayerModelSerializer.LayerId(model),
                        Json = LayerModelSerializer.ToJson(model, false),
                        Compact = LayerModelSerializer.ToCompact(model),
                        TriggerTooltip = BuildTriggerTooltip(model, menuIndex),
                    });
                }
                result.Add(entry);
            }
            return result;
        }

        /// <summary>Expression Menu を再帰走査して パラメーター名 → コントロール名 を引く
        /// （ベストエフォート。MA Menu Installer の合成は追わない）</summary>
        static Dictionary<string, string> BuildMenuNameIndex(VRCAvatarDescriptor descriptor)
        {
            var result = new Dictionary<string, string>();
            if (descriptor == null || descriptor.expressionsMenu == null) return result;
            var visited = new HashSet<VRCExpressionsMenu>();
            void Walk(VRCExpressionsMenu menu)
            {
                if (menu == null || !visited.Add(menu)) return;
                if (menu.controls == null) return;
                foreach (var control in menu.controls)
                {
                    if (control == null) continue;
                    if (control.parameter != null && !string.IsNullOrEmpty(control.parameter.name) && !result.ContainsKey(control.parameter.name))
                        result[control.parameter.name] = control.name;
                    if (control.subParameters != null)
                    {
                        foreach (var sub in control.subParameters)
                        {
                            if (sub != null && !string.IsNullOrEmpty(sub.name) && !result.ContainsKey(sub.name)) result[sub.name] = control.name;
                        }
                    }
                    if (control.type == VRCExpressionsMenu.Control.ControlType.SubMenu) Walk(control.subMenu);
                }
            }
            Walk(descriptor.expressionsMenu);
            return result;
        }

        static string BuildTriggerTooltip(AnimatorLayerModel model, Dictionary<string, string> menuIndex)
        {
            var lines = model.Parameters.Select(p =>
            {
                var ep = p.InExpressionParameters ? $" [EP{(p.Synced ? " synced" : "")}{(p.Saved ? " saved" : "")}]" : "";
                var menu = menuIndex.TryGetValue(p.Name, out var name) ? $" メニュー: {name}" : "";
                return $"{p.Name} ({p.Type}){ep}{menu}";
            }).ToList();
            return lines.Count == 0 ? "" : string.Join("\n", lines);
        }
    }
}
