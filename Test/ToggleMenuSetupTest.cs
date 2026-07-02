using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using net.narazaka.avatarmenucreator;

namespace Narazaka.VRChat.CostumeDashboard.Editor.Test
{
    public class ToggleMenuSetupTest
    {
        GameObject host;

        [SetUp]
        public void SetUp()
        {
            host = new GameObject("トップス");
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(host);
        }

        [Test]
        public void Create_SetsToggleObjectsAndFades()
        {
            var creator = ToggleMenuSetup.Create(
                host,
                new[] { "Costume/Top", "Costume/Ribbon" },
                new[]
                {
                    new ToggleMenuSetup.FadeTarget { MeshPath = "Costume/Top", Frame = FadeFrame.Third },
                    new ToggleMenuSetup.FadeTarget { MeshPath = "Costume/Ribbon", Frame = FadeFrame.AlphaMask },
                },
                1f);

            var menu = creator.AvatarToggleMenu;
            Assert.That(menu.TransitionSeconds, Is.EqualTo(1f));
            Assert.That(menu.Saved, Is.True);
            Assert.That(menu.Synced, Is.True);
            Assert.That(menu.ToggleDefaultValue, Is.True);
            Assert.That(menu.ToggleObjects[("Costume/Top")], Is.EqualTo(ToggleType.ON));
            Assert.That(menu.ToggleObjects[("Costume/Ribbon")], Is.EqualTo(ToggleType.ON));

            var vec = menu.ToggleShaderVectorParameters[("Costume/Top", "_Color3rd")];
            Assert.That(vec.Inactive, Is.EqualTo(new Vector4(1, 1, 1, 0)));
            Assert.That(vec.Active, Is.EqualTo(new Vector4(1, 1, 1, 1)));
            Assert.That(vec.TransitionDurationPercent, Is.EqualTo(100f));

            var am = menu.ToggleShaderParameters[("Costume/Ribbon", "_AlphaMaskValue")];
            Assert.That(am.Inactive, Is.EqualTo(-1f));
            Assert.That(am.Active, Is.EqualTo(0f));
        }

        [Test]
        public void Create_SecondFrame_UsesColor2nd()
        {
            var creator = ToggleMenuSetup.Create(
                host,
                new string[0],
                new[] { new ToggleMenuSetup.FadeTarget { MeshPath = "Costume/Top", Frame = FadeFrame.Second } },
                1f);
            Assert.That(creator.AvatarToggleMenu.ToggleShaderVectorParameters.ContainsKey(("Costume/Top", "_Color2nd")), Is.True);
        }

        [Test]
        public void Create_Twice_ReusesComponent()
        {
            var c1 = ToggleMenuSetup.Create(host, new string[0], new ToggleMenuSetup.FadeTarget[0], 1f);
            var c2 = ToggleMenuSetup.Create(host, new string[0], new ToggleMenuSetup.FadeTarget[0], 1f);
            Assert.That(c1, Is.EqualTo(c2));
        }
    }
}
