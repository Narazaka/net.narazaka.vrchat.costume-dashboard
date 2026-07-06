using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using UnityEditor.UIElements;
using nadena.dev.modular_avatar.core;
using net.narazaka.avatarmenucreator.components;
using CRQ = Narazaka.VRChat.ChangeRenderQueue.ChangeRenderQueue;

namespace Narazaka.VRChat.CostumeDashboard.Editor
{
    public class CostumeDashboardWindow : EditorWindow
    {
        [SerializeField] List<GameObject> costumeRoots = new List<GameObject>();
        [SerializeField] DashboardViewMode viewMode = DashboardViewMode.Mesh;

        MultiColumnTreeView tree;
        VisualElement costumeListContainer;
        VisualElement baseMeshContainer;

        readonly HashSet<int> checkedMeshes = new HashSet<int>();

        /// <summary>Renderer instanceID -> ユーザーが明示選択したフェード枠。エントリなし = 推奨枠に従う</summary>
        readonly Dictionary<int, FadeFrame> frameOverrides = new Dictionary<int, FadeFrame>();

        /// <summary>アバタールート instanceID -> ユーザーが明示選択した素体。エントリなし = 既定素体（BlendShape数最大）に従う</summary>
        readonly Dictionary<int, SkinnedMeshRenderer> baseMeshOverrides = new Dictionary<int, SkinnedMeshRenderer>();

        /// <summary>Refresh 毎に構築する AO ME ホスト設定済みキャッシュ（グループ参照をキーにする。bind ごとの Find/HasComponent 再計算を避ける）</summary>
        readonly Dictionary<SlotGroup, bool> aomeConfiguredCache = new Dictionary<SlotGroup, bool>();

        /// <summary>メッシュビューでスロット行からグループを逆引きするための Refresh 毎キャッシュ（AO ME 列用）</summary>
        readonly Dictionary<SlotInfo, SlotGroup> meshViewSlotGroups = new Dictionary<SlotInfo, SlotGroup>();

        /// <summary>Refresh 毎に構築するアバタールートごとの Toggle Menu 対象キャッシュ（アバター全体走査を bind ごとに行わないため）</summary>
        readonly Dictionary<int, List<(AvatarToggleMenuCreator Creator, HashSet<string> TargetPaths)>> toggleMenuTargetsCache = new Dictionary<int, List<(AvatarToggleMenuCreator, HashSet<string>)>>();

        static readonly Color ConfiguredColor = new Color(0.18f, 0.42f, 0.2f);

        static readonly StyleColor MeshRowTint = new StyleColor(new Color(0.25f, 0.30f, 0.38f, 0.35f));
        static readonly StyleColor GroupRowTint = new StyleColor(new Color(0.32f, 0.27f, 0.38f, 0.35f));

        /// <summary>行種別の背景 tint を全列 bindCell 冒頭で無条件に適用する（recycle されたセルに前行の tint を残さない）。
        /// 警告色等のセル個別スタイルはこの後で上書きする（NoColor へ戻す形のリセットは tint を消すため行わない）</summary>
        static void ApplyRowTint(VisualElement cell, Row row)
        {
            switch (row.Kind)
            {
                case RowKind.Mesh: cell.style.backgroundColor = MeshRowTint; break;
                case RowKind.Group: cell.style.backgroundColor = GroupRowTint; break;
                default: cell.style.backgroundColor = new StyleColor(StyleKeyword.Null); break;
            }
        }

        static readonly List<string> FrameChoices = new List<string> { "推奨", "main", "alpha", "3rd", "2nd" };

        /// <summary>表示: メッシュ（既定、衣装 > メッシュ > スロット） / AO ME（衣装 > グループ > メッシュ > スロット）</summary>
        internal enum DashboardViewMode { Mesh, Group }

        static readonly List<string> ViewModeChoices = new List<string> { "メッシュ", "AO ME" };

        internal enum RowKind { Costume, Group, Mesh, Slot }

        internal class Row
        {
            public RowKind Kind;
            public GameObject Costume;
            public GameObject AvatarRoot;
            public SlotGroup Group;
            public Renderer Renderer;
            public List<SlotInfo> MeshSlots;
            public SlotInfo Slot;
            /// <summary>メッシュビューの衣装行のみ: [AO ME一括] 用にツリー構築時に前計算したグループ一覧</summary>
            public List<SlotGroup> CostumeGroups;
        }

        [MenuItem("Tools/Costume Dashboard")]
        public static void Open()
        {
            GetWindow<CostumeDashboardWindow>("Costume Dashboard");
        }

        void CreateGUI()
        {
            var root = rootVisualElement;

            var toolbar = new VisualElement { style = { flexDirection = FlexDirection.Row, flexShrink = 0 } };
            var viewModePopup = new PopupField<string>("表示", ViewModeChoices, (int)viewMode);
            viewModePopup.RegisterValueChangedCallback(e =>
            {
                viewMode = (DashboardViewMode)ViewModeChoices.IndexOf(e.newValue);
                Refresh();
            });
            toolbar.Add(viewModePopup);
            toolbar.Add(new Button(AddSelectedCostumes) { text = "選択から衣装を追加" });
            toolbar.Add(new Button(Refresh) { text = "更新" });
            toolbar.Add(new Button(CreateToggleMenu) { text = "✓ から Toggle Menu作成" });
            toolbar.Add(new Button(BSSyncChecked) { text = "✓ から BS Sync" });
            root.Add(toolbar);

            costumeListContainer = new VisualElement { style = { flexShrink = 0 } };
            root.Add(costumeListContainer);

            baseMeshContainer = new VisualElement { style = { flexShrink = 0 } };
            root.Add(baseMeshContainer);

            tree = BuildTree();
            tree.style.flexGrow = 1;
            root.Add(tree);

            Refresh();
        }

        void OnFocus()
        {
            if (tree != null) Refresh();
        }

        void AddSelectedCostumes()
        {
            foreach (var go in Selection.gameObjects)
            {
                if (go != null && !costumeRoots.Contains(go)) costumeRoots.Add(go);
            }
            Refresh();
        }

        void Refresh()
        {
            // AO ME列/Toggle✓ 判定用キャッシュは Refresh 単位で作り直す（bind ごとの再計算・全体走査を避けるため）
            aomeConfiguredCache.Clear();
            meshViewSlotGroups.Clear();
            RebuildToggleMenuTargetsCache();
            RebuildCostumeList();
            RebuildBaseMeshList();
            tree.SetRootItems(BuildTreeItems());
            tree.Rebuild();
        }

