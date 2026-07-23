using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using net.narazaka.avatarmenucreator;
using net.narazaka.avatarmenucreator.components;

namespace Narazaka.VRChat.CostumeDashboard.Editor
{
    /// <summary>
    /// 色変えメニュー作成ダイアログ。「衣装 × 選択肢」の表に色違い衣装（Prefab アセット / シーン上 GameObject）を
    /// 割り当てて一度に作成する。既存の AvatarChooseMenuCreator を対象にすれば選択肢を後から追加できる。
    /// </summary>
    public class ChooseMenuCreateDialog : EditorWindow
    {
        class VariantRow
        {
            public string Name = "";
            /// <summary>衣装インデックス -> その衣装の色違いルート（未指定は null）</summary>
            public GameObject[] Variants;
        }

        static readonly string[] ModeChoices = { "新規作成", "既存メニューに選択肢を追加" };

        GameObject avatarRoot;
        List<(GameObject Costume, List<SlotInfo> Slots)> costumes;
        System.Action onCreated;

        int mode;
        AvatarChooseMenuCreator existingCreator;
        readonly List<VariantRow> rows = new List<VariantRow>();
        Vector2 scroll;

        bool IsNew => mode == 0;

        /// <summary>新規作成モードでは行0が「元（アバター上の現状）」で色違い指定を持たない</summary>
        bool RowHasVariants(int rowIndex) => !(IsNew && rowIndex == 0);

        public static void Show(GameObject avatarRoot, List<(GameObject Costume, List<SlotInfo> Slots)> costumes, System.Action onCreated)
        {
            var window = CreateInstance<ChooseMenuCreateDialog>();
            window.titleContent = new GUIContent("色変えメニュー作成");
            window.avatarRoot = avatarRoot;
            window.costumes = costumes;
            window.onCreated = onCreated;
            window.ResetRows();
            window.minSize = new Vector2(Mathf.Min(300 + costumes.Count * 180, 1000), 260);
            window.ShowUtility();
        }

        void ResetRows()
        {
            rows.Clear();
            if (IsNew) rows.Add(new VariantRow { Name = "元", Variants = NewVariants() });
            rows.Add(new VariantRow { Variants = NewVariants() });
        }

        GameObject[] NewVariants() => new GameObject[costumes.Count];

        void OnGUI()
        {
            var newMode = EditorGUILayout.Popup("対象", mode, ModeChoices);
            if (newMode != mode)
            {
                mode = newMode;
                ResetRows();
            }

            if (!IsNew)
            {
                existingCreator = (AvatarChooseMenuCreator)EditorGUILayout.ObjectField("既存メニュー", existingCreator, typeof(AvatarChooseMenuCreator), true);
            }

            EditorGUILayout.Space();

            scroll = EditorGUILayout.BeginScrollView(scroll);

            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField("選択肢", GUILayout.Width(60));
                EditorGUILayout.LabelField("名前", GUILayout.Width(120));
                foreach (var (costume, _) in costumes)
                {
                    EditorGUILayout.LabelField(costume == null ? "(なし)" : costume.name, GUILayout.Width(170));
                }
            }

            var baseIndex = BaseChooseIndex();
            for (var i = 0; i < rows.Count; i++)
            {
                var row = rows[i];
                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUILayout.LabelField((baseIndex + i).ToString(), GUILayout.Width(60));
                    row.Name = EditorGUILayout.TextField(row.Name, GUILayout.Width(120));
                    if (!RowHasVariants(i))
                    {
                        EditorGUILayout.LabelField("（アバター上の現状）");
                        continue;
                    }
                    for (var c = 0; c < costumes.Count; c++)
                    {
                        var newVariant = (GameObject)EditorGUILayout.ObjectField(row.Variants[c], typeof(GameObject), true, GUILayout.Width(170));
                        if (newVariant == row.Variants[c]) continue;
                        row.Variants[c] = newVariant;
                        // 選択肢名が未入力なら色違いのオブジェクト名を初期値として入れる（以降は手編集可）
                        if (newVariant != null && string.IsNullOrEmpty(row.Name)) row.Name = newVariant.name;
                    }
                }
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("+ 選択肢を追加", GUILayout.Width(120)))
                {
                    rows.Add(new VariantRow { Variants = NewVariants() });
                }
                if (rows.Count > MinRows() && GUILayout.Button("− 末尾を削除", GUILayout.Width(120)))
                {
                    rows.RemoveAt(rows.Count - 1);
                }
            }

            EditorGUILayout.EndScrollView();

            EditorGUILayout.Space();
            var error = Validate();
            if (error != null) EditorGUILayout.HelpBox(error, MessageType.Warning);
            using (new EditorGUI.DisabledScope(error != null))
            {
                if (GUILayout.Button("作成"))
                {
                    Create();
                    Close();
                }
            }
        }

        int MinRows() => IsNew ? 2 : 1;

        /// <summary>表の先頭行に対応する選択肢インデックス。既存追加モードは既存 ChooseCount の直後から</summary>
        int BaseChooseIndex() => IsNew || existingCreator == null ? 0 : existingCreator.AvatarChooseMenu.ChooseCount;

        string Validate()
        {
            if (avatarRoot == null) return "アバタールートが見つかりません";
            if (!IsNew)
            {
                if (existingCreator == null) return "既存メニューを指定してください";
                if (AvatarUtil.FindAvatarRoot(existingCreator.gameObject) != avatarRoot) return "既存メニューが対象アバター配下ではありません";
            }
            return null;
        }

        void Create()
        {
            AvatarChooseMenuCreator creator;
            var baseIndex = BaseChooseIndex();

            if (IsNew)
            {
                var slots = costumes.SelectMany(c => c.Slots).ToList();
                creator = ChooseMenuSetup.Create(avatarRoot, slots, rows.Count);
                if (creator == null)
                {
                    EditorUtility.DisplayDialog("Costume Dashboard", "色変えメニューの対象スロットがありません（アバタールート不明 / マテリアル未設定）", "OK");
                    return;
                }
            }
            else
            {
                creator = existingCreator;
                Undo.RecordObject(creator, "Add Choose Menu Variants");
                creator.AvatarChooseMenu.ChooseCount = baseIndex + rows.Count;
            }
            var menu = creator.AvatarChooseMenu;

            var applied = 0;
            var missing = new List<ChooseMenuVariantSetup.MissingSlot>();
            for (var i = 0; i < rows.Count; i++)
            {
                var row = rows[i];
                var chooseIndex = baseIndex + i;
                ChooseMenuVariantSetup.SetChooseName(menu, chooseIndex, row.Name);
                if (!RowHasVariants(i)) continue;
                for (var c = 0; c < costumes.Count; c++)
                {
                    var variant = row.Variants[c];
                    if (variant == null) continue;
                    var result = ChooseMenuVariantSetup.ApplyVariant(menu, avatarRoot, costumes[c].Costume, variant, chooseIndex);
                    applied += result.Applied;
                    missing.AddRange(result.Missing);
                }
            }

            EditorUtility.SetDirty(creator);
            ChooseMenuVariantSetup.LogMissing(creator.gameObject.name, missing);
            Selection.activeObject = creator.gameObject;
            onCreated();
            EditorUtility.DisplayDialog("Costume Dashboard",
                missing.Count > 0
                    ? $"色変えメニュー: {applied}スロット設定 / 対応不可 {missing.Count}件（内訳は Console を参照）"
                    : $"色変えメニュー: {applied}スロット設定",
                "OK");
        }
    }
}
