using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using VRC.SDK3.Avatars.Components;

namespace Narazaka.VRChat.CostumeDashboard.Editor.Test
{
    public class ChooseMenuVariantSetupTest
    {
        const string LtsGuid = "df12117ecd77c31469c224178886498e";

        GameObject avatarRoot;
        GameObject costume;
        GameObject variantRoot;
        Material baseMat;
        Material baseMat2;
        Material redMat;
        Material redMat2;

        [SetUp]
        public void SetUp()
        {
            var shader = AssetDatabase.LoadAssetAtPath<Shader>(AssetDatabase.GUIDToAssetPath(LtsGuid));
            baseMat = new Material(shader);
            baseMat2 = new Material(shader);
            redMat = new Material(shader);
            redMat2 = new Material(shader);

            avatarRoot = new GameObject("Avatar");
            avatarRoot.AddComponent<VRCAvatarDescriptor>();
            costume = new GameObject("Dress");
            costume.transform.SetParent(avatarRoot.transform);
            AddMesh(costume, "Top", baseMat, baseMat2);
            AddMesh(AddChild(costume, "Sub"), "Skirt", baseMat);

            variantRoot = new GameObject("Dress_Red");
            AddMesh(variantRoot, "Top", redMat, redMat2);
            AddMesh(AddChild(variantRoot, "Sub"), "Skirt", redMat);
        }

        [TearDown]
        public void TearDown()
        {
            if (avatarRoot != null) Object.DestroyImmediate(avatarRoot);
            if (variantRoot != null) Object.DestroyImmediate(variantRoot);
            Object.DestroyImmediate(baseMat);
            Object.DestroyImmediate(baseMat2);
            Object.DestroyImmediate(redMat);
            Object.DestroyImmediate(redMat2);
        }

