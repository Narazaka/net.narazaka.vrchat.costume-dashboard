using System.Reflection;
using NUnit.Framework;

namespace Narazaka.VRChat.CostumeDashboard.Editor.Test
{
    public class CostumeDashboardWindowTest
    {
        static string Suffix(SlotGroup group)
        {
            var method = typeof(CostumeDashboardWindow).GetMethod("AOMEHostSuffix", BindingFlags.NonPublic | BindingFlags.Static);
            Assert.That(method, Is.Not.Null, "AOMEHostSuffix が見つからない");
            return (string)method.Invoke(null, new object[] { group });
        }

        [Test]
        public void AOMEHostSuffix_AdjustAppendedForColorFrames()
        {
            var group = new SlotGroup { Variant = "opaque", Preset = FadeFrame.Third, AlphaMaskAdjust = AlphaMaskAdjust.ToMultiply };
            Assert.That(Suffix(group), Is.EqualTo("opaque_3rd_ammul"));
            group.AlphaMaskAdjust = AlphaMaskAdjust.Neutralize;
            Assert.That(Suffix(group), Is.EqualTo("opaque_3rd_amoff"));
            group.AlphaMaskAdjust = AlphaMaskAdjust.None;
            Assert.That(Suffix(group), Is.EqualTo("opaque_3rd"));
        }

        [Test]
        public void AOMEHostSuffix_AlphaMaskPreset_NoAdjustSuffix()
        {
            // AlphaMask 枠は調整 override を適用しない（DriverProps が mode=2 を設定済み）ため suffix も付かない
            var group = new SlotGroup { Variant = "opaque", Preset = FadeFrame.AlphaMask, AlphaMaskAdjust = AlphaMaskAdjust.ToMultiply };
            Assert.That(Suffix(group), Is.EqualTo("opaque_alpha_mask"));
        }

        [Test]
        public void AOMEHostSuffix_OneTwoTrans_IncludesEffectivePreset()
        {
            // onetrans/twotrans も実効枠（group.Preset ?? Third）で suffix を分け、
            // Preset 違い（DriverProps 内容が違う）グループが同一ホストに衝突しないようにする
            var group = new SlotGroup { Variant = "onetrans", Preset = FadeFrame.Third };
            Assert.That(Suffix(group), Is.EqualTo("onetrans_3rd"));
            group.Preset = null;
            Assert.That(Suffix(group), Is.EqualTo("onetrans_3rd"));
            group.Preset = FadeFrame.AlphaMask;
            Assert.That(Suffix(group), Is.EqualTo("onetrans_alpha_mask"));
        }
    }
}
