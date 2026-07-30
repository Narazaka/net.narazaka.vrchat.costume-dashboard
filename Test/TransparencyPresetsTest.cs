using System.Linq;
using NUnit.Framework;

namespace Narazaka.VRChat.CostumeDashboard.Editor.Test
{
    public class TransparencyPresetsTest
    {
        [Test]
        public void For_Third_ContainsCommonAndThirdDriver()
        {
            var props = TransparencyPresets.For(FadeFrame.Third);
            Assert.That(props.First(p => p.Name == "_DstBlend").FloatValue, Is.EqualTo(10));
            Assert.That(props.First(p => p.Name == "_UseMain3rdTex").FloatValue, Is.EqualTo(1));
            Assert.That(props.First(p => p.Name == "_Main3rdTexBlendMode").FloatValue, Is.EqualTo(3));
            Assert.That(props.First(p => p.Name == "_Main3rdTexAlphaMode").FloatValue, Is.EqualTo(2));
            Assert.That(props.Any(p => p.Name == "_UseMain2ndTex"), Is.False);
            Assert.That(props.Any(p => p.Name == "_AlphaMaskMode"), Is.False);
        }

        [Test]
        public void For_Second_ContainsSecondDriver()
        {
            var props = TransparencyPresets.For(FadeFrame.Second);
            Assert.That(props.First(p => p.Name == "_UseMain2ndTex").FloatValue, Is.EqualTo(1));
            Assert.That(props.Any(p => p.Name == "_UseMain3rdTex"), Is.False);
        }

        [Test]
        public void For_AlphaMask_ContainsAlphaMaskDriver()
        {
            var props = TransparencyPresets.For(FadeFrame.AlphaMask);
            Assert.That(props.First(p => p.Name == "_AlphaMaskMode").FloatValue, Is.EqualTo(2));
            Assert.That(props.First(p => p.Name == "_AlphaMaskValue").FloatValue, Is.EqualTo(0));
        }

        [Test]
        public void For_AlwaysOverridesCutoffToNearZero()
        {
            // cutout 由来の残存 _Cutoff によるアルファクリップを防ぐため、全枠で _Cutoff を 0.001 に上書きする
            foreach (var frame in new[] { FadeFrame.Main, FadeFrame.AlphaMask, FadeFrame.Third, FadeFrame.Second })
            {
                var cutoff = TransparencyPresets.For(frame).Single(p => p.Name == "_Cutoff");
                Assert.That(cutoff.FloatValue, Is.EqualTo(0.001f), $"frame={frame}");
                Assert.That(cutoff.Type, Is.EqualTo(PresetPropertyType.Range), $"frame={frame}");
            }
        }

        [Test]
        public void For_Main_CommonOnly()
        {
            var props = TransparencyPresets.For(FadeFrame.Main);
            Assert.That(props.First(p => p.Name == "_DstBlend").FloatValue, Is.EqualTo(10));
            Assert.That(props.Any(p => p.Name == "_UseMain3rdTex"), Is.False);
            Assert.That(props.Any(p => p.Name == "_UseMain2ndTex"), Is.False);
            Assert.That(props.Any(p => p.Name == "_AlphaMaskMode"), Is.False);
        }

        [Test]
        public void DriverProps_Main_Empty()
        {
            var props = TransparencyPresets.DriverProps(FadeFrame.Main);
            Assert.That(props.Count, Is.EqualTo(0));
        }

        [Test]
        public void DriverProps_Third_ThreeProps()
        {
            var props = TransparencyPresets.DriverProps(FadeFrame.Third);
            Assert.That(props.Count, Is.EqualTo(3));
            Assert.That(props.First(p => p.Name == "_UseMain3rdTex").FloatValue, Is.EqualTo(1));
            Assert.That(props.First(p => p.Name == "_Main3rdTexBlendMode").FloatValue, Is.EqualTo(3));
            Assert.That(props.First(p => p.Name == "_Main3rdTexAlphaMode").FloatValue, Is.EqualTo(2));
            Assert.That(props.Any(p => p.Name == "_DstBlend"), Is.False);
        }

        [Test]
        public void DriverProps_AlphaMask()
        {
            var props = TransparencyPresets.DriverProps(FadeFrame.AlphaMask);
            Assert.That(props.First(p => p.Name == "_AlphaMaskMode").FloatValue, Is.EqualTo(2));
            Assert.That(props.First(p => p.Name == "_AlphaMaskScale").FloatValue, Is.EqualTo(1));
            Assert.That(props.First(p => p.Name == "_AlphaMaskValue").FloatValue, Is.EqualTo(0));
        }

        [Test]
        public void OneTwoTransProps_AlwaysNeutralizesCutoff()
        {
            // onetrans/twotrans は透過シェーダー自身の _Cutoff（既定0.5）が FORWARD パスの clip で生きており、
            // フェードでαがしきい値を割った瞬間にメッシュ全体が消える（0まで滑らかに到達しない）ため、
            // For() と同様に全枠で _Cutoff を 0.001 に上書きする
            foreach (var frame in new[] { FadeFrame.Main, FadeFrame.AlphaMask, FadeFrame.Third, FadeFrame.Second })
            {
                var cutoff = TransparencyPresets.OneTwoTransProps(frame, false).Single(p => p.Name == "_Cutoff");
                Assert.That(cutoff.FloatValue, Is.EqualTo(0.001f), $"frame={frame}");
                Assert.That(cutoff.Type, Is.EqualTo(PresetPropertyType.Range), $"frame={frame}");
            }
        }

        [Test]
        public void OneTwoTransProps_TwoPass_NeutralizesPreCutoff()
        {
            // twotrans は FORWARD_BACK (Pre) パスの _PreCutoff（既定0.5）でも clip されるため同様に無効化する。
            // onetrans は FORWARD_BACK を持たないため対象外
            var pre = TransparencyPresets.OneTwoTransProps(FadeFrame.Main, true).Single(p => p.Name == "_PreCutoff");
            Assert.That(pre.FloatValue, Is.EqualTo(0.001f));
            Assert.That(pre.Type, Is.EqualTo(PresetPropertyType.Range));
            Assert.That(TransparencyPresets.OneTwoTransProps(FadeFrame.Main, false).Any(p => p.Name == "_PreCutoff"), Is.False);
        }

        [Test]
        public void OneTwoTransProps_Main_CutoffOnly()
        {
            // Main 枠は駆動プロパティ不要だが、クリップ無効化のため AO ME 自体は必要（スキップしない）
            var props = TransparencyPresets.OneTwoTransProps(FadeFrame.Main, false);
            Assert.That(props.Count, Is.EqualTo(1));
            Assert.That(props[0].Name, Is.EqualTo("_Cutoff"));
        }

        [Test]
        public void OneTwoTransProps_Third_ContainsDriverButNoBlendSettings()
        {
            // ブレンド設定（既に透過で正しい）には触れず、駆動プロパティ＋クリップ無効化のみ
            var props = TransparencyPresets.OneTwoTransProps(FadeFrame.Third, true);
            Assert.That(props.First(p => p.Name == "_UseMain3rdTex").FloatValue, Is.EqualTo(1));
            Assert.That(props.Any(p => p.Name == "_DstBlend"), Is.False);
            Assert.That(props.Any(p => p.Name == "_SrcBlend"), Is.False);
        }

        [Test]
        public void TransparentModeOverride()
        {
            var p = TransparencyPresets.TransparentModeOverride();
            Assert.That(p.Name, Is.EqualTo("_TransparentMode"));
            Assert.That(p.FloatValue, Is.EqualTo(2));
        }
    }
}