        /// <summary>登録済み衣装の属するアバタールートごとに、全 AvatarToggleMenuCreator と対象パス集合を1回だけ収集する</summary>
        void RebuildToggleMenuTargetsCache()
        {
            toggleMenuTargetsCache.Clear();
            var avatarRoots = costumeRoots
                .Where(c => c != null)
                .Select(AvatarUtil.FindAvatarRoot)
                .Where(a => a != null)
                .Distinct();
            foreach (var avatarRoot in avatarRoots)
            {
                toggleMenuTargetsCache[avatarRoot.GetInstanceID()] = ToggleMenuSetup.CollectMenuTargets(avatarRoot);
            }
        }

        /// <summary>グループごとの AO ME ホスト設定済み状態を1回だけ計算してキャッシュする（グループ数ぶんの Find のみ）</summary>
        void CacheAOMEConfigured(GameObject costume, IEnumerable<SlotGroup> groups)
        {
            foreach (var group in groups)
            {
                aomeConfiguredCache[group] = AOMaterialEditorSetup.HasComponent(FindAOMEHost(costume, group));
            }
        }

        /// <summary>Toggle Menu キャッシュから renderer を対象とする AvatarToggleMenuCreator 一覧を引く（bind ごとの全体走査を避ける）</summary>
        List<AvatarToggleMenuCreator> FindCachedToggleMenus(GameObject avatarRoot, Renderer renderer)
        {
            var result = new List<AvatarToggleMenuCreator>();
            if (avatarRoot == null || renderer == null) return result;
            if (!toggleMenuTargetsCache.TryGetValue(avatarRoot.GetInstanceID(), out var entries)) return result;
            var meshPath = AvatarUtil.RelativePath(avatarRoot, renderer.gameObject);
            if (string.IsNullOrEmpty(meshPath)) return result;
            foreach (var (creator, targetPaths) in entries)
            {
                if (targetPaths.Contains(meshPath)) result.Add(creator);
            }
            return result;
        }

        /// <summary>スロット行の属するグループ。グループビューは row.Group、メッシュビューは meshViewSlotGroups での逆引き</summary>
        SlotGroup RowGroup(Row row) => row.Group ?? (row.Slot != null && meshViewSlotGroups.TryGetValue(row.Slot, out var g) ? g : null);

        void RebuildCostumeList()
        {
            costumeListContainer.Clear();
            for (var i = 0; i < costumeRoots.Count; i++)
            {
                var index = i;
                var line = new VisualElement { style = { flexDirection = FlexDirection.Row } };
                var field = new ObjectField { objectType = typeof(GameObject), value = costumeRoots[index], allowSceneObjects = true };
                field.style.flexGrow = 1;
                field.RegisterValueChangedCallback(e =>
                {
                    costumeRoots[index] = e.newValue as GameObject;
                    Refresh();
                });
                line.Add(field);
                line.Add(new Button(() => { costumeRoots.RemoveAt(index); Refresh(); }) { text = "×" });
                costumeListContainer.Add(line);
            }
            var addLine = new VisualElement { style = { flexDirection = FlexDirection.Row } };
            var addField = new ObjectField("衣装を追加") { objectType = typeof(GameObject), allowSceneObjects = true };
            addField.style.flexGrow = 1;
            addField.RegisterValueChangedCallback(e =>
            {
                var go = e.newValue as GameObject;
                if (go != null && !costumeRoots.Contains(go))
                {
                    costumeRoots.Add(go);
                    Refresh();
                }
            });
            addLine.Add(addField);
            costumeListContainer.Add(addLine);
        }

        void RebuildBaseMeshList()
        {
            baseMeshContainer.Clear();
            var avatarRoots = costumeRoots
                .Where(c => c != null)
                .Select(AvatarUtil.FindAvatarRoot)
                .Where(a => a != null)
                .Distinct()
                .ToList();
            foreach (var avatarRoot in avatarRoots)
            {
                var line = new VisualElement { style = { flexDirection = FlexDirection.Row } };
                line.Add(new Label($"素体({avatarRoot.name}):") { style = { width = 160 } });
                var field = new ObjectField { objectType = typeof(SkinnedMeshRenderer), allowSceneObjects = true };
                field.style.flexGrow = 1;
                field.SetValueWithoutNotify(EffectiveBaseMesh(avatarRoot));
                field.RegisterValueChangedCallback(e =>
                {
                    var smr = e.newValue as SkinnedMeshRenderer;
                    if (smr != null && AvatarUtil.FindAvatarRoot(smr.gameObject) != avatarRoot)
                    {
                        EditorUtility.DisplayDialog("Costume Dashboard", "選択したメッシュは対象アバター配下ではありません", "OK");
                        field.SetValueWithoutNotify(EffectiveBaseMesh(avatarRoot));
                        return;
                    }
                    if (smr == null) baseMeshOverrides.Remove(avatarRoot.GetInstanceID());
                    else baseMeshOverrides[avatarRoot.GetInstanceID()] = smr;
                    Refresh();
                });
                line.Add(field);
                baseMeshContainer.Add(line);
            }
        }

        /// <summary>実効素体 = baseMeshOverrides の明示選択があればそれ、なければ既定素体（BlendShape数最大）</summary>
        SkinnedMeshRenderer EffectiveBaseMesh(GameObject avatarRoot)
        {
            if (avatarRoot == null) return null;
            if (baseMeshOverrides.TryGetValue(avatarRoot.GetInstanceID(), out var smr) && smr != null) return smr;
            return BlendShapeSyncSetup.FindDefaultBaseMesh(avatarRoot);
        }

        /// <summary>実効フェード枠 = frameOverrides の明示選択があればそれ、なければ推奨枠</summary>
        FadeFrame? EffectiveFrame(SlotInfo slot) =>
            slot.Renderer != null && frameOverrides.TryGetValue(slot.Renderer.GetInstanceID(), out var f) ? f : slot.FadeCompat?.Recommended;

        List<TreeViewItemData<Row>> BuildTreeItems() =>
            viewMode == DashboardViewMode.Mesh ? BuildMeshViewItems() : BuildGroupViewItems();

