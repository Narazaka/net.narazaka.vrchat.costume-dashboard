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
        public void TransparentModeOverride()
        {
            var p = TransparencyPresets.TransparentModeOverride();
            Assert.That(p.Name, Is.EqualTo("_TransparentMode"));
            Assert.That(p.FloatValue, Is.EqualTo(2));
        }
    }
}
