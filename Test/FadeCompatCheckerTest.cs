using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Narazaka.VRChat.CostumeDashboard.Editor.Test
{
    public class FadeCompatCheckerTest
    {
        const string LtsGuid = "df12117ecd77c31469c224178886498e";
        const string LtsTransGuid = "165365ab7100a044ca85fc8c33548a62";
        const string LtsCutoutGuid = "85d6126cae43b6847aff4b13f4adb8ec";
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
            // 不透明 lts のままだと AlphaMask 緩和で Compatible になってしまうため、
            // AlphaMask が genuinely 使用済み(=不可)になる透過シェーダーに差し替える
            mat.shader = AssetDatabase.LoadAssetAtPath<Shader>(AssetDatabase.GUIDToAssetPath(LtsTransGuid));
            mat.SetColor("_Color", new Color(1f, 0.5f, 0.5f, 1f));
            mat.SetFloat("_AlphaMaskMode", 1);
            var result = FadeCompatChecker.Check(mat);
            Assert.That(result.AlphaMask.Compatible, Is.False);
            Assert.That(result.AlphaMask.Warning, Is.False);
            Assert.That(result.AlphaMask.ShortReason, Is.Not.Null.And.Not.Empty);
            Assert.That(result.Recommended, Is.EqualTo(FadeFrame.Third));
        }

        [Test]
        public void MainAlphaThirdUsed_RecommendSecond()
        {
            mat.shader = AssetDatabase.LoadAssetAtPath<Shader>(AssetDatabase.GUIDToAssetPath(LtsTransGuid));
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
            mat.shader = AssetDatabase.LoadAssetAtPath<Shader>(AssetDatabase.GUIDToAssetPath(LtsTransGuid));
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
        public void TextureAssignedButGateOff_CompatibleWithWarning_ReasonMentionsTexture()
        {
            // ゲート(_UseMain3rdTex)を有効化しないままテクスチャだけ割り当てても出力には効かないため、
            // 新仕様では警告付き利用可（Compatible=true）になる
            var tex = new Texture2D(4, 4);
            try
            {
                mat.SetTexture("_Main3rdTex", tex);
                var result = FadeCompatChecker.Check(mat);
                Assert.That(result.Third.Compatible, Is.True);
                Assert.That(result.Third.Warning, Is.True);
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

        [Test]
        public void ThirdValuesLeftButGateOff_CompatibleWithWarning()
        {
            mat.SetFloat("_Main3rdTexBlendMode", 3); // ゲート(_UseMain3rdTex)は0のまま
            var result = FadeCompatChecker.Check(mat);
            Assert.That(result.Third.Compatible, Is.True);
            Assert.That(result.Third.Warning, Is.True);
            Assert.That(result.Third.ShortReason, Does.Contain("_Main3rdTexBlendMode"));
        }

        [Test]
        public void AlphaMaskValuesLeftButModeOff_CompatibleWithWarning()
        {
            mat.SetFloat("_AlphaMaskValue", 0.5f); // _AlphaMaskMode は 0 のまま
            var result = FadeCompatChecker.Check(mat);
            Assert.That(result.AlphaMask.Compatible, Is.True);
            Assert.That(result.AlphaMask.Warning, Is.True);
        }

        [Test]
        public void AlphaMaskModeOn_OpaqueShader_CompatibleWithWarning()
        {
            // lts (opaque) なので AlphaMask は出力に効かない
            mat.SetFloat("_AlphaMaskMode", 2);
            var result = FadeCompatChecker.Check(mat);
            Assert.That(result.AlphaMask.Compatible, Is.True);
            Assert.That(result.AlphaMask.Warning, Is.True);
        }

        [Test]
        public void AlphaMaskModeOn_TransShader_Incompatible()
        {
            var transShader = AssetDatabase.LoadAssetAtPath<Shader>(AssetDatabase.GUIDToAssetPath(LtsTransGuid));
            var transMat = new Material(transShader);
            try
            {
                transMat.SetFloat("_AlphaMaskMode", 2);
                var result = FadeCompatChecker.Check(transMat);
                Assert.That(result.AlphaMask.Compatible, Is.False);
                Assert.That(result.AlphaMask.Warning, Is.False);
            }
            finally
            {
                Object.DestroyImmediate(transMat);
            }
        }

        [Test]
        public void AlphaMaskModeOn_CutoutShader_Incompatible()
        {
            // lilToon はアルファマスクを LIL_RENDER != 0 (Cutout / Transparent) で適用するため、
            // cutout は緩和対象外(=マスクが効くので使用済みのまま)
            var cutoutShader = AssetDatabase.LoadAssetAtPath<Shader>(AssetDatabase.GUIDToAssetPath(LtsCutoutGuid));
            var cutoutMat = new Material(cutoutShader);
            try
            {
                cutoutMat.SetFloat("_AlphaMaskMode", 2);
                var result = FadeCompatChecker.Check(cutoutMat);
                Assert.That(result.AlphaMask.Compatible, Is.False);
                Assert.That(result.AlphaMask.Warning, Is.False);
            }
            finally
            {
                Object.DestroyImmediate(cutoutMat);
            }
        }

        [Test]
        public void GateOn_Incompatible_NoWarningFlag()
        {
            mat.SetFloat("_UseMain3rdTex", 1);
            var result = FadeCompatChecker.Check(mat);
            Assert.That(result.Third.Compatible, Is.False);
            Assert.That(result.Third.Warning, Is.False);
        }

        [Test]
        public void CleanFrame_NoWarning()
        {
            var result = FadeCompatChecker.Check(mat);
            Assert.That(result.Third.Warning, Is.False);
            Assert.That(result.Third.ShortReason, Is.Null);
        }

        [Test]
        public void AlphaMaskReplace_NoMainTexAlpha_ColorFadesToMultiplyNoWarning()
        {
            mat.shader = AssetDatabase.LoadAssetAtPath<Shader>(AssetDatabase.GUIDToAssetPath(LtsTransGuid));
            mat.SetFloat("_AlphaMaskMode", 1);
            var result = FadeCompatChecker.Check(mat);
            Assert.That(result.ColorFadeImpact.Adjust, Is.EqualTo(AlphaMaskAdjust.ToMultiply));
            Assert.That(result.Main.Compatible, Is.True);
            Assert.That(result.Main.Warning, Is.False);
        }

        [Test]
        public void AlphaMaskReplace_MainTexWithAlpha_ColorFadesWarn()
        {
            mat.shader = AssetDatabase.LoadAssetAtPath<Shader>(AssetDatabase.GUIDToAssetPath(LtsTransGuid));
            mat.SetFloat("_AlphaMaskMode", 1);
            var tex = new Texture2D(4, 4, TextureFormat.RGBA32, false);
            try
            {
                mat.SetTexture("_MainTex", tex);
                var result = FadeCompatChecker.Check(mat);
                Assert.That(result.Main.Warning, Is.True);
                Assert.That(result.Third.Warning, Is.True);
                Assert.That(result.Main.ShortReason, Does.Contain("乗算"));
            }
            finally
            {
                Object.DestroyImmediate(tex);
            }
        }

        [Test]
        public void AlphaMaskSpecialMode_ColorFadesBlocked()
        {
            mat.shader = AssetDatabase.LoadAssetAtPath<Shader>(AssetDatabase.GUIDToAssetPath(LtsTransGuid));
            mat.SetFloat("_AlphaMaskMode", 3);
            var result = FadeCompatChecker.Check(mat);
            Assert.That(result.Main.Compatible, Is.False);
            Assert.That(result.Third.Compatible, Is.False);
            Assert.That(result.Second.Compatible, Is.False);
        }

        [Test]
        public void AlphaMaskResidual_OnOpaque_NeutralizeNoFrameEffect()
        {
            mat.SetFloat("_AlphaMaskMode", 1);
            var result = FadeCompatChecker.Check(mat);
            Assert.That(result.ColorFadeImpact.Adjust, Is.EqualTo(AlphaMaskAdjust.Neutralize));
            Assert.That(result.Main.Compatible, Is.True);
            Assert.That(result.Main.Warning, Is.False);
        }

        [Test]
        public void AlphaMaskMultiply_NoEffect()
        {
            mat.shader = AssetDatabase.LoadAssetAtPath<Shader>(AssetDatabase.GUIDToAssetPath(LtsTransGuid));
            mat.SetFloat("_AlphaMaskMode", 2);
            var result = FadeCompatChecker.Check(mat);
            Assert.That(result.ColorFadeImpact.Adjust, Is.EqualTo(AlphaMaskAdjust.None));
            Assert.That(result.Main.Compatible, Is.True);
            Assert.That(result.Main.Warning, Is.False);
        }

        [Test]
        public void BlockedFrame_KeepsOwnIncompatibleReason()
        {
            mat.shader = AssetDatabase.LoadAssetAtPath<Shader>(AssetDatabase.GUIDToAssetPath(LtsTransGuid));
            mat.SetColor("_Color", new Color(1f, 0.5f, 0.5f, 1f));
            mat.SetFloat("_AlphaMaskMode", 3);
            var result = FadeCompatChecker.Check(mat);
            Assert.That(result.Main.Compatible, Is.False);
            Assert.That(result.Main.ShortReason, Does.Contain("白のみ可"));
            Assert.That(result.Main.ShortReason, Does.Not.Contain("特殊モード"));
            Assert.That(result.Third.ShortReason, Does.Contain("特殊モード"));
        }

        [Test]
        public void WarningReasons_Concatenated()
        {
            mat.shader = AssetDatabase.LoadAssetAtPath<Shader>(AssetDatabase.GUIDToAssetPath(LtsTransGuid));
            mat.SetFloat("_Main3rdTexBlendMode", 3); // ゲート(_UseMain3rdTex)は0のまま残存
            mat.SetFloat("_AlphaMaskMode", 1);
            var tex = new Texture2D(4, 4, TextureFormat.RGBA32, false);
            try
            {
                mat.SetTexture("_MainTex", tex);
                var result = FadeCompatChecker.Check(mat);
                Assert.That(result.Third.Warning, Is.True);
                Assert.That(result.Third.ShortReason, Does.Contain("未使用の設定値あり"));
                Assert.That(result.Third.ShortReason, Does.Contain("乗算に変換"));
                Assert.That(result.Third.ShortReason, Does.Contain("; "));
            }
            finally
            {
                Object.DestroyImmediate(tex);
            }
        }
    }
}
