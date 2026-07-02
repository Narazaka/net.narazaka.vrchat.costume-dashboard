using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using UnityEditor.UIElements;

namespace Narazaka.VRChat.CostumeDashboard.Editor
{
    public class CostumeDashboardWindow : EditorWindow
    {
        [SerializeField] List<GameObject> costumeRoots = new List<GameObject>();

        MultiColumnTreeView tree;
        VisualElement costumeListContainer;

        readonly HashSet<int> checkedMeshes = new HashSet<int>();

        /// <summary>Renderer instanceID -> ユーザーが明示選択したフェード枠。エントリなし = 推奨枠に従う</summary>
        readonly Dictionary<int, FadeFrame> frameOverrides = new Dictionary<int, FadeFrame>();

        static readonly List<string> FrameChoices = new List<string> { "推奨", "main", "alpha", "3rd", "2nd" };

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
            toolbar.Add(new Button(AddSelectedCostumes) { text = "選択から衣装を追加" });
            toolbar.Add(new Button(Refresh) { text = "更新" });
            toolbar.Add(new Button(CreateToggleMenu) { text = "✓ から Toggle Menu作成" });
            root.Add(toolbar);

            costumeListContainer = new VisualElement { style = { flexShrink = 0 } };
            root.Add(costumeListContainer);

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
            RebuildCostumeList();
            tree.SetRootItems(BuildTreeItems());
            tree.Rebuild();
        }

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

        /// <summary>実効フェード枠 = frameOverrides の明示選択があればそれ、なければ推奨枠</summary>
        FadeFrame? EffectiveFrame(SlotInfo slot) =>
            slot.Renderer != null && frameOverrides.TryGetValue(slot.Renderer.GetInstanceID(), out var f) ? f : slot.FadeCompat?.Recommended;

