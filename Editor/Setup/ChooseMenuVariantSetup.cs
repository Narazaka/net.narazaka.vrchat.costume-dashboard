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
        public class VariantResult
        {
            public int Applied;
            /// <summary>対応が取れず選択肢へ書き込めなかったスロットの一覧。
            /// SetupIssue.Target には衣装ルート基準の相対パス（衣装ルート自身は ""）が入る</summary>
            public List<SetupIssue> Missing = new List<SetupIssue>();
        }

        public class RowInput
        {
            public string Name;
            /// <summary>costumeRoots と同じ順序。null 要素はその衣装に割り当てなし</summary>
            public IReadOnlyList<GameObject> Variants;
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
                    result.Missing.Add(new SetupIssue { Target = relative, SlotIndex = slotIndex, Reason = "色違い側に同じパスのオブジェクトがありません" });
                    continue;
                }
                var renderer = target.GetComponent<Renderer>();
                if (renderer == null)
                {
                    result.Missing.Add(new SetupIssue { Target = relative, SlotIndex = slotIndex, Reason = "色違い側に Renderer がありません" });
                    continue;
                }
                var materials = renderer.sharedMaterials;
                if (slotIndex < 0 || slotIndex >= materials.Length)
                {
                    result.Missing.Add(new SetupIssue { Target = relative, SlotIndex = slotIndex, Reason = $"色違い側のスロット数が不足（{materials.Length}）" });
                    continue;
                }
                var material = materials[slotIndex];
                if (material == null)
                {
                    result.Missing.Add(new SetupIssue { Target = relative, SlotIndex = slotIndex, Reason = "色違い側のマテリアルが未設定" });
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
        public static void LogMissing(string label, IEnumerable<SetupIssue> missing)
        {
            var list = missing.ToList();
            if (list.Count == 0) return;
            Debug.LogWarning($"[Costume Dashboard] {label}: 対応不可 {list.Count}件\n" + string.Join("\n", list.Select(m => m.ToString())));
        }

        /// <summary>各行の選択肢名を設定し、割り当てられたカラバリからマテリアルを流し込む。
        /// 戻り値は適用スロット数と、対応するスロットが無く未設定のまま残した内訳</summary>
        public static (int Applied, List<SetupIssue> Missing) ApplyRows(AvatarChooseMenu menu, GameObject avatarRoot,
            IReadOnlyList<GameObject> costumeRoots, IReadOnlyList<RowInput> rows, int baseChooseIndex)
        {
            var applied = 0;
            var missing = new List<SetupIssue>();
            for (var i = 0; i < rows.Count; i++)
            {
                var row = rows[i];
                var chooseIndex = baseChooseIndex + i;
                SetChooseName(menu, chooseIndex, row.Name);
                if (row.Variants == null) continue;
                for (var c = 0; c < costumeRoots.Count; c++)
                {
                    var variant = c < row.Variants.Count ? row.Variants[c] : null;
                    if (variant == null) continue;
                    var result = ApplyVariant(menu, avatarRoot, costumeRoots[c], variant, chooseIndex);
                    applied += result.Applied;
                    missing.AddRange(result.Missing);
                }
            }
            return (applied, missing);
        }
    }
}
