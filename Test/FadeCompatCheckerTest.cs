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
        public void Default_AllCompatible_RecommendThird()
        {
            var result = FadeCompatChecker.Check(mat);
            Assert.That(result.Third.Compatible, Is.True);
            Assert.That(result.Second.Compatible, Is.True);
            Assert.That(result.AlphaMask.Compatible, Is.True);
            Assert.That(result.Recommended, Is.EqualTo(FadeFrame.Third));
        }

        [Test]
        public void ThirdUsed_RecommendSecond()
        {
            mat.SetFloat("_UseMain3rdTex", 1);
            var result = FadeCompatChecker.Check(mat);
            Assert.That(result.Third.Compatible, Is.False);
            Assert.That(result.Third.NonDefaultProps, Has.Some.Matches<NonDefaultProp>(p => p.Name == "_UseMain3rdTex"));
            Assert.That(result.Recommended, Is.EqualTo(FadeFrame.Second));
        }

        [Test]
        public void ThirdAndSecondUsed_RecommendAlphaMask()
        {
            mat.SetFloat("_UseMain3rdTex", 1);
            mat.SetColor("_Color2nd", new Color(1, 0, 0, 1));
            var result = FadeCompatChecker.Check(mat);
            Assert.That(result.Recommended, Is.EqualTo(FadeFrame.AlphaMask));
        }

        [Test]
        public void AllUsed_RecommendNull()
        {
            mat.SetFloat("_UseMain3rdTex", 1);
            mat.SetFloat("_UseMain2ndTex", 1);
            mat.SetFloat("_AlphaMaskMode", 1);
            var result = FadeCompatChecker.Check(mat);
            Assert.That(result.Recommended, Is.Null);
        }

        [Test]
        public void TextureAssigned_Incompatible()
        {
            var tex = new Texture2D(4, 4);
            try
            {
                mat.SetTexture("_Main3rdTex", tex);
                var result = FadeCompatChecker.Check(mat);
                Assert.That(result.Third.Compatible, Is.False);
            }
            finally
            {
                Object.DestroyImmediate(tex);
            }
        }

        [Test]
        public void NonLilToonMaterial_AllCompatible()
        {
            // 判定対象プロパティを持たないマテリアルは全枠 compatible（HasProperty=false は対象外）
            var std = new Material(Shader.Find("Standard"));
            try
            {
                var result = FadeCompatChecker.Check(std);
                Assert.That(result.Third.Compatible, Is.True);
                Assert.That(result.Recommended, Is.EqualTo(FadeFrame.Third));
            }
            finally
            {
                Object.DestroyImmediate(std);
            }
        }
    }
}