        List<TreeViewItemData<Row>> BuildTreeItems()
        {
            var items = new List<TreeViewItemData<Row>>();
            var id = 0;
            foreach (var costume in costumeRoots)
            {
                if (costume == null) continue;
                var avatarRoot = AvatarUtil.FindAvatarRoot(costume);
                var groups = MaterialSlotScanner.GroupByShader(MaterialSlotScanner.Scan(costume), EffectiveFrame);
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
                        var preset = FrameShortLabel(row.Group.Preset);
                        var reason = row.Group.CanSetupFade ? "" : $" ({row.Group.FadeDisabledReason})";
                        return $"{row.Group.Family}/{row.Group.Variant} [{preset}] ({row.Group.Slots.Count}){reason}";
                    case RowKind.Mesh:
                        return row.Renderer == null ? "(missing)" : row.Renderer.name;
                    default:
                        return "";
                }
            }));
            columns.Add(MakeFrameSelectorColumn());
            columns.Add(MakeLabelColumn("slot", "スロット", 50, row => row.Kind == RowKind.Slot ? row.Slot.SlotIndex.ToString() : ""));
            columns.Add(MakeLabelColumn("material", "マテリアル", 150, row => row.Kind == RowKind.Slot ? (row.Slot.Material == null ? "(なし)" : row.Slot.Material.name) : ""));
            columns.Add(MakeLabelColumn("shader", "シェーダー", 130, row => row.Kind == RowKind.Slot ? FormatShader(row.Slot) : ""));
            columns.Add(MakeFrameColumn("main", "main", row => row.FadeCompat?.Main));
            columns.Add(MakeFrameColumn("alphaMask", "AM", row => row.FadeCompat?.AlphaMask));
            columns.Add(MakeFrameColumn("third", "3rd", row => row.FadeCompat?.Third));
            columns.Add(MakeFrameColumn("second", "2nd", row => row.FadeCompat?.Second));
            columns.Add(MakeLabelColumn("recommended", "推奨", 50, row =>
            {
                if (row.Kind != RowKind.Slot || row.Slot.FadeCompat == null) return "";
                var label = FrameShortLabel(EffectiveFrame(row.Slot));
                var isOverride = row.Slot.Renderer != null && frameOverrides.ContainsKey(row.Slot.Renderer.GetInstanceID());
                return isOverride ? label + "*" : label;
            }));
            columns.Add(MakeLabelColumn("queue", "Queue", 60, row =>
            {
                if (row.Kind != RowKind.Slot || row.Slot.Renderer == null) return "";
                var queue = RenderQueueSetup.EffectiveQueue(row.Slot.Renderer, row.Slot.SlotIndex, out var source);
                return source != null ? $"{queue}*" : queue.ToString();
            }));
            columns.Add(new Column
            {
                name = "select",
                title = "選択",
                width = 60,
                makeCell = () => new Button { text = "Select" },
                bindCell = (element, index) =>
                {
                    var button = (Button)element;
                    var row = tree.GetItemDataForIndex<Row>(index);
                    button.style.display = row.Kind == RowKind.Mesh && row.Renderer != null ? DisplayStyle.Flex : DisplayStyle.None;
                    button.clickable = new Clickable((EventBase evt) => SelectRenderer(row, evt));
                },
            });
            columns.Add(new Column
            {
                name = "check",
                title = "✓",
                width = 30,
                makeCell = () => new Toggle(),
                bindCell = (element, index) =>
                {
                    var toggle = (Toggle)element;
                    if (toggle.userData is EventCallback<ChangeEvent<bool>> prev) toggle.UnregisterValueChangedCallback(prev);
                    var row = tree.GetItemDataForIndex<Row>(index);
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
                width = 160,
                makeCell = () => new VisualElement { style = { flexDirection = FlexDirection.Row } },
                bindCell = (element, index) => BindActionsCell((VisualElement)element, index),
            });

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
                width = 80,
                makeCell = () => new PopupField<string>(FrameChoices, 0),
                bindCell = (element, index) =>
                {
                    var popup = (PopupField<string>)element;
                    if (popup.userData is EventCallback<ChangeEvent<string>> prev) popup.UnregisterValueChangedCallback(prev);
                    var row = tree.GetItemDataForIndex<Row>(index);
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
                    popup.style.backgroundColor = warnTooltip != null ? WarningColor : NoColor;
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

        static string FormatShader(SlotInfo slot)
        {
            if (slot.Material == null || slot.Material.shader == null) return "(なし)";
            if (!slot.Family.IsKnown) return slot.Material.shader.name;
            var multi = slot.MultiTransparentMode >= 0 ? $" tm={slot.MultiTransparentMode}" : "";
            return $"{slot.Family.Variant}{multi}";
        }

        Column MakeLabelColumn(string name, string title, float width, Func<Row, string> text)
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
                    ((Label)element).text = text(row);
                },
            };
        }

        Column MakeFrameColumn(string name, string title, Func<SlotInfo, FadeFrameState> stateOf)
        {
            return new Column
            {
                name = name,
                title = title,
                width = 36,
                makeCell = () => new Label(),
                bindCell = (element, index) =>
                {
                    var label = (Label)element;
                    var row = tree.GetItemDataForIndex<Row>(index);
                    if (row.Kind != RowKind.Slot || row.Slot.FadeCompat == null)
                    {
                        label.text = "";
                        label.tooltip = "";
                        return;
                    }
                    var state = stateOf(row.Slot);
                    label.text = state.Compatible ? "○" : "×";
                    label.tooltip = state.Compatible
                        ? "空き"
                        : (state.ShortReason ?? "") + "\n\n" + string.Join("\n", state.NonDefaultProps.Select(p => $"{p.Name}: {p.Current} (default: {p.Default})"));
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
            if (row.Kind == RowKind.Group)
            {
                var existingHost = FindAOMEHost(row);
                var configured = AOMaterialEditorSetup.HasComponent(existingHost);
                var button = new Button(() => CreateAOMaterialEditor(row)) { text = configured ? "AO ME✓" : "AO ME" };
                var (enabled, reason) = AOMEAvailability(row);
                button.SetEnabled(enabled);
                button.tooltip = !enabled ? reason : configured ? "設定済み（再実行で上書き更新）" : "AO Material Editor を作成";
                cell.Add(button);
            }
            else if (row.Kind == RowKind.Mesh && row.Renderer != null)
            {
                var toggleButton = new Button(() => OpenToggleMenuForMesh(row)) { text = "Toggle" };
                toggleButton.SetEnabled(row.AvatarRoot != null);
                toggleButton.tooltip = row.AvatarRoot != null ? "このメッシュだけの Toggle Menu を作成" : "アバタールートが見つかりません";
                cell.Add(toggleButton);

                var queueButton = new Button { text = "Q" };
                queueButton.clicked += () => ShowMeshQueuePopup(row, queueButton.worldBound);
                queueButton.tooltip = "Render Queue 一括設定";
                cell.Add(queueButton);
            }
            else if (row.Kind == RowKind.Slot && row.Slot.Renderer != null)
            {
                var button = new Button { text = "Q" };
                button.clicked += () => ShowQueuePopup(row, button.worldBound);
                button.tooltip = "Render Queue 設定";
                cell.Add(button);
            }
        }

        (bool, string) AOMEAvailability(Row row)
        {
            if (!AOMaterialEditorSetup.IsAvailable) return (false, "aoyon.material-editor が未導入");
            if (row.AvatarRoot == null) return (false, "アバタールートが見つかりません");
            var group = row.Group;
            var isOneTwoTrans = group.Variant.StartsWith("onetrans") || group.Variant.StartsWith("twotrans");
            if (isOneTwoTrans)
            {
                // onetrans/twotrans は preset なしで作るため、3rd 枠が使用済みでも成立するが
                // 未知 family / マテリアル欠損は不可
                if (group.Family == "unknown" || group.Slots.All(s => s.Material == null)) return (false, group.FadeDisabledReason ?? "対象外");
                return (true, null);
            }
            if (!group.CanSetupFade) return (false, group.FadeDisabledReason);
            return (true, null);
        }

        static string AOMEHostSuffix(SlotGroup group)
        {
            var isOneTwoTrans = group.Variant.StartsWith("onetrans") || group.Variant.StartsWith("twotrans");
            var suffix = group.Variant;
            if (!isOneTwoTrans && group.Preset == FadeFrame.Second) suffix += "_2nd";
            if (!isOneTwoTrans && group.Preset == FadeFrame.AlphaMask) suffix += "_alpha_mask";
            return suffix;
        }

        GameObject FindAOMEHost(Row row)
        {
            var t = row.Costume.transform.Find($"trans/{AOMEHostSuffix(row.Group)}");
            return t == null ? null : t.gameObject;
        }

        void CreateAOMaterialEditor(Row row)
        {
            var group = row.Group;
            var isOneTwoTrans = group.Variant.StartsWith("onetrans") || group.Variant.StartsWith("twotrans");
            var suffix = AOMEHostSuffix(group);

            var host = FindOrCreateChild(FindOrCreateChild(row.Costume, "trans"), suffix);

            var slots = group.Slots
                .Where(s => s.Renderer != null)
                .Select(s => new AOMaterialEditorSetup.SlotTarget
                {
                    RendererPath = AvatarUtil.RelativePath(row.AvatarRoot, s.Renderer.gameObject),
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

            List<PresetProperty> properties;
            if (isOneTwoTrans)
            {
                properties = TransparencyPresets.DriverProps(group.Preset ?? FadeFrame.Third);
            }
            else
            {
                properties = TransparencyPresets.For(group.Preset.Value);
                if (group.Family == "lilToon_multi") properties.Add(TransparencyPresets.TransparentModeOverride());
            }

            AOMaterialEditorSetup.Apply(host, slots, shader, properties);
            Refresh();
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
