using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Narazaka.VRChat.CostumeDashboard.Editor.Test
{
    public class MaterialSlotScannerTest
    {
        const string LtsGuid = "df12117ecd77c31469c224178886498e";
        const string LtsCutoutOGuid = "3b4aa19949601f046a20ca8bdaee929f";
        const string LtsTransGuid = "165365ab7100a044ca85fc8c33548a62";

        GameObject root;
        Material ltsMat;
        Material cutoutOMat;
        Material unknownMat;

        [SetUp]
        public void SetUp()
        {
            root = new GameObject("Costume");
            ltsMat = new Material(AssetDatabase.LoadAssetAtPath<Shader>(AssetDatabase.GUIDToAssetPath(LtsGuid)));
            cutoutOMat = new Material(AssetDatabase.LoadAssetAtPath<Shader>(AssetDatabase.GUIDToAssetPath(LtsCutoutOGuid)));
            unknownMat = new Material(Shader.Find("Standard"));
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(root);
            Object.DestroyImmediate(ltsMat);
            Object.DestroyImmediate(cutoutOMat);
            Object.DestroyImmediate(unknownMat);
        }

        GameObject AddMesh(string name, params Material[] mats)
        {
            var go = new GameObject(name);
            go.transform.SetParent(root.transform);
            var smr = go.AddComponent<SkinnedMeshRenderer>();
            smr.sharedMaterials = mats;
            return go;
        }

        [Test]
        public void Scan_CollectsAllSlots()
        {
            AddMesh("Top", ltsMat, unknownMat);
            AddMesh("Skirt", cutoutOMat);
            var slots = MaterialSlotScanner.Scan(root);
            Assert.That(slots.Count, Is.EqualTo(3));
            var top0 = slots.First(s => s.Renderer.name == "Top" && s.SlotIndex == 0);
            Assert.That(top0.Family.Family, Is.EqualTo("lilToon_std"));
            Assert.That(top0.Family.Variant, Is.EqualTo("opaque"));
            Assert.That(top0.FadeCompat, Is.Not.Null);
            var top1 = slots.First(s => s.Renderer.name == "Top" && s.SlotIndex == 1);
            Assert.That(top1.Family.IsKnown, Is.False);
            Assert.That(top1.FadeCompat, Is.Null);
        }

        [Test]
        public void Scan_IncludesInactive()
        {
            var mesh = AddMesh("Hidden", ltsMat);
            mesh.SetActive(false);
            Assert.That(MaterialSlotScanner.Scan(root).Count, Is.EqualTo(1));
        }

        [Test]
        public void Scan_ExcludesEditorOnly()
        {
            var mesh = AddMesh("DebugMesh", ltsMat);
            mesh.tag = "EditorOnly";
            Assert.That(MaterialSlotScanner.Scan(root).Count, Is.EqualTo(0));
        }

        [Test]
        public void Scan_ExcludesUnderEditorOnlyParent()
        {
            var container = new GameObject("EditorOnlyContainer");
            container.transform.SetParent(root.transform);
            container.tag = "EditorOnly";
            var go = new GameObject("Mesh");
            go.transform.SetParent(container.transform);
            go.AddComponent<SkinnedMeshRenderer>().sharedMaterials = new[] { ltsMat };
            Assert.That(MaterialSlotScanner.Scan(root).Count, Is.EqualTo(0));
        }

        GameObject AddParticle(string name, params Material[] mats)
        {
            var go = new GameObject(name);
            go.transform.SetParent(root.transform);
            go.AddComponent<ParticleSystem>();
            go.GetComponent<ParticleSystemRenderer>().sharedMaterials = mats;
            return go;
        }

        [Test]
        public void Scan_IncludesNonMeshRenderers_WithoutFadeSupport()
        {
            AddMesh("Top", ltsMat);
            AddParticle("Effect", ltsMat);
            var slots = MaterialSlotScanner.Scan(root);
            var mesh = slots.First(s => s.Renderer is SkinnedMeshRenderer);
            Assert.That(mesh.SupportsFade, Is.True);
            Assert.That(mesh.FadeCompat, Is.Not.Null);
            var particle = slots.First(s => s.Renderer is ParticleSystemRenderer);
            Assert.That(particle.SupportsFade, Is.False);
            Assert.That(particle.FadeCompat, Is.Null, "フェード非対応 Renderer は FadeCompat を計算しない");
            Assert.That(particle.Material, Is.EqualTo(ltsMat));
        }

        [Test]
        public void Scan_IncludesMeshRenderer()
        {
            var go = new GameObject("Prop");
            go.transform.SetParent(root.transform);
            go.AddComponent<MeshRenderer>().sharedMaterials = new[] { ltsMat };
            var slots = MaterialSlotScanner.Scan(root);
            Assert.That(slots.Count, Is.EqualTo(1));
            Assert.That(slots[0].SupportsFade, Is.True);
        }

        [Test]
        public void GroupByShader_NonMeshRenderer_SplitsAndCannotSetupFade()
        {
            AddMesh("Top", ltsMat);
            AddParticle("Effect", ltsMat);
            var groups = MaterialSlotScanner.GroupByShader(MaterialSlotScanner.Scan(root));
            // ParticleSystemRenderer は trail 用スロットを持つ場合があるためグループ数は固定しない
            var fadeGroups = groups.Where(g => g.SupportsFade).ToList();
            var particleGroups = groups.Where(g => !g.SupportsFade).ToList();
            Assert.That(fadeGroups.Count, Is.EqualTo(1), "同一シェーダーでも Renderer 種別で別グループになる");
            Assert.That(fadeGroups[0].CanSetupFade, Is.True);
            Assert.That(particleGroups.Count, Is.GreaterThanOrEqualTo(1));
            Assert.That(particleGroups.All(g => !g.CanSetupFade), Is.True);
            Assert.That(particleGroups.All(g => g.FadeDisabledReason == "メッシュ以外のRenderer"), Is.True);
            Assert.That(particleGroups.All(g => !g.IsOneTwoTrans), Is.True);
        }

        [Test]
        public void GroupByShader_GroupsByFamilyVariant()
        {
            AddMesh("Top", ltsMat);
            AddMesh("Skirt", ltsMat);
            AddMesh("Ribbon", cutoutOMat);
            var groups = MaterialSlotScanner.GroupByShader(MaterialSlotScanner.Scan(root));
            Assert.That(groups.Count, Is.EqualTo(2));
            var opaqueGroup = groups.First(g => g.Variant == "opaque");
            Assert.That(opaqueGroup.Slots.Count, Is.EqualTo(2));
            Assert.That(opaqueGroup.NeedsShaderOverride, Is.True);
            Assert.That(opaqueGroup.Preset, Is.EqualTo(FadeFrame.Main));
            Assert.That(opaqueGroup.CanSetupFade, Is.True);
        }

        [Test]
        public void GroupByShader_SplitsByRecommendedPreset()
        {
            var mainUsed = new Material(ltsMat);
            mainUsed.SetColor("_Color", new Color(1f, 0.5f, 0.5f, 1f));
            try
            {
                AddMesh("Top", ltsMat);
                AddMesh("Skirt", mainUsed);
                var groups = MaterialSlotScanner.GroupByShader(MaterialSlotScanner.Scan(root));
                Assert.That(groups.Count, Is.EqualTo(2));
                Assert.That(groups.Any(g => g.Preset == FadeFrame.Main), Is.True);
                Assert.That(groups.Any(g => g.Preset == FadeFrame.AlphaMask), Is.True);
            }
            finally
            {
                Object.DestroyImmediate(mainUsed);
            }
        }

        [Test]
        public void GroupByShader_UnknownShader_CannotSetupFade()
        {
            AddMesh("Prop", unknownMat);
            var groups = MaterialSlotScanner.GroupByShader(MaterialSlotScanner.Scan(root));
            Assert.That(groups.Count, Is.EqualTo(1));
            Assert.That(groups[0].CanSetupFade, Is.False);
            Assert.That(groups[0].FadeDisabledReason, Is.Not.Empty);
        }

        [Test]
        public void GroupByShader_NullMaterial_CannotSetupFade()
        {
            AddMesh("Broken", new Material[] { null });
            var groups = MaterialSlotScanner.GroupByShader(MaterialSlotScanner.Scan(root));
            Assert.That(groups.Count, Is.EqualTo(1));
            Assert.That(groups[0].CanSetupFade, Is.False);
        }

        [Test]
        public void GroupByShader_MultiTransparentModeMixed_SplitsIntoSeparateGroups()
        {
            const string LtsMultiGuid = "9294844b15dca184d914a632279b24e1";
            var shader = AssetDatabase.LoadAssetAtPath<Shader>(AssetDatabase.GUIDToAssetPath(LtsMultiGuid));
            Assert.That(shader, Is.Not.Null, "lilToon Multi (ltsmulti.shader) が見つからない");
            var normalMat = new Material(shader);
            var gemMat = new Material(shader);
            Assume.That(normalMat.HasProperty("_TransparentMode"), "_TransparentMode プロパティが無い");
            normalMat.SetFloat("_TransparentMode", 0);
            gemMat.SetFloat("_TransparentMode", 6);
            try
            {
                AddMesh("Body", normalMat);
                AddMesh("Gem", gemMat);
                var groups = MaterialSlotScanner.GroupByShader(MaterialSlotScanner.Scan(root));
                Assert.That(groups.Count, Is.EqualTo(2));
                var normalGroup = groups.First(g => g.Slots.Any(s => s.MultiTransparentMode == 0));
                var gemGroup = groups.First(g => g.Slots.Any(s => s.MultiTransparentMode == 6));
                Assert.That(normalGroup, Is.Not.EqualTo(gemGroup));
                Assert.That(normalGroup.CanSetupFade, Is.True);
                Assert.That(gemGroup.CanSetupFade, Is.False);
                Assert.That(gemGroup.FadeDisabledReason, Is.EqualTo("_TransparentMode が Refraction/Fur/Gem 系"));
            }
            finally
            {
                Object.DestroyImmediate(normalMat);
                Object.DestroyImmediate(gemMat);
            }
        }

        [Test]
        public void GroupByShader_SplitsByAlphaMaskAdjust()
        {
            var transShader = AssetDatabase.LoadAssetAtPath<Shader>(AssetDatabase.GUIDToAssetPath(LtsTransGuid));
            Assert.That(transShader, Is.Not.Null, "lilToon Transparent (ltstrans.shader) が見つからない");
            var offMat = new Material(transShader);
            var mulMat = new Material(transShader);
            mulMat.SetFloat("_AlphaMaskMode", 1);
            try
            {
                AddMesh("Top", offMat);
                AddMesh("Skirt", mulMat);
                var groups = MaterialSlotScanner.GroupByShader(MaterialSlotScanner.Scan(root));
                Assert.That(groups.Count, Is.EqualTo(2));
                Assert.That(groups.Select(g => g.Preset).Distinct().Count(), Is.EqualTo(1), "Preset は同一のはず");
                var offGroup = groups.First(g => g.Slots.Any(s => s.Material == offMat));
                var mulGroup = groups.First(g => g.Slots.Any(s => s.Material == mulMat));
                Assert.That(offGroup, Is.Not.EqualTo(mulGroup));
                Assert.That(offGroup.AlphaMaskAdjust, Is.EqualTo(AlphaMaskAdjust.None));
                Assert.That(mulGroup.AlphaMaskAdjust, Is.EqualTo(AlphaMaskAdjust.ToMultiply));
            }
            finally
            {
                Object.DestroyImmediate(offMat);
                Object.DestroyImmediate(mulMat);
            }
        }

        [Test]
        public void GroupByShader_AlphaMaskPreset_MergesAdjustVariants()
        {
            // Preset==AlphaMask のグループは Adjust による AO ME 設定差が無い（AlphaMask 枠自体は
            // 調整override非対象）ため、Adjust 違いでグループを分割してはならない
            var noneMat = new Material(ltsMat);
            noneMat.SetColor("_Color", new Color(1f, 0.5f, 0.5f, 1f));
            noneMat.SetFloat("_AlphaMaskMode", 0);
            var neutralizeMat = new Material(ltsMat);
            neutralizeMat.SetColor("_Color", new Color(1f, 0.5f, 0.5f, 1f));
            neutralizeMat.SetFloat("_AlphaMaskMode", 1);
            try
            {
                AddMesh("Top", noneMat);
                AddMesh("Skirt", neutralizeMat);
                var groups = MaterialSlotScanner.GroupByShader(MaterialSlotScanner.Scan(root));
                Assert.That(groups.Count, Is.EqualTo(1));
                Assert.That(groups[0].Preset, Is.EqualTo(FadeFrame.AlphaMask));
                Assert.That(groups[0].AlphaMaskAdjust, Is.EqualTo(AlphaMaskAdjust.None));
            }
            finally
            {
                Object.DestroyImmediate(noneMat);
                Object.DestroyImmediate(neutralizeMat);
            }
        }
    }
}
