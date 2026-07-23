using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using net.narazaka.avatarmenucreator.components;
using net.narazaka.avatarmenucreator.collections.instance;

namespace Narazaka.VRChat.CostumeDashboard.Editor
{
    public static class ChooseMenuSetup
    {
        /// <summary>
        /// スロット群をアバタールート単位でグループ化する。各スロットの Renderer から
        /// AvatarUtil.FindAvatarRoot でアバタールートを解決し、同一アバタールートのスロットを束ねる。
        /// Renderer が null / アバタールート未解決のスロットは除外する。出現順を保つ。
        /// </summary>
        public static List<(GameObject AvatarRoot, List<SlotInfo> Slots)> GroupByAvatarRoot(IEnumerable<SlotInfo> slots)
        {
            var groups = new Dictionary<GameObject, List<SlotInfo>>();
            var order = new List<GameObject>();
            foreach (var slot in slots)
            {
                if (slot.Renderer == null) continue;
                var avatarRoot = AvatarUtil.FindAvatarRoot(slot.Renderer.gameObject);
                if (avatarRoot == null) continue;
                if (!groups.TryGetValue(avatarRoot, out var list))
                {
                    list = new List<SlotInfo>();
                    groups[avatarRoot] = list;
                    order.Add(avatarRoot);
                }
                list.Add(slot);
            }
            return order.Select(root => (root, groups[root])).ToList();
        }

        /// <summary>
        /// avatarRoot 配下に色変えメニュー雛形（AvatarChooseMenuCreator）を新規作成する。
        /// Material≠null の各スロットを ChooseMaterials に「選択肢0 = 現在のマテリアル」で列挙する。
        /// 常に新規 GameObject「色」（重複時は GetUniqueNameForSibling で一意化）を作り、既存コンポーネントの
        /// 再利用・マージはしない。対象スロットが0件のときは何も作らず null を返す。Undo 対応。
        /// chooseCount は選択肢数（既定2 = 元＋1バリエーション）。2未満は2に丸める。
        /// </summary>
        public static AvatarChooseMenuCreator Create(GameObject avatarRoot, IEnumerable<SlotInfo> slots, int chooseCount = 2)
        {
            if (avatarRoot == null) return null;

            // 先に登録対象を確定する（0件なら GameObject を作らない）
            var entries = new List<((string meshPath, int slotIndex) key, Material material)>();
            foreach (var slot in slots)
            {
                if (slot.Renderer == null || slot.Material == null) continue;
                var meshPath = AvatarUtil.RelativePath(avatarRoot, slot.Renderer.gameObject);
                if (string.IsNullOrEmpty(meshPath)) continue;
                entries.Add(((meshPath, slot.SlotIndex), slot.Material));
            }
            if (entries.Count == 0) return null;

            var name = GameObjectUtility.GetUniqueNameForSibling(avatarRoot.transform, "色");
            var host = new GameObject(name);
            Undo.RegisterCreatedObjectUndo(host, "Create Choose Menu");
            host.transform.SetParent(avatarRoot.transform, false);

            var creator = Undo.AddComponent<AvatarChooseMenuCreator>(host);
            var menu = creator.AvatarChooseMenu;
            menu.TransitionSeconds = 0f;
            menu.Saved = true;
            menu.Synced = true;
            menu.ChooseCount = Mathf.Max(2, chooseCount);
            menu.ChooseDefaultValue = 0;
            menu.UseParentMenu = true;
            // 選択肢数に必要最低限の bit 数へ圧縮する（Synced かつ ChooseCount > 1 で有効。既定で ON にする）
            menu.UseCompressed = true;

            foreach (var (key, material) in entries)
            {
                menu.ChooseMaterials[key] = new IntMaterialDictionary();
                menu.ChooseMaterials[key][0] = material;
            }

            EditorUtility.SetDirty(creator);
            return creator;
        }
    }
}
