using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using nadena.dev.modular_avatar.core;

namespace Narazaka.VRChat.CostumeDashboard.Editor.Test
{
    public class ReactiveComponentSetupTest : UndoCleanupTestBase
    {
        GameObject costume;
        GameObject mesh;

        [SetUp]
        public void SetUp()
        {
            costume = Track(new GameObject("Costume"));
            mesh = new GameObject("Mesh");
            mesh.transform.SetParent(costume.transform);
            mesh.AddComponent<SkinnedMeshRenderer>();
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
            // moved.gameObject は Relocate 内で Undo.RegisterCreatedObjectUndo 済みのため、
            // ここへの追加も Undo.AddComponent にしないと TearDown の Undo 巻き戻し時に
            // 「dangling during an undo operation」警告が出る
            Undo.AddComponent<BoxCollider>(moved.gameObject);

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

        [Test]
        public void RelocateAndWire_RegistersReactiveWaitOnMatchingToggleMenu()
        {
            // アバタールート > 衣装 > メッシュ（Renderer 付き）に Reactive Component を置く
            var avatarRoot = Track(new GameObject("Avatar"));
            var costume = Track(new GameObject("Costume"));
            costume.transform.SetParent(avatarRoot.transform);
            var mesh = Track(new GameObject("Mesh"));
            mesh.transform.SetParent(costume.transform);
            var comp = mesh.AddComponent<ModularAvatarShapeChanger>();

            // メッシュを toggle 対象に含む既存 Toggle Menu を作る
            var menuHost = Track(new GameObject("Menu"));
            menuHost.transform.SetParent(costume.transform);
            ToggleMenuSetup.Create(menuHost, new[] { "Costume/Mesh" },
                new List<ToggleMenuSetup.FadeTarget>(), 1f);

            var moved = ReactiveComponentSetup.RelocateAndWire(comp, avatarRoot);

            Assert.That(moved, Is.Not.Null);
            var childPath = AvatarUtil.RelativePath(avatarRoot, moved.gameObject);
            var (_, targets) = ToggleMenuSetup.CollectMenuTargets(avatarRoot).First();
            Assert.That(targets, Does.Contain(childPath), "移設先が既存メニューへ変化待機として登録されること");
        }

        [Test]
        public void RelocateAndWire_NullAvatarRoot_StillRelocatesWithoutWiring()
        {
            var comp = mesh.AddComponent<ModularAvatarShapeChanger>();

            var moved = ReactiveComponentSetup.RelocateAndWire(comp, null);

            Assert.That(moved, Is.Not.Null);
            Assert.That(ReactiveComponentSetup.IsRelocated(moved), Is.True);
        }

        [Test]
        public void RemoveAndUnwire_UnregistersOrphanFromMatchingToggleMenu()
        {
            // アバタールート > 衣装 > メッシュ に移設済み Reactive Component を置く
            var avatarRoot = Track(new GameObject("Avatar"));
            var costume = Track(new GameObject("Costume"));
            costume.transform.SetParent(avatarRoot.transform);
            var mesh = Track(new GameObject("Mesh"));
            mesh.transform.SetParent(costume.transform);
            var comp = mesh.AddComponent<ModularAvatarShapeChanger>();
            var moved = ReactiveComponentSetup.Relocate(comp);
            var childPath = AvatarUtil.RelativePath(avatarRoot, moved.gameObject);

            // 移設先を変化待機として登録済みの既存 Toggle Menu を作る
            var menuHost = Track(new GameObject("Menu"));
            menuHost.transform.SetParent(costume.transform);
            ToggleMenuSetup.Create(menuHost, new[] { "Costume/Mesh" },
                new List<ToggleMenuSetup.FadeTarget>(), 1f, new[] { childPath });

            var orphanPath = ReactiveComponentSetup.RemoveAndUnwire(moved, avatarRoot);

            Assert.That(orphanPath, Is.EqualTo(childPath));
            var (_, targets) = ToggleMenuSetup.CollectMenuTargets(avatarRoot).First();
            Assert.That(targets, Does.Not.Contain(orphanPath), "孤児エントリが既存メニューから取り除かれること");
        }

        [Test]
        public void RemoveAndUnwire_NotOrphaned_ReturnsNullWithoutTouchingMenu()
        {
            // 移設先 (_reactive) に他コンポーネントが残るため孤児にならないケース
            var comp1 = mesh.AddComponent<ModularAvatarShapeChanger>();
            var comp2 = mesh.AddComponent<ModularAvatarObjectToggle>();
            var moved1 = ReactiveComponentSetup.Relocate(comp1);
            ReactiveComponentSetup.Relocate(comp2);

            var orphanPath = ReactiveComponentSetup.RemoveAndUnwire(moved1, costume);

            Assert.That(orphanPath, Is.Null);
        }
    }
}
