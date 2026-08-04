using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace Narazaka.VRChat.CostumeDashboard.Editor
{
    /// <summary>Animator layer の作用説明ビュー。既定は登録衣装配下の MA Merge Animator のみ解析、
    /// [アバターも解析] で Descriptor playable layers + 衣装外 Merge Animator を追加</summary>
    public class AnimatorLayersView : VisualElement
    {
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
            // label ではなく text: チェックボックスの直後にラベルが並ぶ (ToggleLeft 相当)
            var analyzeAvatarToggle = new Toggle { text = "アバターも解析" };
            analyzeAvatarToggle.RegisterValueChangedCallback(e => { AnalyzeAvatar = e.newValue; if (StateChanged != null) StateChanged(); Refresh(costumeRoots); });
            toolbar.Add(analyzeAvatarToggle);
            var filterToggle = new Toggle { text = "登録衣装に作用のみ" };
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

        public void Refresh(List<GameObject> roots)
        {
            costumeRoots = roots.ToList();
            entries = AnimatorEntryBuilder.Build(costumeRoots, AnalyzeAvatar);
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
                width = 160,
                makeCell = () =>
                {
                    var container = new VisualElement { style = { flexDirection = FlexDirection.Row } };
                    container.Add(new Button { text = "生成" });
                    container.Add(new Button { text = "JSON", tooltip = "layer構造のJSONをコピー" });
                    container.Add(new Button { text = "TXT", tooltip = "layer構造のコンパクト表記（LLM入力と同形式）をコピー" });
                    return container;
                },
                bindCell = (cell, index) =>
                {
                    var row = tree.GetItemDataForIndex<Row>(index);
                    var generate = (Button)cell[0];
                    var copyJson = (Button)cell[1];
                    var copyCompact = (Button)cell[2];
                    var isLayer = row.Layer != null;
                    copyJson.style.display = isLayer ? DisplayStyle.Flex : DisplayStyle.None;
                    copyCompact.style.display = isLayer ? DisplayStyle.Flex : DisplayStyle.None;
                    if (isLayer)
                    {
                        var layer = row.Layer;
                        generate.style.display = layer.Classification.Kind == LayerPatternKind.Complex ? DisplayStyle.Flex : DisplayStyle.None;
                        generate.tooltip = "このlayerのLLM説明を（再）生成";
                        generate.clickable = new Clickable(() => GenerateOne(layer));
                        copyJson.clickable = new Clickable(() => { EditorGUIUtility.systemCopyBuffer = LayerModelSerializer.ToJson(layer.Model, true); });
                        copyCompact.clickable = new Clickable(() => { EditorGUIUtility.systemCopyBuffer = layer.Compact; });
                    }
                    else
                    {
                        // ソース行: そのAnimator内の複雑layerだけ一括生成（説明が要るのは大抵FX等の一部のみのため）
                        var source = row.Source;
                        generate.style.display = source.Layers.Any(l => l.Classification.Kind == LayerPatternKind.Complex) ? DisplayStyle.Flex : DisplayStyle.None;
                        generate.tooltip = "このAnimator内の未生成の複雑layerを一括生成";
                        generate.clickable = new Clickable(() => GenerateSource(source));
                    }
                    generate.SetEnabled(!generating);
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

        List<LayerEntry> UncachedComplex(IEnumerable<LayerEntry> layers, string model)
        {
            return layers
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
            var targets = UncachedComplex(entries.SelectMany(e => e.Layers), settings.CacheKeyModel);
            if (targets.Count == 0)
            {
                EditorUtility.DisplayDialog("一括生成", "未生成の複雑layerはありません。", "OK");
                return;
            }
            ConfirmAndGenerate(settings, targets);
        }

        /// <summary>ソース（Animator）単位の一括生成。説明が要るのは大抵FX等の一部Animatorのみのための導線</summary>
        void GenerateSource(SourceEntry source)
        {
            var settings = LlmSettings.Load();
            if (!EnsureSettings(settings)) return;
            var targets = UncachedComplex(source.Layers, settings.CacheKeyModel);
            if (targets.Count == 0)
            {
                EditorUtility.DisplayDialog("一括生成", "このAnimatorに未生成の複雑layerはありません。", "OK");
                return;
            }
            ConfirmAndGenerate(settings, targets);
        }

        void ConfirmAndGenerate(LlmSettings settings, List<LayerEntry> targets)
        {
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
