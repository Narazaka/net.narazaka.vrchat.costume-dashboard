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

        readonly HashSet<long> checkedSlots = new HashSet<long>();

        static long SlotKey(SlotInfo slot) => ((long)slot.Renderer.GetInstanceID() << 8) | (uint)(slot.SlotIndex & 0xff);

        internal enum RowKind { Costume, Group, Slot }

        internal class Row
        {
            public RowKind Kind;
            public GameObject Costume;
            public GameObject AvatarRoot;
            public SlotGroup Group;
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

        List<TreeViewItemData<Row>> BuildTreeItems()
        {
            var items = new List<TreeViewItemData<Row>>();
            var id = 0;
            foreach (var costume in costumeRoots)
            {
                if (costume == null) continue;
                var avatarRoot = AvatarUtil.FindAvatarRoot(costume);
                var groups = MaterialSlotScanner.GroupByShader(MaterialSlotScanner.Scan(costume));
                var groupItems = new List<TreeViewItemData<Row>>();
                foreach (var group in groups)
                {
                    var slotItems = group.Slots
                        .Select(slot => new TreeViewItemData<Row>(id++, new Row { Kind = RowKind.Slot, Costume = costume, AvatarRoot = avatarRoot, Group = group, Slot = slot }))
                        .ToList();
                    groupItems.Add(new TreeViewItemData<Row>(id++, new Row { Kind = RowKind.Group, Costume = costume, AvatarRoot = avatarRoot, Group = group }, slotItems));
                }
                items.Add(new TreeViewItemData<Row>(id++, new Row { Kind = RowKind.Costume, Costume = costume, AvatarRoot = avatarRoot }, groupItems));
            }
            return items;
        }

        MultiColumnTreeView BuildTree()
        {
            var columns = new Columns();
            columns.Add(MakeLabelColumn("object", "オブジェクト", 240, row =>
            {
                switch (row.Kind)
                {
                    case RowKind.Costume:
                        var warn = row.AvatarRoot == null ? " ⚠ アバタールートが見つかりません" : "";
                        var toggleCount = row.Costume.GetComponentsInChildren<net.narazaka.avatarmenucreator.components.AvatarToggleMenuCreator>(true).Length;
                        var toggleInfo = toggleCount > 0 ? $" [Toggle Menu: {toggleCount}]" : "";
                        return row.Costume.name + toggleInfo + warn;
                    case RowKind.Group:
                        var preset = row.Group.Preset switch
                        {
                            FadeFrame.Third => "3rd",
                            FadeFrame.Second => "2nd",
                            FadeFrame.AlphaMask => "AM",
                            _ => "×",
                        };
                        var reason = row.Group.CanSetupFade ? "" : $" ({row.Group.FadeDisabledReason})";
                        return $"{row.Group.Family}/{row.Group.Variant} [{preset}] ({row.Group.Slots.Count}){reason}";
                    default:
                        return row.Slot.Renderer == null ? "(missing)" : row.Slot.Renderer.name;
                }
            }));
            columns.Add(MakeLabelColumn("slot", "スロット", 50, row => row.Kind == RowKind.Slot ? row.Slot.SlotIndex.ToString() : ""));
            columns.Add(MakeLabelColumn("material", "マテリアル", 150, row => row.Kind == RowKind.Slot ? (row.Slot.Material == null ? "(なし)" : row.Slot.Material.name) : ""));
            columns.Add(MakeLabelColumn("shader", "シェーダー", 130, row => row.Kind == RowKind.Slot ? FormatShader(row.Slot) : ""));
            columns.Add(MakeFrameColumn("third", "3rd", row => row.FadeCompat?.Third));
            columns.Add(MakeFrameColumn("second", "2nd", row => row.FadeCompat?.Second));
            columns.Add(MakeFrameColumn("alphaMask", "AM", row => row.FadeCompat?.AlphaMask));
            columns.Add(MakeLabelColumn("recommended", "推奨", 50, row =>
            {
                if (row.Kind != RowKind.Slot || row.Slot.FadeCompat == null) return "";
                return row.Slot.FadeCompat.Recommended switch
                {
                    FadeFrame.Third => "3rd",
                    FadeFrame.Second => "2nd",
                    FadeFrame.AlphaMask => "AM",
                    _ => "なし",
                };
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
                    button.style.display = row.Kind == RowKind.Slot && row.Slot.Renderer != null ? DisplayStyle.Flex : DisplayStyle.None;
                    button.clickable = new Clickable(() => SelectRenderer(row));
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
                    var row = tree.GetItemDataForIndex<Row>(index);
                    if (row.Kind != RowKind.Slot || row.Slot.Renderer == null)
                    {
                        toggle.style.display = DisplayStyle.None;
                        return;
                    }
                    toggle.style.display = DisplayStyle.Flex;
                    toggle.SetValueWithoutNotify(checkedSlots.Contains(SlotKey(row.Slot)));
                    toggle.RegisterValueChangedCallback(e =>
                    {
                        if (e.newValue) checkedSlots.Add(SlotKey(row.Slot));
                        else checkedSlots.Remove(SlotKey(row.Slot));
                    });
                },
            });
            columns.Add(new Column
            {
                name = "actions",
                title = "操作",
                width = 110,
                makeCell = () => new VisualElement { style = { flexDirection = FlexDirection.Row } },
                bindCell = (element, index) => BindActionsCell((VisualElement)element, index),
            });

            var view = new MultiColumnTreeView(columns);
            view.SetRootItems(new List<TreeViewItemData<Row>>());
            return view;
        }

        static string FormatShader(SlotInfo slot)
        {
            if (slot.Material == null || slot.Material.shader == null) return "(なし)";
            if (!slot.Family.IsKnown) return slot.Material.shader.name;
            var multi = slot.MultiTransparentMode >= 0 ? $" tm={slot.MultiTransparentMode}" : "";
            return $"{slot.Family.Variant}{multi}";
        }

        Column MakeLabelColumn(string name, string title, float width, System.Func<Row, string> text)
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

        Column MakeFrameColumn(string name, string title, System.Func<SlotInfo, FadeFrameState> stateOf)
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
                        : string.Join("\n", state.NonDefaultProps.Select(p => $"{p.Name}: {p.Current} (default: {p.Default})"));
                },
            };
        }

        void SelectRenderer(Row row)
        {
            var go = row.Slot.Renderer.gameObject;
            var e = Event.current;
            var additive = e != null && (e.control || e.command);
            if (additive)
            {
                var objects = new List<Object>(Selection.objects);
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
            else if (row.Kind == RowKind.Slot && row.Slot.Renderer != null)
            {
                var button = new Button(() => ShowQueuePopup(row)) { text = "Q" };
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
                properties = TransparencyPresets.OneTwoTransOverrides();
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

        void ShowQueuePopup(Row row)
        {
            UnityEditor.PopupWindow.Show(new Rect(Event.current != null ? Event.current.mousePosition : Vector2.zero, Vector2.zero),
                new QueuePopup(row.Slot, Refresh));
        }

        class QueuePopup : PopupWindowContent
        {
            readonly SlotInfo slot;
            readonly System.Action onApplied;
            int value;

            public QueuePopup(SlotInfo slot, System.Action onApplied)
            {
                this.slot = slot;
                this.onApplied = onApplied;
                value = RenderQueueSetup.EffectiveQueue(slot.Renderer, slot.SlotIndex, out _);
            }

            public override Vector2 GetWindowSize() => new Vector2(220, 76);

            public override void OnGUI(Rect rect)
            {
                value = EditorGUILayout.IntField("Render Queue", value);
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
            ToggleMenuCreateDialog.Show(slots[0].costume, avatarRoots[0], slots.Select(s => s.slot).ToList(), () =>
            {
                checkedSlots.Clear();
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
                    if (slot.Renderer != null && checkedSlots.Contains(SlotKey(slot)))
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

            public static void Show(GameObject costume, GameObject avatarRoot, List<SlotInfo> slots, System.Action onCreated)
            {
                var window = CreateInstance<ToggleMenuCreateDialog>();
                window.titleContent = new GUIContent("Toggle Menu作成");
                window.costume = costume;
                window.avatarRoot = avatarRoot;
                window.slots = slots;
                window.onCreated = onCreated;
                window.menuName = costume.name;
                window.minSize = window.maxSize = new Vector2(320, 100);
                window.ShowUtility();
            }

            void OnGUI()
            {
                menuName = EditorGUILayout.TextField("メニュー名", menuName);
                transitionSeconds = EditorGUILayout.FloatField("フェード秒数", transitionSeconds);
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
                var fades = new List<ToggleMenuSetup.FadeTarget>();
                var fadeMeshes = new HashSet<string>();
                foreach (var slot in slots)
                {
                    var frame = slot.FadeCompat?.Recommended;
                    if (frame == null) continue;
                    var meshPath = AvatarUtil.RelativePath(avatarRoot, slot.Renderer.gameObject);
                    if (string.IsNullOrEmpty(meshPath) || !fadeMeshes.Add(meshPath)) continue;
                    fades.Add(new ToggleMenuSetup.FadeTarget { MeshPath = meshPath, Frame = frame.Value });
                }

                var host = new GameObject(menuName);
                host.transform.SetParent(costume.transform, false);
                Undo.RegisterCreatedObjectUndo(host, "Create Toggle Menu");
                ToggleMenuSetup.Create(host, togglePaths, fades, transitionSeconds);
                onCreated();
            }
        }
    }
}
