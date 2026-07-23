using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using net.narazaka.avatarmenucreator;

namespace Narazaka.VRChat.CostumeDashboard.Editor
{
    /// <summary>
    /// 色変えメニューの選択肢に「色違い衣装」のマテリアルを流し込む。
    ///
    /// 手作業では色変えプレハブをアバター内へ入れて既存衣装と同名にし、Avatar Choose Menu Creator の
    /// picker ボタンで値を取得する、という手順が必要だった。ここではリネームも picker も経由せず、
    /// 衣装ルート基準の相対パスで色違い側の同じ位置の Renderer を直接引いて書き込む。
    /// variantRoot は Project の Prefab アセット・シーン上 GameObject のどちらでもよい（Transform.Find のみ使う）。
    /// </summary>
    public static class ChooseMenuVariantSetup
    {
        /// <summary>対応が取れず選択肢へ書き込めなかったスロット1件</summary>
        public class MissingSlot
        {
            /// <summary>衣装ルート基準の相対パス（衣装ルート自身は ""）</summary>
            public string CostumePath;
            public int SlotIndex;
            public string Reason;

            public override string ToString() =>
                $"{(string.IsNullOrEmpty(CostumePath) ? "(ルート)" : CostumePath)} [スロット{SlotIndex}] {Reason}";
        }

        public class VariantResult
        {
            public int Applied;
            public List<MissingSlot> Missing = new List<MissingSlot>();
        }

        /// <summary>
        /// menu.ChooseMaterials の既存キー（= 選択肢0 で列挙済みのスロット）のうち costumeRoot 配下のものについて、
        /// variantRoot 内の同一相対パス・同一スロットのマテリアルを選択肢 chooseIndex へ書き込む。
        /// 対応が取れないスロットは書き込まずスキップし、VariantResult.Missing に理由を記録する
        /// （＝その選択肢は未設定のまま残り、意図せず元の色で埋まることはない）。
        /// costumeRoot 配下でないキーは他衣装のぶんなので Missing にも数えず無視する。
        /// </summary>
        public static VariantResult ApplyVariant(AvatarChooseMenu menu, GameObject avatarRoot, GameObject costumeRoot, GameObject variantRoot, int chooseIndex)
        {
            var result = new VariantResult();
            if (menu == null || avatarRoot == null || costumeRoot == null || variantRoot == null) return result;

            var costumePath = AvatarUtil.RelativePath(avatarRoot, costumeRoot);
            if (costumePath == null) return result; // 衣装がアバター配下にない

            foreach (var key in menu.ChooseMaterials.Keys.ToList())
            {
                var (avatarPath, slotIndex) = key;
                if (!TryToCostumeRelative(costumePath, avatarPath, out var relative)) continue;

                var target = FindChild(variantRoot, relative);
                if (target == null)
                {
                    result.Missing.Add(new MissingSlot { CostumePath = relative, SlotIndex = slotIndex, Reason = "色違い側に同じパスのオブジェクトがありません" });
                    continue;
                }
                var renderer = target.GetComponent<Renderer>();
                if (renderer == null)
                {
                    result.Missing.Add(new MissingSlot { CostumePath = relative, SlotIndex = slotIndex, Reason = "色違い側に Renderer がありません" });
                    continue;
                }
                var materials = renderer.sharedMaterials;
                if (slotIndex < 0 || slotIndex >= materials.Length)
                {
                    result.Missing.Add(new MissingSlot { CostumePath = relative, SlotIndex = slotIndex, Reason = $"色違い側のスロット数が不足（{materials.Length}）" });
                    continue;
                }
                var material = materials[slotIndex];
                if (material == null)
                {
                    result.Missing.Add(new MissingSlot { CostumePath = relative, SlotIndex = slotIndex, Reason = "色違い側のマテリアルが未設定" });
                    continue;
                }
                if (!menu.ChooseMaterials.TryGetValue(key, out var values) || values == null) continue;
                values[chooseIndex] = material;
                result.Applied++;
            }
            return result;
        }

        /// <summary>アバタールート相対パスを衣装ルート相対パスへ読み替える。衣装配下でなければ false</summary>
        public static bool TryToCostumeRelative(string costumePath, string avatarPath, out string relative)
        {
            relative = null;
            if (avatarPath == null) return false;
            if (string.IsNullOrEmpty(costumePath))
            {
                // 衣装ルート == アバタールート
                relative = avatarPath;
                return true;
            }
            if (avatarPath == costumePath)
            {
                relative = "";
                return true;
            }
            if (!avatarPath.StartsWith(costumePath + "/")) return false;
            relative = avatarPath.Substring(costumePath.Length + 1);
            return true;
        }

        static GameObject FindChild(GameObject root, string relative)
        {
            if (string.IsNullOrEmpty(relative)) return root;
            var t = root.transform.Find(relative);
            return t == null ? null : t.gameObject;
        }

        /// <summary>選択肢名を設定する（空文字は既定名のままにするため設定しない）</summary>
        public static void SetChooseName(AvatarChooseMenu menu, int chooseIndex, string name)
        {
            if (menu == null || string.IsNullOrEmpty(name)) return;
            menu.ChooseNames[chooseIndex] = name;
        }

        /// <summary>Missing の内訳を Console へ出す（通知は件数のみなので詳細はこちらで確認する）</summary>
        public static void LogMissing(string label, IEnumerable<MissingSlot> missing)
        {
            var list = missing.ToList();
            if (list.Count == 0) return;
            Debug.LogWarning($"[Costume Dashboard] {label}: 対応不可 {list.Count}件\n" + string.Join("\n", list.Select(m => m.ToString())));
        }
    }
}
