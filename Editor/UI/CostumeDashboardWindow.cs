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
    }
}