        /// <summary>メッシュビュー（既定）: 衣装 > メッシュ > スロット。メッシュは衣装スキャン全体を Renderer でバケットし、遭遇順に1回だけ現れる</summary>
        List<TreeViewItemData<Row>> BuildMeshViewItems()
        {
            var items = new List<TreeViewItemData<Row>>();
            var id = 0;
            foreach (var costume in costumeRoots)
            {
                if (costume == null) continue;
                var avatarRoot = AvatarUtil.FindAvatarRoot(costume);
                var scan = MaterialSlotScanner.Scan(costume);
                // [AO ME一括] 用のグループ一覧はツリー構築時に前計算して衣装行 Row に持たせる
                // （bindCell で毎回 Scan+GroupByShader しない。MeshSlots と同じ前計算方針）
                var costumeGroups = MaterialSlotScanner.GroupByShader(scan, EffectiveFrame);
                CacheAOMEConfigured(costume, costumeGroups);
                // AO ME列でスロット行からグループを引けるよう、スロット参照をキーに逆引きキャッシュを構築する
                foreach (var group in costumeGroups)
                {
                    foreach (var slot in group.Slots)
                    {
                        meshViewSlotGroups[slot] = group;
                    }
                }

                // 衣装スキャン全体を Renderer ごとに束ねる（出現順を保持）
                var meshOrder = new List<Renderer>();
                var meshSlots = new Dictionary<Renderer, List<SlotInfo>>();
                foreach (var slot in scan)
                {
                    if (!meshSlots.TryGetValue(slot.Renderer, out var list))
                    {
                        list = new List<SlotInfo>();
                        meshSlots[slot.Renderer] = list;
                        meshOrder.Add(slot.Renderer);
                    }
                    list.Add(slot);
                }

                var meshItems = new List<TreeViewItemData<Row>>();
                foreach (var renderer in meshOrder)
                {
                    var slots = meshSlots[renderer];
                    var slotItems = slots
                        .Select(slot => new TreeViewItemData<Row>(id++, new Row { Kind = RowKind.Slot, Costume = costume, AvatarRoot = avatarRoot, Slot = slot }))
                        .ToList();
                    meshItems.Add(new TreeViewItemData<Row>(id++, new Row { Kind = RowKind.Mesh, Costume = costume, AvatarRoot = avatarRoot, Renderer = renderer, MeshSlots = slots }, slotItems));
                }
                items.Add(new TreeViewItemData<Row>(id++, new Row { Kind = RowKind.Costume, Costume = costume, AvatarRoot = avatarRoot, CostumeGroups = costumeGroups }, meshItems));
            }
            return items;
        }

        /// <summary>AO MEビュー（従来）: 衣装 > グループ（シェーダー種別・実効フェード枠） > メッシュ > スロット</summary>
        List<TreeViewItemData<Row>> BuildGroupViewItems()
        {
            var items = new List<TreeViewItemData<Row>>();
            var id = 0;
            foreach (var costume in costumeRoots)
            {
                if (costume == null) continue;
                var avatarRoot = AvatarUtil.FindAvatarRoot(costume);
                var groups = MaterialSlotScanner.GroupByShader(MaterialSlotScanner.Scan(costume), EffectiveFrame);
                CacheAOMEConfigured(costume, groups);
                var groupItems = new List<TreeViewItemData<Row>>();
                foreach (var group in groups)
                {
                    // グループ内スロットを Renderer ごとに束ねる（出現順を保持）
                    var meshOrder = new List<Renderer>();
                    var meshSlots = new Dictionary<Renderer, List<SlotInfo>>();
                    foreach (var slot in group.Slots)
                    {
                        if (!meshSlots.TryGetValue(slot.Renderer, out var list))
                        {
                            list = new List<SlotInfo>();
                            meshSlots[slot.Renderer] = list;
                            meshOrder.Add(slot.Renderer);
                        }
                        list.Add(slot);
                    }

                    var meshItems = new List<TreeViewItemData<Row>>();
                    foreach (var renderer in meshOrder)
                    {
                        var slots = meshSlots[renderer];
                        var slotItems = slots
                            .Select(slot => new TreeViewItemData<Row>(id++, new Row { Kind = RowKind.Slot, Costume = costume, AvatarRoot = avatarRoot, Group = group, Slot = slot }))
                            .ToList();
                        meshItems.Add(new TreeViewItemData<Row>(id++, new Row { Kind = RowKind.Mesh, Costume = costume, AvatarRoot = avatarRoot, Group = group, Renderer = renderer, MeshSlots = slots }, slotItems));
                    }
                    groupItems.Add(new TreeViewItemData<Row>(id++, new Row { Kind = RowKind.Group, Costume = costume, AvatarRoot = avatarRoot, Group = group }, meshItems));
                }
                items.Add(new TreeViewItemData<Row>(id++, new Row { Kind = RowKind.Costume, Costume = costume, AvatarRoot = avatarRoot }, groupItems));
            }
            return items;
        }

        MultiColumnTreeView BuildTree()
        {
            var columns = new Columns();
            columns.Add(MakeLabelColumn("object", "オブジェクト", 220, row =>
            {
                switch (row.Kind)
                {
                    case RowKind.Costume:
                        var warn = row.AvatarRoot == null ? " ⚠ アバタールートが見つかりません" : "";
                        var toggleCount = row.Costume.GetComponentsInChildren<net.narazaka.avatarmenucreator.components.AvatarToggleMenuCreator>(true).Length;
                        var toggleInfo = toggleCount > 0 ? $" [Toggle Menu: {toggleCount}]" : "";
                        return row.Costume.name + toggleInfo + warn;
                    case RowKind.Group:
                        var reason = row.Group.CanSetupFade ? "" : $" ({row.Group.FadeDisabledReason})";
                        return $"{DisplayNames.Group(row.Group)} ({row.Group.Slots.Count}){reason}";
                    case RowKind.Mesh:
                        return row.Renderer == null ? "(missing)" : row.Renderer.name;
                    default:
                        return "";
                }
            // グループ行は日本語表示名になるため、ホスト GameObject 名（ASCII suffix）との対応は tooltip で示す
            }, row => row.Kind == RowKind.Group ? AOMEHostSuffix(row.Group) : ""));
            columns.Add(new Column
            {
                name = "select",
                title = "選択",
                width = 56,
                makeCell = () => new Button { text = "Select" },
                bindCell = (element, index) =>
                {
                    var button = (Button)element;
                    var row = tree.GetItemDataForIndex<Row>(index);
                    ApplyRowTint(button, row);
                    button.style.display = row.Kind == RowKind.Mesh && row.Renderer != null ? DisplayStyle.Flex : DisplayStyle.None;
                    button.clickable = new Clickable((EventBase evt) => SelectRenderer(row, evt));
                },
            });
            columns.Add(new Column
            {
                name = "check",
                title = "✓",
                width = 28,
                makeCell = () => new Toggle(),
                bindCell = (element, index) =>
                {
                    var toggle = (Toggle)element;
                    if (toggle.userData is EventCallback<ChangeEvent<bool>> prev) toggle.UnregisterValueChangedCallback(prev);
                    var row = tree.GetItemDataForIndex<Row>(index);
                    ApplyRowTint(toggle, row);
                    if (row.Kind != RowKind.Mesh || row.Renderer == null)
                    {
                        toggle.userData = null;
                        toggle.style.display = DisplayStyle.None;
                        return;
                    }
                    toggle.style.display = DisplayStyle.Flex;
                    var meshId = row.Renderer.GetInstanceID();
                    toggle.SetValueWithoutNotify(checkedMeshes.Contains(meshId));
                    EventCallback<ChangeEvent<bool>> cb = e =>
                    {
                        if (e.newValue) checkedMeshes.Add(meshId);
                        else checkedMeshes.Remove(meshId);
                    };
                    toggle.userData = cb;
                    toggle.RegisterValueChangedCallback(cb);
                },
            });
            columns.Add(new Column
            {
                name = "actions",
                title = "操作",
                width = 230,
                makeCell = () => new VisualElement { style = { flexDirection = FlexDirection.Row } },
                bindCell = (element, index) => BindActionsCell((VisualElement)element, index),
            });
            columns.Add(MakeLabelColumn("queue", "Queue", 56, row =>
            {
                if (row.Kind != RowKind.Slot || row.Slot.Renderer == null) return "";
                var queue = RenderQueueSetup.EffectiveQueue(row.Slot.Renderer, row.Slot.SlotIndex, out var source);
                return source != null ? $"{queue}*" : queue.ToString();
            }));
            columns.Add(MakeFrameSelectorColumn());
            columns.Add(MakeLabelColumn("recommended", "推奨", 44, row =>
            {
                if (row.Kind != RowKind.Slot || row.Slot.FadeCompat == null) return "";
                var label = FrameShortLabel(EffectiveFrame(row.Slot));
                var isOverride = row.Slot.Renderer != null && frameOverrides.ContainsKey(row.Slot.Renderer.GetInstanceID());
                return isOverride ? label + "*" : label;
            }));
            columns.Add(MakeAOMEGroupColumn());
            columns.Add(MakeLabelColumn("slot", "スロット", 34, row => row.Kind == RowKind.Slot ? row.Slot.SlotIndex.ToString() : ""));
            columns.Add(MakeFrameColumn("main", "main", row => row.FadeCompat?.Main));
            columns.Add(MakeFrameColumn("alphaMask", "AM", row => row.FadeCompat?.AlphaMask));
            columns.Add(MakeFrameColumn("third", "3rd", row => row.FadeCompat?.Third));
            columns.Add(MakeFrameColumn("second", "2nd", row => row.FadeCompat?.Second));
            columns.Add(MakeLabelColumn("material", "マテリアル", 150, row => row.Kind == RowKind.Slot ? (row.Slot.Material == null ? "(なし)" : row.Slot.Material.name) : ""));
            columns.Add(MakeLabelColumn("shader", "シェーダー", 130, row => row.Kind == RowKind.Slot ? DisplayNames.Shader(row.Slot) : ""));
            columns.Add(MakeBlendShapeColumn());

            var view = new MultiColumnTreeView(columns);
            view.SetRootItems(new List<TreeViewItemData<Row>>());
            return view;
        }

