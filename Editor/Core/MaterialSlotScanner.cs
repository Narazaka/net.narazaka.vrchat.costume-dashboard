using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Narazaka.VRChat.CostumeDashboard.Editor
{
    public class SlotInfo
    {
        public Renderer Renderer;
        public int SlotIndex;
        public Material Material;
        public ShaderFamilyInfo Family;
        /// <summary>family が lilToon_multi のときの _TransparentMode 値。それ以外 -1</summary>
        public int MultiTransparentMode = -1;
        /// <summary>Family.IsKnown のときのみ非 null</summary>
        public FadeCompatResult FadeCompat;
    }

    public class SlotGroup
    {
        public string Family;
        public string Variant;
        public string ShaderGuid;
        /// <summary>グループ内マテリアルの推奨フェード枠（グループ分割キー）。null = フェード不可</summary>
        public FadeFrame? Preset;
        public List<SlotInfo> Slots = new List<SlotInfo>();
        public bool NeedsShaderOverride;
        public string TransparentGuid;
        public bool CanSetupFade;
        public string FadeDisabledReason;
    }

    public static class MaterialSlotScanner
    {
        public static List<SlotInfo> Scan(GameObject costumeRoot)
        {
            var result = new List<SlotInfo>();
            if (costumeRoot == null) return result;
            foreach (var renderer in costumeRoot.GetComponentsInChildren<Renderer>(true))
            {
                if (!(renderer is SkinnedMeshRenderer) && !(renderer is MeshRenderer)) continue;
                var materials = renderer.sharedMaterials;
                for (var i = 0; i < materials.Length; i++)
                {
                    var mat = materials[i];
                    var family = ShaderCatalog.Resolve(mat == null ? null : mat.shader);
                    var info = new SlotInfo
                    {
                        Renderer = renderer,
                        SlotIndex = i,
                        Material = mat,
                        Family = family,
                    };
                    if (family.Family == "lilToon_multi" && mat != null && mat.HasProperty("_TransparentMode"))
                    {
                        info.MultiTransparentMode = Mathf.RoundToInt(mat.GetFloat("_TransparentMode"));
                    }
                    if (family.IsKnown && mat != null)
                    {
                        info.FadeCompat = FadeCompatChecker.Check(mat);
                    }
                    result.Add(info);
                }
            }
            return result;
        }

        /// <summary>
        /// シェーダー種別・実効フェード枠でグルーピングする。
        /// effectiveFrame が null のときは slot.FadeCompat?.Recommended を実効枠として使う（既定挙動）。
        /// 非 null のときはその関数の戻り値をグループ分割キー・group.Preset の両方に使う（UI 側のカスタム枠選択を反映するため）
        /// </summary>
        public static List<SlotGroup> GroupByShader(IEnumerable<SlotInfo> slots, Func<SlotInfo, FadeFrame?> effectiveFrame = null)
        {
            var groups = new Dictionary<(string, string, string, FadeFrame?, bool), SlotGroup>();
            foreach (var slot in slots)
            {
                var guid = ShaderGuidOf(slot.Material);
                var preset = effectiveFrame != null ? effectiveFrame(slot) : slot.FadeCompat?.Recommended;
                // lilToon_multi の _TransparentMode が Refraction/Fur/Gem 系 (>=3) はフェード遮断対象。
                // 通常モードと混在させると先頭スロット次第で availability が誤るため別グループに分離する
                var multiBlocked = slot.Family.Family == "lilToon_multi" && slot.MultiTransparentMode >= 3;
                var key = (slot.Family.Family, slot.Family.Variant, guid, preset, multiBlocked);
                if (!groups.TryGetValue(key, out var group))
                {
                    group = new SlotGroup
                    {
                        Family = slot.Family.Family,
                        Variant = slot.Family.Variant,
                        ShaderGuid = guid,
                        Preset = preset,
                        NeedsShaderOverride = slot.Family.NeedsShaderOverride,
                        TransparentGuid = slot.Family.TransparentGuid,
                    };
                    SetFadeAvailability(group, slot);
                    groups.Add(key, group);
                }
                group.Slots.Add(slot);
            }
            return groups.Values
                .OrderBy(g => g.Family).ThenBy(g => g.Variant).ThenBy(g => g.Preset)
                .ToList();
        }

        static void SetFadeAvailability(SlotGroup group, SlotInfo sample)
        {
            if (sample.Material == null || sample.Material.shader == null)
            {
                group.CanSetupFade = false;
                group.FadeDisabledReason = "マテリアル未設定";
                return;
            }
            if (!sample.Family.IsKnown)
            {
                group.CanSetupFade = false;
                group.FadeDisabledReason = "未知のシェーダー";
                return;
            }
            if (sample.Family.Family == "lilToon_multi" && sample.MultiTransparentMode >= 3)
            {
                group.CanSetupFade = false;
                group.FadeDisabledReason = "_TransparentMode が Refraction/Fur/Gem 系";
                return;
            }
            if (group.Preset == null)
            {
                group.CanSetupFade = false;
                group.FadeDisabledReason = "main/AlphaMask/3rd/2nd 全枠使用済み";
                return;
            }
            group.CanSetupFade = true;
            group.FadeDisabledReason = null;
        }

        static string ShaderGuidOf(Material mat)
        {
            if (mat == null || mat.shader == null) return "";
            var path = AssetDatabase.GetAssetPath(mat.shader);
            return string.IsNullOrEmpty(path) ? "" : AssetDatabase.AssetPathToGUID(path);
        }
    }
}
