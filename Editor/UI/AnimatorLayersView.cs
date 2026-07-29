using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using VRC.SDK3.Avatars.Components;
using VRC.SDK3.Avatars.ScriptableObjects;

namespace Narazaka.VRChat.CostumeDashboard.Editor
{
    /// <summary>Animator layer の作用説明ビュー。既定は登録衣装配下の MA Merge Animator のみ解析、
    /// [アバターも解析] で Descriptor playable layers + 衣装外 Merge Animator を追加</summary>
    public class AnimatorLayersView : VisualElement
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

        class Row
        {
            public SourceEntry Source;
            public LayerEntry Layer; // null = ソース行
        }

        public bool AnalyzeAvatar;
        public bool FilterCostumeOnly;
        /// <summary>AnalyzeAvatar / FilterCostumeOnly 変更時。ウィンドウ側で SerializeField へ永続化する</summary>
        public Action StateChanged;

        readonly MultiColumnTreeView tree;
        readonly TextField detail;
        readonly Label statusLabel;
        readonly Button generateButton;
        List<GameObject> costumeRoots = new List<GameObject>();
        List<SourceEntry> entries = new List<SourceEntry>();
        readonly Dictionary<string, string> cache;
        bool generating;

        public AnimatorLayersView()
        {
            cache = LayerExplainer.LoadCache(LayerExplainer.DefaultCachePath);

            var toolbar = new VisualElement { style = { flexDirection = FlexDirection.Row, flexShrink = 0 } };
            var analyzeAvatarToggle = new Toggle("アバターも解析");
            analyzeAvatarToggle.RegisterValueChangedCallback(e => { AnalyzeAvatar = e.newValue; if (StateChanged != null) StateChanged(); Refresh(costumeRoots); });
            toolbar.Add(analyzeAvatarToggle);
            var filterToggle = new Toggle("登録衣装に作用のみ");
            filterToggle.RegisterValueChangedCallback(e => { FilterCostumeOnly = e.newValue; if (StateChanged != null) StateChanged(); Refresh(costumeRoots); });
            toolbar.Add(filterToggle);
            generateButton = new Button(GenerateAll) { text = "未生成を一括生成" };
            toolbar.Add(generateButton);
            toolbar.Add(new Button(LlmSettingsWindow.Open) { text = "LLM設定" });
            statusLabel = new Label("") { style = { unityTextAlign = TextAnchor.MiddleLeft } };
            toolbar.Add(statusLabel);
            Add(toolbar);
            // ウィンドウ側が SerializeField から復元した値をトグルUIへ反映する
            RegisterCallback<AttachToPanelEvent>(_ =>
            {
                analyzeAvatarToggle.SetValueWithoutNotify(AnalyzeAvatar);
                filterToggle.SetValueWithoutNotify(FilterCostumeOnly);
            });

            tree = BuildTreeView();
            tree.style.flexGrow = 1;
            Add(tree);

            // TextField 単体はスクロールしないため ScrollView でラップする
            var detailScroll = new ScrollView { style = { flexShrink = 0, height = 140 } };
            detail = new TextField { multiline = true, isReadOnly = true };
            detail.style.whiteSpace = WhiteSpace.Normal;
            detailScroll.Add(detail);
            Add(detailScroll);
        }

        /// <summary>純ロジック部（UI非依存・テスト対象）: 衣装群からソース列挙 → モデル化 → 分類・注記</summary>
        public static List<SourceEntry> BuildEntries(List<GameObject> costumeRoots, bool analyzeAvatar)
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

        public void Refresh(List<GameObject> roots)
        {
            costumeRoots = roots.ToList();
            entries = BuildEntries(costumeRoots, AnalyzeAvatar);
            var items = new List<TreeViewItemData<Row>>();
            var id = 0;
            foreach (var source in entries)
            {
                var children = new List<TreeViewItemData<Row>>();
                foreach (var layer in source.Layers)
                {
                    if (FilterCostumeOnly && layer.Model.AffectedCostumes.Count == 0) continue;
                    children.Add(new TreeViewItemData<Row>(id++, new Row { Source = source, Layer = layer }));
                }
                if (FilterCostumeOnly && children.Count == 0 && source.Source.Warning == null) continue;
                items.Add(new TreeViewItemData<Row>(id++, new Row { Source = source }, children));
            }
            tree.SetRootItems(items);
            tree.Rebuild();
            tree.ExpandAll();
        }