        static string FrameShortLabel(FadeFrame? frame) => frame switch
        {
            FadeFrame.Main => "main",
            FadeFrame.Third => "3rd",
            FadeFrame.Second => "2nd",
            FadeFrame.AlphaMask => "AM",
            _ => "なし",
        };

        static string FrameChoiceLabel(FadeFrame? frame) => frame switch
        {
            FadeFrame.Main => "main",
            FadeFrame.AlphaMask => "alpha",
            FadeFrame.Third => "3rd",
            FadeFrame.Second => "2nd",
            _ => "推奨",
        };

        static FadeFrame? FrameChoiceValue(string label) => label switch
        {
            "main" => FadeFrame.Main,
            "alpha" => FadeFrame.AlphaMask,
            "3rd" => FadeFrame.Third,
            "2nd" => FadeFrame.Second,
            _ => (FadeFrame?)null,
        };

        static FadeFrameState StateFor(FadeCompatResult compat, FadeFrame frame) => frame switch
        {
            FadeFrame.Main => compat.Main,
            FadeFrame.Third => compat.Third,
            FadeFrame.Second => compat.Second,
            FadeFrame.AlphaMask => compat.AlphaMask,
            _ => null,
        };

        static readonly StyleColor WarningColor = new StyleColor(new Color(0.6f, 0.3f, 0.1f, 0.5f));
        static readonly StyleColor WarningTextColor = new StyleColor(new Color(1f, 0.75f, 0.4f, 1f));
        static readonly StyleColor NoColor = new StyleColor(StyleKeyword.Null);

        Column MakeFrameSelectorColumn()
        {
            return new Column
            {
                name = "frame",
                title = "枠",
                width = 72,
                makeCell = () => new PopupField<string>(FrameChoices, 0),
                bindCell = (element, index) =>
                {
                    var popup = (PopupField<string>)element;
                    if (popup.userData is EventCallback<ChangeEvent<string>> prev) popup.UnregisterValueChangedCallback(prev);
                    var row = tree.GetItemDataForIndex<Row>(index);
                    ApplyRowTint(popup, row);
                    if (row.Kind != RowKind.Mesh || row.Renderer == null)
                    {
                        popup.userData = null;
                        popup.style.display = DisplayStyle.None;
                        return;
                    }
                    popup.style.display = DisplayStyle.Flex;
                    var meshId = row.Renderer.GetInstanceID();
                    var current = frameOverrides.TryGetValue(meshId, out var f) ? (FadeFrame?)f : null;
                    popup.SetValueWithoutNotify(FrameChoiceLabel(current));

                    // override 枠が使用済みのスロットがあれば警告表示（作成は妨げない）
                    string warnTooltip = null;
                    if (current != null)
                    {
                        var incompatible = row.MeshSlots
                            .Where(s => s.FadeCompat != null)
                            .Select(s => StateFor(s.FadeCompat, current.Value) is FadeFrameState state && !state.Compatible ? state.ShortReason : null)
                            .Where(reason => reason != null)
                            .ToList();
                        if (incompatible.Count > 0) warnTooltip = string.Join("\n", incompatible);
                    }
                    // backgroundColor は ApplyRowTint が毎 bind 冒頭でリセット済み。警告時のみ上書きする
                    // （NoColor へ戻す形のリセットにすると行 tint を消してしまう）
                    if (warnTooltip != null) popup.style.backgroundColor = WarningColor;
                    popup.style.color = warnTooltip != null ? WarningTextColor : NoColor;
                    popup.tooltip = warnTooltip ?? "";

                    EventCallback<ChangeEvent<string>> cb = e =>
                    {
                        var frame = FrameChoiceValue(e.newValue);
                        if (frame == null) frameOverrides.Remove(meshId);
                        else frameOverrides[meshId] = frame.Value;
                        Refresh();
                    };
                    popup.userData = cb;
                    popup.RegisterValueChangedCallback(cb);
                },
            };
        }

