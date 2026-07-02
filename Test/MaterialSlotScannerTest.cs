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
            Assert.That(opaqueGroup.Preset, Is.EqualTo(FadeFrame.Third));
            Assert.That(opaqueGroup.CanSetupFade, Is.True);
        }

        [Test]
        public void GroupByShader_SplitsByRecommendedPreset()
        {
            var thirdUsed = new Material(ltsMat);
            thirdUsed.SetFloat("_UseMain3rdTex", 1);
            try
            {
                AddMesh("Top", ltsMat);
                AddMesh("Skirt", thirdUsed);
                var groups = MaterialSlotScanner.GroupByShader(MaterialSlotScanner.Scan(root));
                Assert.That(groups.Count, Is.EqualTo(2));
                Assert.That(groups.Any(g => g.Preset == FadeFrame.Third), Is.True);
                Assert.That(groups.Any(g => g.Preset == FadeFrame.Second), Is.True);
            }
            finally
            {
                Object.DestroyImmediate(thirdUsed);
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
    }
}