        MultiColumnTreeView BuildTreeView()
        {
            var columns = new Columns();
            columns.Add(MakeColumn("layer", "レイヤー", 200, row =>
                row.Layer != null ? row.Layer.Model.LayerName : SourceTitle(row.Source)));
            columns.Add(MakeColumn("kind", "分類", 70, row =>
                row.Layer != null ? KindLabel(row.Layer.Classification.Kind) : (row.Source.Source.Warning ?? "")));
            columns.Add(MakeColumn("trigger", "トリガー", 120, row =>
                row.Layer != null ? string.Join(", ", row.Layer.Model.Parameters.Select(p => p.Name)) : "",
                row => row.Layer != null ? row.Layer.TriggerTooltip : null));
            columns.Add(MakeColumn("target", "作用先", 150, row =>
                row.Layer != null ? row.Layer.Classification.TargetSummary : ""));
            columns.Add(MakeColumn("costume", "衣装", 90, row =>
                row.Layer != null ? string.Join(", ", row.Layer.Model.AffectedCostumes) : ""));
            columns.Add(MakeColumn("summary", "説明", 280, row =>
                row.Layer != null ? FirstLine(ExplanationText(row.Layer)) : ""));
            columns.Add(new Column
            {
                name = "ops",
                title = "操作",
                width = 110,
                makeCell = () =>
                {
                    var container = new VisualElement { style = { flexDirection = FlexDirection.Row } };
                    container.Add(new Button { text = "生成" });
                    container.Add(new Button { text = "JSON" });
                    return container;
                },
                bindCell = (cell, index) =>
                {
                    var row = tree.GetItemDataForIndex<Row>(index);
                    var generate = (Button)cell[0];
                    var copy = (Button)cell[1];
                    var isLayer = row.Layer != null;
                    generate.style.display = isLayer && row.Layer.Classification.Kind == LayerPatternKind.Complex ? DisplayStyle.Flex : DisplayStyle.None;
                    copy.style.display = isLayer ? DisplayStyle.Flex : DisplayStyle.None;
                    if (!isLayer) return;
                    var layer = row.Layer;
                    generate.clickable = new Clickable(() => GenerateOne(layer));
                    generate.SetEnabled(!generating);
                    copy.clickable = new Clickable(() => { EditorGUIUtility.systemCopyBuffer = LayerModelSerializer.ToJson(layer.Model, true); });
                },
            });
            var view = new MultiColumnTreeView(columns) { selectionType = SelectionType.Single };
            view.selectionChanged += _ => UpdateDetail();
            return view;
        }

        Column MakeColumn(string name, string title, float width, Func<Row, string> text, Func<Row, string> tooltip = null)
        {
            return new Column
            {
                name = name,
                title = title,
                width = width,
                makeCell = () => new Label { style = { unityTextOverflowPosition = TextOverflowPosition.End, overflow = Overflow.Hidden } },
                bindCell = (cell, index) =>
                {
                    var row = tree.GetItemDataForIndex<Row>(index);
                    var label = (Label)cell;
                    label.text = text(row) ?? "";
                    var tip = tooltip != null ? tooltip(row) : null;
                    label.tooltip = string.IsNullOrEmpty(tip) ? label.text : tip;
                },
            };
        }

        static string SourceTitle(SourceEntry source)
        {
            var info = source.Source.Info;
            return info.Kind == AnimatorSourceKind.PlayableLayer
                ? $"{info.LayerType} (Descriptor) {info.ControllerName}"
                : $"Merge Animator ({info.ComponentPath}) [{info.LayerType}] {info.ControllerName}";
        }

        static string KindLabel(LayerPatternKind kind)
        {
            switch (kind)
            {
                case LayerPatternKind.BoolToggle: return "トグル";
                case LayerPatternKind.IntSelect: return "排他選択";
                case LayerPatternKind.FloatContinuous: return "連続";
                case LayerPatternKind.Complex: return "複雑";
                default: return "空";
            }
        }

        string ExplanationText(LayerEntry layer)
        {
            if (layer.Classification.Kind != LayerPatternKind.Complex) return layer.Classification.Summary;
            var settings = LlmSettings.Load();
            if (cache.TryGetValue(LayerExplainer.CacheKey(layer.Compact, settings.CacheKeyModel), out var text)) return text;
            return layer.Classification.Summary + "（LLM説明は未生成）";
        }

