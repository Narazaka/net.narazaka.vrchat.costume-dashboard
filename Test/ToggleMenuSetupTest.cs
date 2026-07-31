using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using VRC.SDK3.Avatars.Components;
using net.narazaka.avatarmenucreator;
using nadena.dev.modular_avatar.core;

namespace Narazaka.VRChat.CostumeDashboard.Editor.Test
{
    public class ToggleMenuSetupTest : UndoCleanupTestBase
    {
        const string LtsGuid = "df12117ecd77c31469c224178886498e";

        GameObject host;

        [SetUp]
        public void SetUp()
        {
            host = Track(new GameObject("トップス"));
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
        public void Create_MainFrame_UsesColor()
        {
            var creator = ToggleMenuSetup.Create(
                host,
                new string[0],
                new[] { new ToggleMenuSetup.FadeTarget { MeshPath = "Costume/Top", Frame = FadeFrame.Main } },
                1f);
            var vec = creator.AvatarToggleMenu.ToggleShaderVectorParameters[("Costume/Top", "_Color")];
            Assert.That(vec.Inactive, Is.EqualTo(new Vector4(1, 1, 1, 0)));
            Assert.That(vec.Active, Is.EqualTo(new Vector4(1, 1, 1, 1)));
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
        public void Create_ReactiveWaitPaths_RegisteredAsOnWithOffset99()
        {
            var creator = ToggleMenuSetup.Create(
                host,
                new[] { "Costume/Top" },
                new ToggleMenuSetup.FadeTarget[0],
                1f,
                new[] { "Costume/Top/Top_reactive" });

            var menu = creator.AvatarToggleMenu;
            Assert.That(menu.ToggleObjects[("Costume/Top/Top_reactive")], Is.EqualTo(ToggleType.ON));
            Assert.That(menu.ToggleObjectTransitionOffsetPercents[("Costume/Top/Top_reactive")], Is.EqualTo(99f));
            // 通常の toggle 対象には変化待機は付かない
            Assert.That(menu.ToggleObjectTransitionOffsetPercents.ContainsKey("Costume/Top"), Is.False);
        }

        [Test]
        public void RegisterReactiveWait_AddsToExistingMenu()
        {
            var creator = ToggleMenuSetup.Create(host, new[] { "Costume/Top" }, new ToggleMenuSetup.FadeTarget[0], 1f);
            ToggleMenuSetup.RegisterReactiveWait(creator, "Costume/Top/Top_reactive");

            var menu = creator.AvatarToggleMenu;
            Assert.That(menu.ToggleObjects[("Costume/Top/Top_reactive")], Is.EqualTo(ToggleType.ON));
            Assert.That(menu.ToggleObjectTransitionOffsetPercents[("Costume/Top/Top_reactive")], Is.EqualTo(99f));
        }

        [Test]
        public void UnregisterPath_RemovesReactiveWaitEntries()
        {
            var creator = ToggleMenuSetup.Create(
                host,
                new[] { "Costume/Top" },
                new ToggleMenuSetup.FadeTarget[0],
                1f,
                new[] { "Costume/Top/Top_reactive" });

            ToggleMenuSetup.UnregisterPath(creator, "Costume/Top/Top_reactive");

            var menu = creator.AvatarToggleMenu;
            Assert.That(menu.ToggleObjects.ContainsKey("Costume/Top/Top_reactive"), Is.False);
            Assert.That(menu.ToggleObjectTransitionOffsetPercents.ContainsKey("Costume/Top/Top_reactive"), Is.False);
            // 他のエントリは残る
            Assert.That(menu.ToggleObjects.ContainsKey("Costume/Top"), Is.True);
        }

        [Test]
        public void Create_Twice_ReusesComponent()
        {
            var c1 = ToggleMenuSetup.Create(host, new string[0], new ToggleMenuSetup.FadeTarget[0], 1f);
            var c2 = ToggleMenuSetup.Create(host, new string[0], new ToggleMenuSetup.FadeTarget[0], 1f);
            Assert.That(c1, Is.EqualTo(c2));
        }

        [Test]
        public void BuildFadeTargets_SameRenderer_UsesCommonFrame_Single()
        {
            var avatarRoot = Track(new GameObject("Avatar"));
            avatarRoot.AddComponent<VRCAvatarDescriptor>();
            var mesh = new GameObject("Mesh");
            mesh.transform.SetParent(avatarRoot.transform);
            var smr = mesh.AddComponent<SkinnedMeshRenderer>();
            var shader = AssetDatabase.LoadAssetAtPath<Shader>(AssetDatabase.GUIDToAssetPath(LtsGuid));
            Assert.That(shader, Is.Not.Null, "lilToon (lts.shader) が見つからない");
            // slot0: デフォルト(main○) / slot1: _Color 非白 (main不可、alpha可) -> レンダラー共通枠は AlphaMask の1件のみ
            var defaultMat = Track(new Material(shader));
            var coloredMat = Track(new Material(shader));
            coloredMat.SetColor("_Color", new Color(1f, 0.5f, 0.5f, 1f));
            smr.sharedMaterials = new[] { defaultMat, coloredMat };
            var slots = MaterialSlotScanner.Scan(avatarRoot);
            var fades = ToggleMenuSetup.BuildFadeTargets(avatarRoot, slots);
            Assert.That(fades.Count, Is.EqualTo(1));
            Assert.That(fades[0].MeshPath, Is.EqualTo("Mesh"));
            Assert.That(fades[0].Frame, Is.EqualTo(FadeFrame.AlphaMask));
        }

        [Test]
        public void BuildFadeTargets_SameMeshPathAndFrame_Deduplicated()
        {
            var avatarRoot = Track(new GameObject("Avatar"));
            avatarRoot.AddComponent<VRCAvatarDescriptor>();
            var mesh = new GameObject("Mesh");
            mesh.transform.SetParent(avatarRoot.transform);
            var smr = mesh.AddComponent<SkinnedMeshRenderer>();
            var shader = AssetDatabase.LoadAssetAtPath<Shader>(AssetDatabase.GUIDToAssetPath(LtsGuid));
            Assert.That(shader, Is.Not.Null, "lilToon (lts.shader) が見つからない");
            // 両スロットともデフォルト -> どちらも Recommended=Main で (meshPath, Frame) が重複する
            var mat1 = Track(new Material(shader));
            var mat2 = Track(new Material(shader));
            smr.sharedMaterials = new[] { mat1, mat2 };
            var slots = MaterialSlotScanner.Scan(avatarRoot);
            var fades = ToggleMenuSetup.BuildFadeTargets(avatarRoot, slots);
            Assert.That(fades.Count, Is.EqualTo(1));
            Assert.That(fades[0].MeshPath, Is.EqualTo("Mesh"));
            Assert.That(fades[0].Frame, Is.EqualTo(FadeFrame.Main));
        }

        [Test]
        public void FindMenusTargeting_MatchesToggleObjects()
        {
            var avatarRoot = Track(new GameObject("Avatar"));
            avatarRoot.AddComponent<VRCAvatarDescriptor>();
            var mesh = new GameObject("Mesh");
            mesh.transform.SetParent(avatarRoot.transform);
            var renderer = mesh.AddComponent<SkinnedMeshRenderer>();
            var otherMesh = new GameObject("Other");
            otherMesh.transform.SetParent(avatarRoot.transform);
            var otherRenderer = otherMesh.AddComponent<SkinnedMeshRenderer>();
            var creator = ToggleMenuSetup.Create(avatarRoot, new[] { "Mesh" }, new ToggleMenuSetup.FadeTarget[0], 1f);

            var hits = ToggleMenuSetup.FindMenusTargeting(avatarRoot, renderer);
            Assert.That(hits.Count, Is.EqualTo(1));
            Assert.That(hits[0], Is.EqualTo(creator));

            var misses = ToggleMenuSetup.FindMenusTargeting(avatarRoot, otherRenderer);
            Assert.That(misses.Count, Is.EqualTo(0));
        }

        [Test]
        public void FindMenusTargeting_MatchesFadeKeys()
        {
            var avatarRoot = Track(new GameObject("Avatar"));
            avatarRoot.AddComponent<VRCAvatarDescriptor>();
            var mesh = new GameObject("Mesh");
            mesh.transform.SetParent(avatarRoot.transform);
            var renderer = mesh.AddComponent<SkinnedMeshRenderer>();
            // ToggleObjects は空のまま、shaderVectorFades（Main枠 -> _Color）のみで作成する
            var creator = ToggleMenuSetup.Create(
                avatarRoot,
                new string[0],
                new[] { new ToggleMenuSetup.FadeTarget { MeshPath = "Mesh", Frame = FadeFrame.Main } },
                1f);

            var hits = ToggleMenuSetup.FindMenusTargeting(avatarRoot, renderer);
            Assert.That(hits.Count, Is.EqualTo(1));
            Assert.That(hits[0], Is.EqualTo(creator));
        }

        [Test]
        public void FindMenusTargeting_NullSafe()
        {
            var avatarRoot = Track(new GameObject("Avatar"));
            avatarRoot.AddComponent<VRCAvatarDescriptor>();
            var outsideMesh = Track(new GameObject("Outside"));
            var outsideRenderer = outsideMesh.AddComponent<SkinnedMeshRenderer>();
            ToggleMenuSetup.Create(avatarRoot, new[] { "Mesh" }, new ToggleMenuSetup.FadeTarget[0], 1f);

            // avatarRoot 配下ではないレンダラー -> 相対パスが取れず空リスト
            Assert.That(ToggleMenuSetup.FindMenusTargeting(avatarRoot, outsideRenderer).Count, Is.EqualTo(0));
            // avatarRoot / renderer が null -> 空リスト
            Assert.That(ToggleMenuSetup.FindMenusTargeting(null, outsideRenderer).Count, Is.EqualTo(0));
            Assert.That(ToggleMenuSetup.FindMenusTargeting(avatarRoot, null).Count, Is.EqualTo(0));
        }

        [Test]
        public void BuildFadeTargets_FrameOverride_Wins()
        {
            var avatarRoot = Track(new GameObject("Avatar"));
            avatarRoot.AddComponent<VRCAvatarDescriptor>();
            var mesh = new GameObject("Mesh");
            mesh.transform.SetParent(avatarRoot.transform);
            var smr = mesh.AddComponent<SkinnedMeshRenderer>();
            var shader = AssetDatabase.LoadAssetAtPath<Shader>(AssetDatabase.GUIDToAssetPath(LtsGuid));
            Assert.That(shader, Is.Not.Null, "lilToon (lts.shader) が見つからない");
            // デフォルトマテリアル -> Recommended=Main。override で Third を強制する
            var defaultMat = Track(new Material(shader));
            smr.sharedMaterials = new[] { defaultMat };
            var slots = MaterialSlotScanner.Scan(avatarRoot);
            var overrides = new Dictionary<int, FadeFrame> { { smr.GetInstanceID(), FadeFrame.Third } };
            var fades = ToggleMenuSetup.BuildFadeTargets(avatarRoot, slots, overrides);
            Assert.That(fades.Count, Is.EqualTo(1));
            Assert.That(fades[0].MeshPath, Is.EqualTo("Mesh"));
            Assert.That(fades[0].Frame, Is.EqualTo(FadeFrame.Third));
        }

        [Test]
        public void CreateForSlots_IncludesRelocatedReactiveComponentsAsWaitTargets()
        {
            // アバタールート > 衣装 > メッシュ、メッシュ配下に移設済み Reactive Component を置く
            var avatarRoot = Track(new GameObject("Avatar"));
            var costume = Track(new GameObject("Costume"));
            costume.transform.SetParent(avatarRoot.transform);
            var mesh = Track(new GameObject("Mesh"));
            mesh.transform.SetParent(costume.transform);
            var renderer = mesh.AddComponent<SkinnedMeshRenderer>();

            // 移設済み状態（(ホスト名)_reactive 配下）を作る
            var reactiveHost = Track(new GameObject("Mesh_reactive"));
            reactiveHost.transform.SetParent(mesh.transform);
            reactiveHost.AddComponent<ModularAvatarShapeChanger>();

            var slots = new List<SlotInfo> { new SlotInfo { Renderer = renderer, SlotIndex = 0 } };

            var creator = ToggleMenuSetup.CreateForSlots(costume, avatarRoot, slots, null, "テストメニュー", 1f);

            Assert.That(creator, Is.Not.Null);
            Assert.That(creator.gameObject.name, Is.EqualTo("テストメニュー"));
            Assert.That(creator.gameObject.transform.parent, Is.EqualTo(costume.transform));
            var (_, targets) = ToggleMenuSetup.CollectMenuTargets(avatarRoot).First();
            Assert.That(targets, Does.Contain("Costume/Mesh"));
            Assert.That(targets, Does.Contain("Costume/Mesh/Mesh_reactive"), "移設済み Reactive Component が変化待機として含まれること");
        }
    }
}