        Column MakeBlendShapeColumn()
        {
            return new Column
            {
                name = "bs",
                title = "BS",
                width = 34,
                makeCell = () => new Label(),
                bindCell = (element, index) =>
                {
                    var label = (Label)element;
                    var row = tree.GetItemDataForIndex<Row>(index);
                    ApplyRowTint(label, row);
                    var smr = row.Kind == RowKind.Mesh ? row.Renderer as SkinnedMeshRenderer : null;
                    var names = smr != null ? BlendShapeSyncSetup.GetBlendShapeNames(smr) : null;
                    if (names == null || names.Count == 0)
                    {
                        label.text = "";
                        label.tooltip = "";
                        return;
                    }
                    label.text = names.Count.ToString();
                    label.tooltip = names.Count > 50
                        ? string.Join("\n", names.Take(50)) + $"\n…他{names.Count - 50}件"
                        : string.Join("\n", names);
                },
            };
        }

        /// <summary>スロット行の属するグループの AO ME ホスト suffix を表示する（両ビュー共通）。
        /// 設定済みなら「✓ 」を前置、フェード対象外グループ（onetrans/twotrans 特例を除く）は「—」</summary>
        Column MakeAOMEGroupColumn()
        {
            return new Column
            {
                name = "aomeGroup",
                title = "AO ME",
                width = 130,
                makeCell = () => new Label(),
                bindCell = (element, index) =>
                {
                    var label = (Label)element;
                    var row = tree.GetItemDataForIndex<Row>(index);
                    ApplyRowTint(label, row);
                    var group = row.Kind == RowKind.Slot ? RowGroup(row) : null;
                    if (group == null)
                    {
                        label.text = "";
                        label.tooltip = "";
                        return;
                    }
                    var isOneTwoTrans = group.Variant.StartsWith("onetrans") || group.Variant.StartsWith("twotrans");
                    if (!group.CanSetupFade && !isOneTwoTrans)
                    {
                        label.text = "—";
                        label.tooltip = "";
                        return;
                    }
                    var configured = aomeConfiguredCache.TryGetValue(group, out var isConfigured) && isConfigured;
                    var groupName = DisplayNames.Group(group);
                    label.text = configured ? $"✓ {groupName}" : groupName;
                    // ホスト GameObject 名（ASCII suffix）との対応は tooltip で示す
                    label.tooltip = AOMEHostSuffix(group);
                },
            };
        }

        Column MakeLabelColumn(string name, string title, float width, Func<Row, string> text, Func<Row, string> tooltip = null)
        {
            return new Column
            {
                name = name,
                title = title,
                width = width,
                makeCell = () => new Label(),
                bindCell = (element, index) =>
                {
                    var row = tree.GetItemDataForIndex<Row>(index);
                    ApplyRowTint(element, row);
                    var label = (Label)element;
                    label.text = text(row);
                    label.tooltip = tooltip == null ? "" : tooltip(row) ?? "";
                },
            };
        }

        Column MakeFrameColumn(string name, string title, Func<SlotInfo, FadeFrameState> stateOf)
        {
            return new Column
            {
                name = name,
                title = title,
                width = 30,
                makeCell = () => new Label(),
                bindCell = (element, index) =>
                {
                    var label = (Label)element;
                    var row = tree.GetItemDataForIndex<Row>(index);
                    ApplyRowTint(label, row);
                    if (row.Kind != RowKind.Slot || row.Slot.FadeCompat == null)
                    {
                        label.text = "";
                        label.tooltip = "";
                        return;
                    }
                    var state = stateOf(row.Slot);
                    label.text = state.Compatible ? (state.Warning ? "△" : "○") : "×";
                    label.tooltip = state.ShortReason != null
                        ? state.ShortReason + "\n\n" + string.Join("\n", state.NonDefaultProps.Select(p => $"{p.Name}: {p.Current} (default: {p.Default})"))
                        : "空き";
                },
            };
        }

        void SelectRenderer(Row row, EventBase evt)
        {
            var go = row.Renderer.gameObject;
            var additive = evt is IPointerEvent pe ? (pe.ctrlKey || pe.commandKey)
                : evt is IMouseEvent me && (me.ctrlKey || me.commandKey);
            if (additive)
            {
                var objects = new List<UnityEngine.Object>(Selection.objects);
                if (!objects.Contains(go)) objects.Add(go);
                Selection.objects = objects.ToArray();
            }
            else
            {
                Selection.activeGameObject = go;
            }
            EditorGUIUtility.PingObject(go);
        }

        void BindActionsCell(VisualElement cell, int index)
        {
            cell.Clear();
            var row = tree.GetItemDataForIndex<Row>(index);
            ApplyRowTint(cell, row);
            if (row.Kind == RowKind.Costume && row.CostumeGroups != null)
            {
                // グループ一覧はツリー構築時に前計算済み（row.CostumeGroups）。bindCell では可否判定のみ行う
                var availableCount = row.CostumeGroups.Count(g => AOMEAvailability(row.Costume, row.AvatarRoot, g).Item1);
                var button = new Button(() => CreateAOMEBatch(row.Costume, row.AvatarRoot, row.CostumeGroups)) { text = "AO ME一括" };
                button.SetEnabled(availableCount > 0);
                button.tooltip = availableCount > 0 ? $"{availableCount}グループに AO Material Editor を作成"
                    : row.AvatarRoot == null ? "アバタールートが見つかりません"
                    : "AO ME 対象グループがありません";
                cell.Add(button);
            }
            else if (row.Kind == RowKind.Group)
            {
                // ホスト設定済み判定は Refresh 時に aomeConfiguredCache へ前計算済み（bind では Find/HasComponent を再計算しない）
                var configured = aomeConfiguredCache.TryGetValue(row.Group, out var isConfigured) && isConfigured;
                var button = new Button(() => { CreateAOMaterialEditor(row.Costume, row.AvatarRoot, row.Group); Refresh(); }) { text = configured ? "AO ME✓" : "AO ME" };
                var (enabled, reason) = AOMEAvailability(row.Costume, row.AvatarRoot, row.Group);
                button.SetEnabled(enabled);
                button.tooltip = !enabled ? reason : configured ? "設定済み（再実行で上書き更新）" : "AO Material Editor を作成";
                button.style.backgroundColor = configured ? ConfiguredColor : (StyleColor)StyleKeyword.Null;
                cell.Add(button);
            }
            else if (row.Kind == RowKind.Mesh && row.Renderer != null)
            {
                // Toggle Menu 対象判定は Refresh 時に toggleMenuTargetsCache へ前計算済み
                // （FindMenusTargeting 相当のアバター全体走査を bind ごとに行わない）
                var toggleMatches = FindCachedToggleMenus(row.AvatarRoot, row.Renderer);
                var toggleConfigured = toggleMatches.Count > 0;
                var toggleButton = new Button(() => OpenToggleMenuForMesh(row)) { text = toggleConfigured ? "Toggle✓" : "Toggle" };
                toggleButton.SetEnabled(row.AvatarRoot != null);
                toggleButton.tooltip = toggleConfigured
                    ? string.Join("\n", toggleMatches.Select(c => AvatarUtil.RelativePath(row.AvatarRoot, c.gameObject)))
                    : row.AvatarRoot != null ? "このメッシュだけの Toggle Menu を作成" : "アバタールートが見つかりません";
                toggleButton.style.backgroundColor = toggleConfigured ? ConfiguredColor : (StyleColor)StyleKeyword.Null;
                cell.Add(toggleButton);

                // メッシュ行 [Q]: ChangeRenderQueue が1つでも付いていれば設定済み表示
                // （ボタンは cell.Clear() 後に毎 bind 新規作成されるため、色/文言のリセットは自動的に成立する）
                var queueConfigured = row.Renderer.GetComponents<CRQ>().Length > 0;
                var queueButton = new Button { text = queueConfigured ? "Q✓" : "Q" };
                queueButton.clicked += () => ShowMeshQueuePopup(row, queueButton.worldBound);
                queueButton.tooltip = "Render Queue 一括設定";
                queueButton.style.backgroundColor = queueConfigured ? ConfiguredColor : (StyleColor)StyleKeyword.Null;
                cell.Add(queueButton);

                if (row.Renderer is SkinnedMeshRenderer)
                {
                    var configured = row.Renderer.GetComponent<ModularAvatarBlendshapeSync>() != null;
                    var bsButton = new Button(() => ApplyBSSync(row)) { text = configured ? "BS✓" : "BS" };
                    var (enabled, reason) = BSSyncAvailability(row.Renderer, row.AvatarRoot);
                    bsButton.SetEnabled(enabled);
                    bsButton.tooltip = !enabled ? reason : configured ? "設定済み（再実行で同名バインドを更新）" : "BlendShape Sync を設定";
                    bsButton.style.backgroundColor = configured ? ConfiguredColor : (StyleColor)StyleKeyword.Null;
                    cell.Add(bsButton);
                }
            }
            else if (row.Kind == RowKind.Slot && row.Slot.Renderer != null)
            {
                // スロット行 [Q]: このスロットに効く ChangeRenderQueue があれば設定済み表示
                RenderQueueSetup.EffectiveQueue(row.Slot.Renderer, row.Slot.SlotIndex, out var source);
                var configured = source != null;
                var button = new Button { text = configured ? "Q✓" : "Q" };
                button.clicked += () => ShowQueuePopup(row, button.worldBound);
                button.tooltip = "Render Queue 設定";
                button.style.backgroundColor = configured ? ConfiguredColor : (StyleColor)StyleKeyword.Null;
                cell.Add(button);
            }
        }

