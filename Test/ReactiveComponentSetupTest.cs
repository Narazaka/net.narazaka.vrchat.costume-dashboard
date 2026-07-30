using System.Linq;
using NUnit.Framework;
using UnityEngine;
using nadena.dev.modular_avatar.core;

namespace Narazaka.VRChat.CostumeDashboard.Editor.Test
{
    public class ReactiveComponentSetupTest
    {
        GameObject costume;
        GameObject mesh;

        [SetUp]
        public void SetUp()
        {
            costume = new GameObject("Costume");
            mesh = new GameObject("Mesh");
            mesh.transform.SetParent(costume.transform);
            mesh.AddComponent<SkinnedMeshRenderer>();
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(costume);
        }

        [Test]
        public void Scan_FindsReactiveComponentDerivatives()
        {
            mesh.AddComponent<ModularAvatarShapeChanger>();
            mesh.AddComponent<ModularAvatarObjectToggle>();
            var boneChild = new GameObject("Bone");
            boneChild.transform.SetParent(costume.transform);
            boneChild.AddComponent<ModularAvatarMeshCutter>();

            var found = ReactiveComponentSetup.Scan(costume);
            Assert.That(found.Count, Is.EqualTo(3));
        }

        [Test]
        public void Scan_ExcludesEditorOnly()
        {
            mesh.tag = "EditorOnly";
            mesh.AddComponent<ModularAvatarShapeChanger>();
            Assert.That(ReactiveComponentSetup.Scan(costume), Is.Empty);
        }

        [Test]
        public void Scan_NullSafe()
        {
            Assert.That(ReactiveComponentSetup.Scan(null), Is.Empty);
        }

        [Test]
        public void Relocate_MovesToChildPreservingValues()
        {
            var comp = mesh.AddComponent<ModularAvatarShapeChanger>();
            comp.Threshold = 0.5f;
            comp.enabled = false;

            var moved = ReactiveComponentSetup.Relocate(comp);

            Assert.That(comp == null, Is.True, "元コンポーネントが削除されている");
            Assert.That(moved.gameObject.name, Is.EqualTo("Mesh_reactive"));
            Assert.That(moved.transform.parent, Is.EqualTo(mesh.transform));
            Assert.That(((ModularAvatarShapeChanger)moved).Threshold, Is.EqualTo(0.5f));
            Assert.That(moved.enabled, Is.False);
            Assert.That(ReactiveComponentSetup.IsRelocated(moved), Is.True);
        }

        [Test]
        public void Relocate_ReusesExistingChild()
        {
            var comp1 = mesh.AddComponent<ModularAvatarShapeChanger>();
            var comp2 = mesh.AddComponent<ModularAvatarObjectToggle>();

            var moved1 = ReactiveComponentSetup.Relocate(comp1);
            var moved2 = ReactiveComponentSetup.Relocate(comp2);

            Assert.That(moved1.gameObject, Is.EqualTo(moved2.gameObject));
            Assert.That(mesh.transform.Cast<Transform>().Count(t => t.name == "Mesh_reactive"), Is.EqualTo(1));
        }

        [Test]
        public void Relocate_AlreadyRelocated_ReturnsSame()
        {
            var comp = mesh.AddComponent<ModularAvatarShapeChanger>();
            var moved = ReactiveComponentSetup.Relocate(comp);
            var again = ReactiveComponentSetup.Relocate(moved);
            Assert.That(again, Is.EqualTo(moved));
            // reactive 子の下にさらに子が生えたりしない
            Assert.That(moved.transform.childCount, Is.EqualTo(0));
        }

        [Test]
        public void Remove_NotRelocated_DeletesComponentOnly_ReturnsNull()
        {
            var comp = mesh.AddComponent<ModularAvatarShapeChanger>();
            var orphanPath = ReactiveComponentSetup.Remove(comp, costume);
            Assert.That(orphanPath, Is.Null);
            Assert.That(mesh.GetComponent<ModularAvatarShapeChanger>() == null, Is.True);
        }

        [Test]
        public void Remove_RelocatedLast_DeletesEmptyChildAndReturnsPath()
        {
            // 最後の移設コンポーネントを削除したら空の移設先も消し、
            // Toggle Menu の孤児エントリ掃除用にそのパスを返す
            var comp = mesh.AddComponent<ModularAvatarShapeChanger>();
            var moved = ReactiveComponentSetup.Relocate(comp);

            var orphanPath = ReactiveComponentSetup.Remove(moved, costume);

            Assert.That(orphanPath, Is.EqualTo("Mesh/Mesh_reactive"));
            Assert.That(mesh.transform.Find("Mesh_reactive") == null, Is.True);
        }

        [Test]
        public void Remove_RelocatedWithRemaining_KeepsChild()
        {
            var comp1 = mesh.AddComponent<ModularAvatarShapeChanger>();
            var comp2 = mesh.AddComponent<ModularAvatarObjectToggle>();
            var moved1 = ReactiveComponentSetup.Relocate(comp1);
            ReactiveComponentSetup.Relocate(comp2);

            var orphanPath = ReactiveComponentSetup.Remove(moved1, costume);

            Assert.That(orphanPath, Is.Null);
            Assert.That(mesh.transform.Find("Mesh_reactive") != null, Is.True);
        }

        [Test]
        public void Remove_RelocatedButChildHasUserAdditions_KeepsChild()
        {
            // ユーザーが移設先に別コンポーネントを足していた場合はオブジェクトを消さない
            var comp = mesh.AddComponent<ModularAvatarShapeChanger>();
            var moved = ReactiveComponentSetup.Relocate(comp);
            moved.gameObject.AddComponent<BoxCollider>();

            var orphanPath = ReactiveComponentSetup.Remove(moved, costume);

            Assert.That(orphanPath, Is.Null);
            Assert.That(mesh.transform.Find("Mesh_reactive") != null, Is.True);
        }

        [Test]
        public void Remove_NullAvatarRoot_StillDeletesEmptyChild_ReturnsNull()
        {
            var comp = mesh.AddComponent<ModularAvatarShapeChanger>();
            var moved = ReactiveComponentSetup.Relocate(comp);

            var orphanPath = ReactiveComponentSetup.Remove(moved, null);

            Assert.That(orphanPath, Is.Null);
            Assert.That(mesh.transform.Find("Mesh_reactive") == null, Is.True);
        }
    }
}