        static string FirstLine(string text)
        {
            if (string.IsNullOrEmpty(text)) return "";
            var i = text.IndexOf('\n');
            return i < 0 ? text : text.Substring(0, i);
        }

        void UpdateDetail()
        {
            var row = tree.selectedItem as Row;
            if (row == null || row.Layer == null) { detail.value = ""; return; }
            var layer = row.Layer;
            var bindings = layer.Model.States.SelectMany(s => s.Bindings)
                .Select(b => $"  {(b.Path ?? "(Humanoid)")} {b.Property} = {b.ValueSummary}")
                .Distinct().Take(50).ToList();
            detail.value =
                $"{layer.Id}\n" +
                $"分類: {KindLabel(layer.Classification.Kind)} / 作用先: {layer.Classification.TargetSummary}\n" +
                (string.IsNullOrEmpty(layer.TriggerTooltip) ? "" : $"トリガー:\n{layer.TriggerTooltip}\n") +
                $"説明: {ExplanationText(layer)}\n" +
                $"バインディング:\n{string.Join("\n", bindings)}";
        }

        List<LayerEntry> UncachedComplexLayers(string model)
        {
            return entries.SelectMany(e => e.Layers)
                .Where(l => l.Classification.Kind == LayerPatternKind.Complex)
                .Where(l => !cache.ContainsKey(LayerExplainer.CacheKey(l.Compact, model)))
                .ToList();
        }

        static bool EnsureSettings(LlmSettings settings)
        {
            if (settings.IsReady) return true;
            EditorUtility.DisplayDialog("LLM設定が必要",
                settings.Provider == LlmProvider.Cli
                    ? "LLM設定（CLIコマンド）を設定してください。"
                    : "LLM設定（APIキー・モデル名）を設定してください。", "OK");
            return false;
        }

        void GenerateAll()
        {
            var settings = LlmSettings.Load();
            if (!EnsureSettings(settings)) return;
            var targets = UncachedComplexLayers(settings.CacheKeyModel);
            if (targets.Count == 0)
            {
                EditorUtility.DisplayDialog("一括生成", "未生成の複雑layerはありません。", "OK");
                return;
            }
            var payload = targets.Select(t => (t.Id, t.Compact)).ToList();
            var chunks = LayerExplainer.Chunk(payload, settings.MaxInputChars);
            var totalChars = payload.Sum(p => p.Compact.Length);
            var chunkNote = chunks.Count > 1
                ? $"\n（合計が入力上限 {settings.MaxInputChars:N0} 文字を超えるため {chunks.Count} 分割。上限はLLM設定で変更可）"
                : "";
            if (!EditorUtility.DisplayDialog("一括生成",
                $"複雑layer {targets.Count} 件を {chunks.Count} リクエスト（合計約 {totalChars:N0} 文字）で {settings.DisplayTarget} に送信します。実行しますか？{chunkNote}",
                "実行", "キャンセル")) return;
            Generate(settings, targets, payload);
        }

        void GenerateOne(LayerEntry layer)
        {
            var settings = LlmSettings.Load();
            if (!EnsureSettings(settings)) return;
            // 個別ボタンは強制再生成を兼ねる（キャッシュ有無を見ない）
            Generate(settings, new List<LayerEntry> { layer }, new List<(string, string)> { (layer.Id, layer.Compact) });
        }

        void Generate(LlmSettings settings, List<LayerEntry> targets, List<(string Id, string Json)> payload)
        {
            generating = true;
            generateButton.SetEnabled(false);
            var compactById = targets.ToDictionary(t => t.Id, t => t.Compact);
            LayerExplainer.GenerateAsync(settings, payload,
                (index, total) => { statusLabel.text = $"生成中… ({index + 1}/{total})"; },
                (results, error) =>
                {
                    generating = false;
                    generateButton.SetEnabled(true);
                    foreach (var kv in results)
                    {
                        // 応答の id からキャッシュキー（内容ハッシュ）へ引き直して保存
                        if (compactById.TryGetValue(kv.Key, out var compact)) cache[LayerExplainer.CacheKey(compact, settings.CacheKeyModel)] = kv.Value;
                    }
                    LayerExplainer.SaveCache(LayerExplainer.DefaultCachePath, cache);
                    statusLabel.text = error != null ? "エラー: " + FirstLine(error) : $"{results.Count} 件生成完了";
                    if (error != null) Debug.LogError("[CostumeDashboard] LLM生成エラー: " + error);
                    tree.RefreshItems();
                    UpdateDetail();
                });
        }
    }
}