        (bool, string) AOMEAvailability(GameObject costume, GameObject avatarRoot, SlotGroup group)
        {
            if (!AOMaterialEditorSetup.IsAvailable) return (false, "aoyon.material-editor が未導入");
            if (avatarRoot == null) return (false, "アバタールートが見つかりません");
            var isOneTwoTrans = group.Variant.StartsWith("onetrans") || group.Variant.StartsWith("twotrans");
            if (isOneTwoTrans)
            {
                // onetrans/twotrans は実効枠（DriverProps(group.Preset)）を適用するだけで shader override は行わないため、
                // 3rd 枠が使用済みでも成立するが、未知 family / マテリアル欠損は不可
                if (group.Family == "unknown" || group.Slots.All(s => s.Material == null)) return (false, group.FadeDisabledReason ?? "対象外");
                if (group.Preset == FadeFrame.Main) return (false, "main 駆動は AO ME 不要（既に透過、_Color を直接駆動）");
                return (true, null);
            }
            if (!group.CanSetupFade) return (false, group.FadeDisabledReason);
            return (true, null);
        }

        /// <summary>BS Sync 実行可否＋不可理由。target==素体自身、素体側とシェイプ名が一致しない場合等は不可</summary>
        (bool, string) BSSyncAvailability(Renderer renderer, GameObject avatarRoot)
        {
            var smr = renderer as SkinnedMeshRenderer;
            if (smr == null || BlendShapeSyncSetup.GetBlendShapeNames(smr).Count == 0) return (false, "BlendShapeなし");
            if (avatarRoot == null) return (false, "アバタールートが見つかりません");
            var baseMesh = EffectiveBaseMesh(avatarRoot);
            if (baseMesh == smr) return (false, "素体自身");
            if (BlendShapeSyncSetup.MatchingNames(smr, baseMesh).Count == 0) return (false, "素体と同名のシェイプなし");
            return (true, null);
        }

        void ApplyBSSync(Row row)
        {
            var smr = (SkinnedMeshRenderer)row.Renderer;
            BlendShapeSyncSetup.Apply(smr, EffectiveBaseMesh(row.AvatarRoot));
            Refresh();
        }

        static string AOMEHostSuffix(SlotGroup group)
        {
            var isOneTwoTrans = group.Variant.StartsWith("onetrans") || group.Variant.StartsWith("twotrans");
            // onetrans/twotrans は Preset==null（全枠使用済み）でも作成可能で DriverProps は Third を既定枠にする
            // （CreateAOMaterialEditor の effectivePreset と同じ規則）。実効枠が異なれば DriverProps 内容も異なるため、
            // ホスト suffix にも実効枠を反映して同一ホストへの衝突を防ぐ
            var effectivePreset = isOneTwoTrans ? (group.Preset ?? FadeFrame.Third) : group.Preset;
            var suffix = group.Variant;
            if (effectivePreset == FadeFrame.Second) suffix += "_2nd";
            else if (effectivePreset == FadeFrame.AlphaMask) suffix += "_alpha_mask";
            else if (effectivePreset == FadeFrame.Third) suffix += "_3rd";
            // AlphaMask 枠は調整 override を適用しない（DriverProps が mode=2 を設定済み）ため suffix も付けない
            if (group.Preset != FadeFrame.AlphaMask)
            {
                switch (group.AlphaMaskAdjust)
                {
                    case AlphaMaskAdjust.Neutralize: suffix += "_amoff"; break;
                    case AlphaMaskAdjust.ToMultiply: suffix += "_ammul"; break;
                }
            }
            return suffix;
        }

        GameObject FindAOMEHost(GameObject costume, SlotGroup group)
        {
            var t = costume.transform.Find($"trans/{AOMEHostSuffix(group)}");
            return t == null ? null : t.gameObject;
        }

