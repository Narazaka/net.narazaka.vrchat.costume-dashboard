using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using VRC.SDK3.Avatars.Components;

namespace Narazaka.VRChat.CostumeDashboard.Editor.Test
{
    public class ChooseMenuSetupTest : UndoCleanupTestBase
    {
        const string LtsGuid = "df12117ecd77c31469c224178886498e";

        GameObject root;
        Material mat;
        Material mat2;

        [SetUp]
        public void SetUp()
        {
            root = Track(new GameObject("Avatar"));
            var shader = AssetDatabase.LoadAssetAtPath<Shader>(AssetDatabase.GUIDToAssetPath(LtsGuid));
            mat = Track(new Material(shader));
            mat2 = Track(new Material(shader));
        }

        GameObject AddMesh(GameObject parent, string name, params Material[] mats)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent.transform);
            go.AddComponent<SkinnedMeshRenderer>().sharedMaterials = mats;
            return go;
        }

        [Test]
        public void Create_EnrollsAllNonNullSlotsWithCurrentAsChoiceZero()
        {
            AddMesh(root, "Top", mat, mat2);
            AddMesh(root, "Skirt", mat);
            var creator = ChooseMenuSetup.Create(root, MaterialSlotScanner.Scan(root));
            Assert.That(creator, Is.Not.Null);
            var menu = creator.AvatarChooseMenu;
            Assert.That(menu.ChooseMaterials.Count, Is.EqualTo(3));
            Assert.That(menu.ChooseMaterials[("Top", 0)][0], Is.EqualTo(mat));
            Assert.That(menu.ChooseMaterials[("Top", 1)][0], Is.EqualTo(mat2));
            Assert.That(menu.ChooseMaterials[("Skirt", 0)][0], Is.EqualTo(mat));
            Assert.That(menu.ChooseCount, Is.EqualTo(2));
            Assert.That(menu.ChooseDefaultValue, Is.EqualTo(0));
            Assert.That(menu.TransitionSeconds, Is.EqualTo(0f));
            Assert.That(menu.UseParentMenu, Is.True);
            Assert.That(menu.Saved, Is.True);
            Assert.That(menu.Synced, Is.True);
            Assert.That(menu.UseCompressed, Is.True);
            Assert.That(menu.CanUseCompressed, Is.True);
            Assert.That(creator.gameObject.name, Is.EqualTo("色"));
            Assert.That(creator.transform.parent, Is.EqualTo(root.transform));
        }

        [Test]
        public void Create_ExcludesNullMaterialSlots()
        {
            AddMesh(root, "Top", mat, null);
            var menu = ChooseMenuSetup.Create(root, MaterialSlotScanner.Scan(root)).AvatarChooseMenu;
            Assert.That(menu.ChooseMaterials.Count, Is.EqualTo(1));
            Assert.That(menu.ChooseMaterials.ContainsKey(("Top", 0)), Is.True);
            Assert.That(menu.ChooseMaterials.ContainsKey(("Top", 1)), Is.False);
        }

        [Test]
        public void Create_NoTargetSlots_ReturnsNullAndCreatesNoObject()
        {
            AddMesh(root, "Empty", new Material[] { null });
            var before = root.transform.childCount;
            var creator = ChooseMenuSetup.Create(root, MaterialSlotScanner.Scan(root));
            Assert.That(creator, Is.Null);
            Assert.That(root.transform.childCount, Is.EqualTo(before));
        }

        [Test]
        public void Create_AlwaysCreatesNewObject()
        {
            AddMesh(root, "Top", mat);
            var slots = MaterialSlotScanner.Scan(root);
            var c1 = ChooseMenuSetup.Create(root, slots);
            var c2 = ChooseMenuSetup.Create(root, slots);
            Assert.That(c1, Is.Not.Null);
            Assert.That(c2, Is.Not.Null);
            Assert.That(c1.gameObject, Is.Not.EqualTo(c2.gameObject));
            Assert.That(c1.gameObject.name, Is.EqualTo("色"));
            Assert.That(c2.gameObject.name, Is.EqualTo("色 (1)"));
        }

        [Test]
        public void Create_ChooseCountIsClampedToAtLeastTwo()
        {
            AddMesh(root, "Top", mat);
            var slots = MaterialSlotScanner.Scan(root);
            Assert.That(ChooseMenuSetup.Create(root, slots, 4).AvatarChooseMenu.ChooseCount, Is.EqualTo(4));
            Assert.That(ChooseMenuSetup.Create(root, slots, 1).AvatarChooseMenu.ChooseCount, Is.EqualTo(2));
        }

        [Test]
        public void GroupByAvatarRoot_GroupsByDescriptorRoot()
        {
            var av1 = Track(new GameObject("Av1"));
            av1.AddComponent<VRCAvatarDescriptor>();
            var av2 = Track(new GameObject("Av2"));
            av2.AddComponent<VRCAvatarDescriptor>();
            AddMesh(av1, "M1", mat);
            AddMesh(av2, "M2", mat);
            var slots = MaterialSlotScanner.Scan(av1).Concat(MaterialSlotScanner.Scan(av2)).ToList();
            var groups = ChooseMenuSetup.GroupByAvatarRoot(slots);
            Assert.That(groups.Count, Is.EqualTo(2));
            Assert.That(groups.Select(g => g.AvatarRoot), Is.EquivalentTo(new[] { av1, av2 }));
        }

        [Test]
        public void GroupByAvatarRoot_ExcludesSlotsWithoutAvatarRoot()
        {
            // root には VRCAvatarDescriptor が無いため対象外
            AddMesh(root, "Top", mat);
            var groups = ChooseMenuSetup.GroupByAvatarRoot(MaterialSlotScanner.Scan(root));
            Assert.That(groups.Count, Is.EqualTo(0));
        }

        [Test]
        public void ValidateTargets_SingleAvatarRoot_ReturnsOk()
        {
            var avatar = Track(new GameObject("Avatar"));
            avatar.AddComponent<VRCAvatarDescriptor>();
            var costume1 = Track(new GameObject("Costume1"));
            costume1.transform.SetParent(avatar.transform);
            AddMesh(costume1, "Top", mat);
            var costume2 = Track(new GameObject("Costume2"));
            costume2.transform.SetParent(avatar.transform);
            AddMesh(costume2, "Skirt", mat);

            var costumeSlots = new List<(GameObject Costume, List<SlotInfo> Slots)>
            {
                (costume1, MaterialSlotScanner.Scan(costume1)),
                (costume2, MaterialSlotScanner.Scan(costume2)),
            };
            var (ok, reason, avatarRoot) = ChooseMenuSetup.ValidateTargets(costumeSlots);
            Assert.That(ok, Is.True);
            Assert.That(reason, Is.Null);
            Assert.That(avatarRoot, Is.EqualTo(avatar));
        }

        [Test]
        public void ValidateTargets_NoSlots_ReturnsUIMessage()
        {
            var costume = Track(new GameObject("Costume"));
            var costumeSlots = new List<(GameObject Costume, List<SlotInfo> Slots)>
            {
                (costume, new List<SlotInfo>()),
            };
            var (ok, reason, avatarRoot) = ChooseMenuSetup.ValidateTargets(costumeSlots);
            Assert.That(ok, Is.False);
            Assert.That(reason, Is.EqualTo("色変えメニューの対象スロットがありません"));
            Assert.That(avatarRoot, Is.Null);
        }

        [Test]
        public void ValidateTargets_DifferentAvatarRoots_ReturnsFalseWithUIMessage()
        {
            // 二重実装防止の核心: 異なるアバター配下の衣装を2つ渡すと NG になり、
            // 文言は旧 CostumeDashboardWindow.ShowChooseMenuDialog と同一であること
            var avatar1 = Track(new GameObject("Avatar1"));
            avatar1.AddComponent<VRCAvatarDescriptor>();
            var costume1 = Track(new GameObject("Costume1"));
            costume1.transform.SetParent(avatar1.transform);
            AddMesh(costume1, "Top", mat);

            var avatar2 = Track(new GameObject("Avatar2"));
            avatar2.AddComponent<VRCAvatarDescriptor>();
            var costume2 = Track(new GameObject("Costume2"));
            costume2.transform.SetParent(avatar2.transform);
            AddMesh(costume2, "Skirt", mat);

            var costumeSlots = new List<(GameObject Costume, List<SlotInfo> Slots)>
            {
                (costume1, MaterialSlotScanner.Scan(costume1)),
                (costume2, MaterialSlotScanner.Scan(costume2)),
            };
            var (ok, reason, avatarRoot) = ChooseMenuSetup.ValidateTargets(costumeSlots);
            Assert.That(ok, Is.False);
            Assert.That(reason, Is.EqualTo("対象の衣装は同一アバター配下である必要があります"));
            Assert.That(avatarRoot, Is.Null);
        }
    }
}
