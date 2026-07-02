using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Narazaka.VRChat.CostumeDashboard.Editor.Test
{
    public class FadeCompatCheckerTest
    {
        const string LtsGuid = "df12117ecd77c31469c224178886498e";
        Material mat;

        [SetUp]
        public void SetUp()
        {
            var shader = AssetDatabase.LoadAssetAtPath<Shader>(AssetDatabase.GUIDToAssetPath(LtsGuid));
            Assert.That(shader, Is.Not.Null, "lilToon (lts.shader) が見つからない");
            mat = new Material(shader);
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(mat);
        }

        [Test]
        public void Default_AllCompatible_RecommendMain()
        {
            var result = FadeCompatChecker.Check(mat);
            Assert.That(result.Main.Compatible, Is.True);
            Assert.That(result.Third.Compatible, Is.True);
            Assert.That(result.Second.Compatible, Is.True);
            Assert.That(result.AlphaMask.Compatible, Is.True);
            Assert.That(result.Recommended, Is.EqualTo(FadeFrame.Main));
            Assert.That(result.Main.ShortReason, Is.Null);
        }

        [Test]
        public void MainColored_RecommendAlphaMask()
        {
            mat.SetColor("_Color", new Color(1f, 0.5f, 0.5f, 1f));
            var result = FadeCompatChecker.Check(mat);
            Assert.That(result.Main.Compatible, Is.False);
            Assert.That(result.Main.ShortReason, Does.Contain("_Color"));
            Assert.That(result.Main.ShortReason, Does.Contain("白のみ可"));
            Assert.That(result.Recommended, Is.EqualTo(FadeFrame.AlphaMask));
        }

        [Test]
        public void MainAndAlphaMaskUsed_RecommendThird()
        {
            mat.SetColor("_Color", new Color(1f, 0.5f, 0.5f, 1f));
            mat.SetFloat("_AlphaMaskMode", 1);
            var result = FadeCompatChecker.Check(mat);
            Assert.That(result.AlphaMask.ShortReason, Is.Not.Null.And.Not.Empty);
            Assert.That(result.Recommended, Is.EqualTo(FadeFrame.Third));
        }

        [Test]
        public void MainAlphaThirdUsed_RecommendSecond()
        {
            mat.SetColor("_Color", new Color(1f, 0.5f, 0.5f, 1f));
            mat.SetFloat("_AlphaMaskMode", 1);
            mat.SetFloat("_UseMain3rdTex", 1);
            var result = FadeCompatChecker.Check(mat);
            Assert.That(result.Third.ShortReason, Does.Contain("3rd"));
            Assert.That(result.Recommended, Is.EqualTo(FadeFrame.Second));
        }

        [Test]
        public void AllUsed_RecommendNull()
        {
            mat.SetColor("_Color", new Color(1f, 0.5f, 0.5f, 1f));
            mat.SetFloat("_AlphaMaskMode", 1);
            mat.SetFloat("_UseMain3rdTex", 1);
            mat.SetFloat("_UseMain2ndTex", 1);
            var result = FadeCompatChecker.Check(mat);
            Assert.That(result.Recommended, Is.Null);
        }

        [Test]
        public void ThirdUsed_ButMainFree_RecommendMain()
        {
            mat.SetFloat("_UseMain3rdTex", 1);
            var result = FadeCompatChecker.Check(mat);
            Assert.That(result.Third.Compatible, Is.False);
            Assert.That(result.Recommended, Is.EqualTo(FadeFrame.Main));
        }

        [Test]
        public void TextureAssigned_Incompatible_ReasonMentionsTexture()
        {
            var tex = new Texture2D(4, 4);
            try
            {
                mat.SetTexture("_Main3rdTex", tex);
                var result = FadeCompatChecker.Check(mat);
                Assert.That(result.Third.Compatible, Is.False);
                Assert.That(result.Third.ShortReason, Does.Contain("_Main3rdTex"));
            }
            finally
            {
                Object.DestroyImmediate(tex);
            }
        }

        [Test]
        public void NonLilToonMaterial_AllCompatible()
        {
            var std = new Material(Shader.Find("Standard"));
            try
            {
                var result = FadeCompatChecker.Check(std);
                Assert.That(result.Main.Compatible, Is.True); // Standard の _Color は白がデフォルト
                Assert.That(result.Recommended, Is.EqualTo(FadeFrame.Main));
            }
            finally
            {
                Object.DestroyImmediate(std);
            }
        }
    }
}