        void CreateAOMaterialEditor(GameObject costume, GameObject avatarRoot, SlotGroup group)
        {
            var isOneTwoTrans = group.Variant.StartsWith("onetrans") || group.Variant.StartsWith("twotrans");
            var suffix = AOMEHostSuffix(group);

            var host = FindOrCreateChild(FindOrCreateChild(costume, "trans"), suffix);

            var slots = group.Slots
                .Where(s => s.Renderer != null)
                .Select(s => new AOMaterialEditorSetup.SlotTarget
                {
                    RendererPath = AvatarUtil.RelativePath(avatarRoot, s.Renderer.gameObject),
                    MaterialIndex = s.SlotIndex,
                })
                .Where(s => !string.IsNullOrEmpty(s.RendererPath))
                .ToList();

            Shader shader = null;
            if (group.NeedsShaderOverride)
            {
                shader = AssetDatabase.LoadAssetAtPath<Shader>(AssetDatabase.GUIDToAssetPath(group.TransparentGuid));
                if (shader == null)
                {
                    EditorUtility.DisplayDialog("Costume Dashboard", $"透過版シェーダーが見つかりません (GUID: {group.TransparentGuid})", "OK");
                    return;
                }
            }

            // onetrans/twotrans は Preset==null（全枠使用済み）でも作成可能で DriverProps は Third を既定枠にするため、
            // AlphaMask 調整 override の判定も同じ実効枠で行う（raw Preset で判定すると null 時に override が落ちる）
            var effectivePreset = isOneTwoTrans ? (group.Preset ?? FadeFrame.Third) : group.Preset;

            List<PresetProperty> properties;
            if (isOneTwoTrans)
            {
                properties = TransparencyPresets.DriverProps(effectivePreset.Value);
            }
            else
            {
                properties = TransparencyPresets.For(group.Preset.Value);
                if (group.Family == "lilToon_multi") properties.Add(TransparencyPresets.TransparentModeOverride());
            }

            // 実効枠が Main/Third/Second のとき、AlphaMask 残存値による色フェードへの干渉を
            // AO ME 側で打ち消す。AlphaMask 枠自体は DriverProps が既に _AlphaMaskMode=2 を設定済みのため対象外
            if (effectivePreset == FadeFrame.Main || effectivePreset == FadeFrame.Third || effectivePreset == FadeFrame.Second)
            {
                switch (group.AlphaMaskAdjust)
                {
                    case AlphaMaskAdjust.Neutralize:
                        properties.Add(TransparencyPresets.AlphaMaskModeOverride(0));
                        break;
                    case AlphaMaskAdjust.ToMultiply:
                        properties.Add(TransparencyPresets.AlphaMaskModeOverride(2));
                        break;
                }
            }

            AOMaterialEditorSetup.Apply(host, slots, shader, properties);
        }

        /// <summary>メッシュビューの衣装行 [AO ME一括]: groups のうち AOMEAvailability が有効な全グループに CreateAOMaterialEditor を実行</summary>
        void CreateAOMEBatch(GameObject costume, GameObject avatarRoot, List<SlotGroup> groups)
        {
            var created = 0;
            var skipped = 0;
            // グループキー/ホスト suffix の設計上、通常は同一バッチ内で suffix が重複することはないが、
            // 万一の回帰（キー正規化漏れ等）で衝突した場合に SlotTargets を後勝ちで上書きしてしまう事故を防ぐ防御線
            var usedSuffixes = new HashSet<string>();
            foreach (var group in groups)
            {
                var (enabled, _) = AOMEAvailability(costume, avatarRoot, group);
                if (!enabled)
                {
                    skipped++;
                    continue;
                }
                var suffix = AOMEHostSuffix(group);
                if (!usedSuffixes.Add(suffix))
                {
                    skipped++;
                    continue;
                }
                CreateAOMaterialEditor(costume, avatarRoot, group);
                created++;
            }
            Refresh();
            ShowNotification(new GUIContent($"AO ME: {created}グループ作成 / {skipped}スキップ"));
        }

        static GameObject FindOrCreateChild(GameObject parent, string name)
        {
            var t = parent.transform.Find(name);
            if (t != null) return t.gameObject;
            var go = new GameObject(name);
            go.transform.SetParent(parent.transform, false);
            Undo.RegisterCreatedObjectUndo(go, "Create " + name);
            return go;
        }

        void ShowQueuePopup(Row row, Rect anchor)
        {
            UnityEditor.PopupWindow.Show(anchor, new QueuePopup(row.Slot, Refresh));
        }

        void ShowMeshQueuePopup(Row row, Rect anchor)
        {
            var initialSlotIndex = row.MeshSlots.Count > 0 ? row.MeshSlots[0].SlotIndex : 0;
            UnityEditor.PopupWindow.Show(anchor, new QueuePopup(row.Renderer, initialSlotIndex, Refresh));
        }

        class QueuePopup : PopupWindowContent
        {
            readonly SlotInfo slot;
            readonly Renderer renderer;
            readonly System.Action onApplied;
            int value;

            /// <summary>スロット単位設定</summary>
            public QueuePopup(SlotInfo slot, System.Action onApplied)
            {
                this.slot = slot;
                this.onApplied = onApplied;
                value = RenderQueueSetup.EffectiveQueue(slot.Renderer, slot.SlotIndex, out _);
            }

            /// <summary>メッシュ（Renderer）単位一括設定</summary>
            public QueuePopup(Renderer renderer, int initialSlotIndex, System.Action onApplied)
            {
                this.renderer = renderer;
                this.onApplied = onApplied;
                value = RenderQueueSetup.EffectiveQueue(renderer, initialSlotIndex, out _);
            }

            public override Vector2 GetWindowSize() => new Vector2(220, renderer != null ? 56 : 76);

            public override void OnGUI(Rect rect)
            {
                value = EditorGUILayout.IntField("Render Queue", value);
                if (renderer != null)
                {
                    if (GUILayout.Button("このメッシュ全体に設定"))
                    {
                        RenderQueueSetup.SetAll(renderer, value);
                        onApplied();
                        editorWindow.Close();
                    }
                    return;
                }
                if (GUILayout.Button("このスロットに設定"))
                {
                    RenderQueueSetup.Set(slot.Renderer, slot.SlotIndex, value);
                    onApplied();
                    editorWindow.Close();
                }
                RenderQueueSetup.EffectiveQueue(slot.Renderer, slot.SlotIndex, out var source);
                using (new EditorGUI.DisabledScope(source == null))
                {
                    if (GUILayout.Button("ChangeRenderQueue を削除"))
                    {
                        RenderQueueSetup.Remove(source);
                        onApplied();
                        editorWindow.Close();
                    }
                }
            }
        }

        void OpenToggleMenuForMesh(Row row)
        {
            // row.MeshSlots はグループ内バケツなので、同一レンダラーのスロットが複数グループに
            // またがる場合（スロットごとに実効枠が異なる等）は一部しか含まない。
            // Toggle Menu はメッシュ全体を対象にするため、衣装全体から再収集する
            var slots = MaterialSlotScanner.Scan(row.Costume).Where(s => s.Renderer == row.Renderer).ToList();
            ToggleMenuCreateDialog.Show(row.Costume, row.AvatarRoot, slots, frameOverrides, row.Renderer.name, Refresh);
        }