        static GameObject AddChild(GameObject parent, string name)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent.transform);
            return go;
        }

        static GameObject AddMesh(GameObject parent, string name, params Material[] mats)
        {
            var go = AddChild(parent, name);
            go.AddComponent<SkinnedMeshRenderer>().sharedMaterials = mats;
            return go;
        }

        net.narazaka.avatarmenucreator.AvatarChooseMenu CreateMenu(int chooseCount = 3) =>
            ChooseMenuSetup.Create(avatarRoot, MaterialSlotScanner.Scan(costume), chooseCount).AvatarChooseMenu;

        [Test]
        public void ApplyVariant_FillsMatchingPathsBySlot()
        {
            var menu = CreateMenu();
            var result = ChooseMenuVariantSetup.ApplyVariant(menu, avatarRoot, costume, variantRoot, 1);
            Assert.That(result.Applied, Is.EqualTo(3));
            Assert.That(result.Missing, Is.Empty);
            Assert.That(menu.ChooseMaterials[("Dress/Top", 0)][1], Is.EqualTo(redMat));
            Assert.That(menu.ChooseMaterials[("Dress/Top", 1)][1], Is.EqualTo(redMat2));
            Assert.That(menu.ChooseMaterials[("Dress/Sub/Skirt", 0)][1], Is.EqualTo(redMat));
            // 選択肢0（元）は保持される
            Assert.That(menu.ChooseMaterials[("Dress/Top", 0)][0], Is.EqualTo(baseMat));
        }

        [Test]
        public void ApplyVariant_MissingObject_SkipsAndReports()
        {
            var menu = CreateMenu();
            Object.DestroyImmediate(variantRoot.transform.Find("Top").gameObject);
            var result = ChooseMenuVariantSetup.ApplyVariant(menu, avatarRoot, costume, variantRoot, 1);
            Assert.That(result.Applied, Is.EqualTo(1));
            Assert.That(result.Missing.Count, Is.EqualTo(2));
            Assert.That(menu.ChooseMaterials[("Dress/Top", 0)].ContainsKey(1), Is.False, "対応不可のスロットは未設定のまま");
            Assert.That(menu.ChooseMaterials[("Dress/Sub/Skirt", 0)][1], Is.EqualTo(redMat));
        }

        [Test]
        public void ApplyVariant_SlotIndexOutOfRange_SkipsAndReports()
        {
            var menu = CreateMenu();
            variantRoot.transform.Find("Top").GetComponent<SkinnedMeshRenderer>().sharedMaterials = new[] { redMat };
            var result = ChooseMenuVariantSetup.ApplyVariant(menu, avatarRoot, costume, variantRoot, 1);
            Assert.That(result.Applied, Is.EqualTo(2));
            Assert.That(result.Missing.Count, Is.EqualTo(1));
            Assert.That(result.Missing[0].SlotIndex, Is.EqualTo(1));
            Assert.That(menu.ChooseMaterials[("Dress/Top", 1)].ContainsKey(1), Is.False);
        }

        [Test]
        public void ApplyVariant_NoRenderer_SkipsAndReports()
        {
            var menu = CreateMenu();
            Object.DestroyImmediate(variantRoot.transform.Find("Top").GetComponent<SkinnedMeshRenderer>());
            var result = ChooseMenuVariantSetup.ApplyVariant(menu, avatarRoot, costume, variantRoot, 1);
            Assert.That(result.Applied, Is.EqualTo(1));
            Assert.That(result.Missing.Count, Is.EqualTo(2));
            Assert.That(result.Missing[0].Reason, Does.Contain("Renderer"));
        }

        [Test]
        public void ApplyVariant_NullMaterialInVariant_SkipsAndReports()
        {
            var menu = CreateMenu();
            variantRoot.transform.Find("Top").GetComponent<SkinnedMeshRenderer>().sharedMaterials = new Material[] { null, redMat2 };
            var result = ChooseMenuVariantSetup.ApplyVariant(menu, avatarRoot, costume, variantRoot, 1);
            Assert.That(result.Applied, Is.EqualTo(2));
            Assert.That(result.Missing.Count, Is.EqualTo(1));
            Assert.That(menu.ChooseMaterials[("Dress/Top", 0)].ContainsKey(1), Is.False);
        }

        [Test]
        public void ApplyVariant_IgnoresKeysOfOtherCostumes()
        {
            // 別衣装のキーも含むメニューに対し、指定した衣装配下のキーだけを対象にする
            var other = new GameObject("Other");
            other.transform.SetParent(avatarRoot.transform);
            AddMesh(other, "Cape", baseMat);
            try
            {
                var creator = ChooseMenuSetup.Create(avatarRoot, MaterialSlotScanner.Scan(avatarRoot), 3);
                var menu = creator.AvatarChooseMenu;
                var result = ChooseMenuVariantSetup.ApplyVariant(menu, avatarRoot, costume, variantRoot, 1);
                Assert.That(result.Applied, Is.EqualTo(3));
                Assert.That(result.Missing, Is.Empty, "他衣装のキーは Missing に数えない");
                Assert.That(menu.ChooseMaterials[("Other/Cape", 0)].ContainsKey(1), Is.False);
            }
            finally
            {
                Object.DestroyImmediate(other);
            }
        }

        [Test]
        public void ApplyVariant_CostumeRootIsAvatarRoot()
        {
            var creator = ChooseMenuSetup.Create(avatarRoot, MaterialSlotScanner.Scan(avatarRoot), 3);
            var menu = creator.AvatarChooseMenu;
            // 衣装ルート == アバタールート のときは色違い側も同じ階層（Dress/...）を持つ必要がある
            var wrapper = new GameObject("VariantAvatar");
            try
            {
                variantRoot.name = "Dress";
                variantRoot.transform.SetParent(wrapper.transform);
                var result = ChooseMenuVariantSetup.ApplyVariant(menu, avatarRoot, avatarRoot, wrapper, 1);
                Assert.That(result.Applied, Is.EqualTo(3));
                Assert.That(menu.ChooseMaterials[("Dress/Top", 0)][1], Is.EqualTo(redMat));
            }
            finally
            {
                variantRoot.transform.SetParent(null);
                Object.DestroyImmediate(wrapper);
            }
        }

        [Test]
        public void ApplyVariant_SecondVariantIndexIsIndependent()
        {
            var menu = CreateMenu(4);
            ChooseMenuVariantSetup.ApplyVariant(menu, avatarRoot, costume, variantRoot, 1);
            variantRoot.transform.Find("Top").GetComponent<SkinnedMeshRenderer>().sharedMaterials = new[] { baseMat2, redMat2 };
            ChooseMenuVariantSetup.ApplyVariant(menu, avatarRoot, costume, variantRoot, 2);
            Assert.That(menu.ChooseMaterials[("Dress/Top", 0)][1], Is.EqualTo(redMat));
            Assert.That(menu.ChooseMaterials[("Dress/Top", 0)][2], Is.EqualTo(baseMat2));
        }

        [Test]
        public void SetChooseName_SetsAndIgnoresEmpty()
        {
            var menu = CreateMenu();
            ChooseMenuVariantSetup.SetChooseName(menu, 1, "赤");
            ChooseMenuVariantSetup.SetChooseName(menu, 2, "");
            Assert.That(menu.ChooseNames[1], Is.EqualTo("赤"));
            Assert.That(menu.ChooseNames.ContainsKey(2), Is.False);
        }

        [TestCase("Dress", "Dress/Top", true, "Top")]
        [TestCase("Dress", "Dress", true, "")]
        [TestCase("Dress", "Other/Cape", false, null)]
        [TestCase("Dress", "DressLong/Top", false, null)]
        [TestCase("", "Dress/Top", true, "Dress/Top")]
        public void TryToCostumeRelative_Cases(string costumePath, string avatarPath, bool expected, string expectedRelative)
        {
            var ok = ChooseMenuVariantSetup.TryToCostumeRelative(costumePath, avatarPath, out var relative);
            Assert.That(ok, Is.EqualTo(expected));
            if (expected) Assert.That(relative, Is.EqualTo(expectedRelative));
        }
    }
}
