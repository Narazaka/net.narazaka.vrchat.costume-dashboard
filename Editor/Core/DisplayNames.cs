namespace Narazaka.VRChat.CostumeDashboard.Editor
{
    /// <summary>UI 表示用の日本語表示名。挙動（ホスト GameObject 名等の ASCII 識別子）には使わない</summary>
    public static class DisplayNames
    {
        /// <summary>スロットのシェーダー表示名（例: 不透明 Outline Tess / 半透明 Multi / もっちり カットアウト）。
        /// unknown family は従来どおり shader 名を返す</summary>
        public static string Shader(SlotInfo slot)
        {
            if (slot.Material == null || slot.Material.shader == null) return "(なし)";
            if (!slot.Family.IsKnown) return slot.Material.shader.name;
            return Variant(slot.Family.Family, slot.Family.Variant, slot.MultiTransparentMode);
        }

        /// <summary>variant/family/multiTm からの表示名（Shader(slot) の実体。テスト容易性のため分離）</summary>
        public static string Variant(string family, string variant, int multiTransparentMode)
        {
            var outline = variant != null && variant.EndsWith("_o");
            var baseVariant = outline ? variant.Substring(0, variant.Length - 2) : variant;

            if (family == "lilToon_multi")
            {
                // multi は実効 _TransparentMode から variant 日本語を決める（3以上=対象外表示のまま）
                var multiName = multiTransparentMode switch
                {
                    0 => "不透明",
                    1 => "カットアウト",
                    2 => "半透明",
                    _ => baseVariant,
                };
                multiName += " Multi";
                if (outline) multiName += " Outline";
                return multiName;
            }

            var name = baseVariant switch
            {
                "opaque" => "不透明",
                "cutout" => "カットアウト",
                "trans" => "半透明",
                "onetrans" => "半透明 1パス",
                "twotrans" => "半透明 2パス",
                _ => "不明",
            };
            if (outline) name += " Outline";

            switch (family)
            {
                case "lilToon_tess": name += " Tess"; break;
                case "lilToon_lite": name += " Lite"; break;
                case "motchiri_std": name = "もっちり " + name; break;
                case "motchiri_tess": name = "もっちり " + name + " Tess"; break;
            }

            return name;
        }

        /// <summary>フェード枠の表示名: main / Alpha / 3rd / 2nd</summary>
        public static string Frame(FadeFrame frame) => frame switch
        {
            FadeFrame.Main => "main",
            FadeFrame.AlphaMask => "Alpha",
            FadeFrame.Third => "3rd",
            FadeFrame.Second => "2nd",
            _ => frame.ToString(),
        };

        /// <summary>AO ME グループ表示名（例: 半透明 2パス → Alpha (マスク乗算化)）。Preset null は variant のみ</summary>
        public static string Group(SlotGroup group)
        {
            var multiTm = group.Slots.Count > 0 ? group.Slots[0].MultiTransparentMode : -1;
            var name = Variant(group.Family, group.Variant, multiTm);
            if (group.Preset == null) return name;
            name += " → " + Frame(group.Preset.Value);
            switch (group.AlphaMaskAdjust)
            {
                case AlphaMaskAdjust.Neutralize: name += " (マスク無効化)"; break;
                case AlphaMaskAdjust.ToMultiply: name += " (マスク乗算化)"; break;
            }
            return name;
        }
    }
}