        void CreateToggleMenu()
        {
            var slots = CollectCheckedSlots();
            if (slots.Count == 0)
            {
                EditorUtility.DisplayDialog("Costume Dashboard", "✓ 列でメッシュをチェックしてください", "OK");
                return;
            }
            var avatarRoots = slots.Select(s => s.avatarRoot).Distinct().ToList();
            if (avatarRoots.Count != 1 || avatarRoots[0] == null)
            {
                EditorUtility.DisplayDialog("Costume Dashboard", "チェックしたメッシュは同一アバター配下である必要があります", "OK");
                return;
            }
            ToggleMenuCreateDialog.Show(slots[0].costume, avatarRoots[0], slots.Select(s => s.slot).ToList(), frameOverrides, slots[0].costume.name, () =>
            {
                checkedMeshes.Clear();
                Refresh();
            });
        }

        List<(SlotInfo slot, GameObject costume, GameObject avatarRoot)> CollectCheckedSlots()
        {
            var result = new List<(SlotInfo slot, GameObject costume, GameObject avatarRoot)>();
            foreach (var costume in costumeRoots)
            {
                if (costume == null) continue;
                var avatarRoot = AvatarUtil.FindAvatarRoot(costume);
                foreach (var slot in MaterialSlotScanner.Scan(costume))
                {
                    if (slot.Renderer != null && checkedMeshes.Contains(slot.Renderer.GetInstanceID()))
                    {
                        result.Add((slot, costume, avatarRoot));
                    }
                }
            }
            return result;
        }

        List<(Renderer renderer, GameObject avatarRoot)> CollectCheckedMeshes()
        {
            var seen = new HashSet<int>();
            var result = new List<(Renderer renderer, GameObject avatarRoot)>();
            foreach (var costume in costumeRoots)
            {
                if (costume == null) continue;
                var avatarRoot = AvatarUtil.FindAvatarRoot(costume);
                foreach (var slot in MaterialSlotScanner.Scan(costume))
                {
                    if (slot.Renderer == null || !checkedMeshes.Contains(slot.Renderer.GetInstanceID())) continue;
                    if (!seen.Add(slot.Renderer.GetInstanceID())) continue;
                    result.Add((slot.Renderer, avatarRoot));
                }
            }
            return result;
        }

        void BSSyncChecked()
        {
            var applied = 0;
            var skipped = 0;
            foreach (var (renderer, avatarRoot) in CollectCheckedMeshes())
            {
                var (enabled, _) = BSSyncAvailability(renderer, avatarRoot);
                if (!enabled)
                {
                    skipped++;
                    continue;
                }
                BlendShapeSyncSetup.Apply((SkinnedMeshRenderer)renderer, EffectiveBaseMesh(avatarRoot));
                applied++;
            }
            Refresh();
            ShowNotification(new GUIContent($"BS Sync: {applied}件適用 / {skipped}件スキップ"));
        }

        class ToggleMenuCreateDialog : EditorWindow
        {
            GameObject costume;
            GameObject avatarRoot;
            List<SlotInfo> slots;
            System.Action onCreated;
            string menuName;
            float transitionSeconds = 1f;

            readonly Dictionary<int, FadeFrame> dialogOverrides = new Dictionary<int, FadeFrame>();
            List<Renderer> meshOrder;
            Dictionary<Renderer, List<SlotInfo>> meshSlots;

            public static void Show(GameObject costume, GameObject avatarRoot, List<SlotInfo> slots, IReadOnlyDictionary<int, FadeFrame> initialOverrides, string defaultMenuName, System.Action onCreated)
            {
                var window = CreateInstance<ToggleMenuCreateDialog>();
                window.titleContent = new GUIContent("Toggle Menu作成");
                window.costume = costume;
                window.avatarRoot = avatarRoot;
                window.slots = slots;
                window.onCreated = onCreated;
                window.menuName = defaultMenuName;

                window.meshOrder = new List<Renderer>();
                window.meshSlots = new Dictionary<Renderer, List<SlotInfo>>();
                foreach (var slot in slots)
                {
                    if (slot.Renderer == null) continue;
                    if (!window.meshSlots.TryGetValue(slot.Renderer, out var list))
                    {
                        list = new List<SlotInfo>();
                        window.meshSlots[slot.Renderer] = list;
                        window.meshOrder.Add(slot.Renderer);
                    }
                    list.Add(slot);
                }
                if (initialOverrides != null)
                {
                    foreach (var renderer in window.meshOrder)
                    {
                        if (initialOverrides.TryGetValue(renderer.GetInstanceID(), out var f))
                        {
                            window.dialogOverrides[renderer.GetInstanceID()] = f;
                        }
                    }
                }

                window.minSize = window.maxSize = new Vector2(360, 100 + window.meshOrder.Count * 22);
                window.ShowUtility();
            }

            void OnGUI()
            {
                menuName = EditorGUILayout.TextField("メニュー名", menuName);
                transitionSeconds = EditorGUILayout.FloatField("フェード秒数", transitionSeconds);

                if (meshOrder.Count > 0)
                {
                    EditorGUILayout.LabelField("フェード枠（メッシュ単位）", EditorStyles.boldLabel);
                    foreach (var renderer in meshOrder)
                    {
                        if (renderer == null) continue;
                        var id = renderer.GetInstanceID();
                        var currentIndex = dialogOverrides.TryGetValue(id, out var f) ? FrameChoices.IndexOf(FrameChoiceLabel(f)) : 0;
                        EditorGUILayout.BeginHorizontal();
                        EditorGUILayout.LabelField(renderer.name, GUILayout.Width(180));
                        var newIndex = EditorGUILayout.Popup(currentIndex, FrameChoices.ToArray());
                        EditorGUILayout.EndHorizontal();
                        if (newIndex != currentIndex)
                        {
                            var newFrame = FrameChoiceValue(FrameChoices[newIndex]);
                            if (newFrame == null) dialogOverrides.Remove(id);
                            else dialogOverrides[id] = newFrame.Value;
                        }
                    }
                }

                using (new EditorGUI.DisabledScope(string.IsNullOrEmpty(menuName)))
                {
                    if (GUILayout.Button("作成"))
                    {
                        Create();
                        Close();
                    }
                }
            }

            void Create()
            {
                var togglePaths = slots
                    .Select(s => AvatarUtil.RelativePath(avatarRoot, s.Renderer.gameObject))
                    .Where(p => !string.IsNullOrEmpty(p))
                    .Distinct()
                    .ToList();
                var fades = ToggleMenuSetup.BuildFadeTargets(avatarRoot, slots, dialogOverrides);

                var host = new GameObject(menuName);
                host.transform.SetParent(costume.transform, false);
                Undo.RegisterCreatedObjectUndo(host, "Create Toggle Menu");
                ToggleMenuSetup.Create(host, togglePaths, fades, transitionSeconds);
                onCreated();
            }
        }
    }
}
