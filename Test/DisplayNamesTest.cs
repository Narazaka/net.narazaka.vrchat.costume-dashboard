using NUnit.Framework;

namespace Narazaka.VRChat.CostumeDashboard.Editor.Test
{
    public class DisplayNamesTest
    {
        [Test]
        public void Variant_Opaque_O_Tess()
        {
            Assert.That(DisplayNames.Variant("lilToon_tess", "opaque_o", -1), Is.EqualTo("不透明 Outline Tess"));
        }

        [Test]
        public void Variant_TwoTrans()
        {
            Assert.That(DisplayNames.Variant("lilToon_std", "twotrans", -1), Is.EqualTo("半透明 2パス"));
        }

        [Test]
        public void Variant_Motchiri_Cutout()
        {
            Assert.That(DisplayNames.Variant("motchiri_std", "cutout", -1), Is.EqualTo("もっちり カットアウト"));
        }

        [Test]
        public void Variant_Multi_Trans()
        {
            // Outline/Multi の並びは実装定義。assert をそのまま実装の期待値とする
            Assert.That(DisplayNames.Variant("lilToon_multi", "multi_o", 2), Is.EqualTo("半透明 Multi Outline"));
        }

        [Test]
        public void Frame_AlphaMask_IsAlpha()
        {
            Assert.That(DisplayNames.Frame(FadeFrame.AlphaMask), Is.EqualTo("Alpha"));
        }

        [Test]
        public void Group_WithAdjust()
        {
            var group = new SlotGroup
            {
                Family = "lilToon_std",
                Variant = "twotrans",
                Preset = FadeFrame.AlphaMask,
                AlphaMaskAdjust = AlphaMaskAdjust.ToMultiply,
            };
            Assert.That(DisplayNames.Group(group), Is.EqualTo("半透明 2パス → Alpha (マスク乗算化)"));
        }
    }
}
