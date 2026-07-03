using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using VRC.SDK3.Avatars.Components;
using net.narazaka.avatarmenucreator;

namespace Narazaka.VRChat.CostumeDashboard.Editor.Test
{
    public class ToggleMenuSetupTest
    {
        const string LtsGuid = "df12117ecd77c31469c224178886498e";

        GameObject host;

        [SetUp]
        public void SetUp()
        {
            host = new GameObject("トップス");
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(host);
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
        public void Create_Twice_ReusesComponent()
        {
            var c1 = ToggleMenuSetup.Create(host, new string[0], new ToggleMenuSetup.FadeTarget[0], 1f);
            var c2 = ToggleMenuSetup.Create(host, new string[0], new ToggleMenuSetup.FadeTarget[0], 1f);
            Assert.That(c1, Is.EqualTo(c2));
        }

        [Test]
        public void BuildFadeTargets_SameRenderer_DifferentRecommendedFrames_BothKept()
        {
            var avatarRoot = new GameObject("Avatar");
            avatarRoot.AddComponent<VRCAvatarDescriptor>();
            var mesh = new GameObject("Mesh");
            mesh.transform.SetParent(avatarRoot.transform);
            var smr = mesh.AddComponent<SkinnedMeshRenderer>();
            var shader = AssetDatabase.LoadAssetAtPath<Shader>(AssetDatabase.GUIDToAssetPath(LtsGuid));
            Assert.That(shader, Is.Not.Null, "lilToon (lts.shader) が見つからない");
            // slot0: デフォルト -> Recommended=Main, slot1: _Color 非白 (Main不可) -> Recommended=AlphaMask
            var defaultMat = new Material(shader);
            var coloredMat = new Material(shader);
            coloredMat.SetColor("_Color", new Color(1f, 0.5f, 0.5f, 1f));
            smr.sharedMaterials = new[] { defaultMat, coloredMat };
            try
            {
                var slots = MaterialSlotScanner.Scan(avatarRoot);
                var fades = ToggleMenuSetup.BuildFadeTargets(avatarRoot, slots);
                Assert.That(fades.Count, Is.EqualTo(2));
                Assert.That(fades, Has.Some.Matches<ToggleMenuSetup.FadeTarget>(f => f.MeshPath == "Mesh" && f.Frame == FadeFrame.Main));
                Assert.That(fades, Has.Some.Matches<ToggleMenuSetup.FadeTarget>(f => f.MeshPath == "Mesh" && f.Frame == FadeFrame.AlphaMask));
            }
            finally
            {
                Object.DestroyImmediate(avatarRoot);
                Object.DestroyImmediate(defaultMat);
                Object.DestroyImmediate(coloredMat);
            }
        }

        [Test]
        public void BuildFadeTargets_SameMeshPathAndFrame_Deduplicated()
        {
            var avatarRoot = new GameObject("Avatar");
            avatarRoot.AddComponent<VRCAvatarDescriptor>();
            var mesh = new GameObject("Mesh");
            mesh.transform.SetParent(avatarRoot.transform);
            var smr = mesh.AddComponent<SkinnedMeshRenderer>();
            var shader = AssetDatabase.LoadAssetAtPath<Shader>(AssetDatabase.GUIDToAssetPath(LtsGuid));
            Assert.That(shader, Is.Not.Null, "lilToon (lts.shader) が見つからない");
            // 両スロットともデフォルト -> どちらも Recommended=Main で (meshPath, Frame) が重複する
            var mat1 = new Material(shader);
            var mat2 = new Material(shader);
            smr.sharedMaterials = new[] { mat1, mat2 };
            try
            {
                var slots = MaterialSlotScanner.Scan(avatarRoot);
                var fades = ToggleMenuSetup.BuildFadeTargets(avatarRoot, slots);
                Assert.That(fades.Count, Is.EqualTo(1));
                Assert.That(fades[0].MeshPath, Is.EqualTo("Mesh"));
                Assert.That(fades[0].Frame, Is.EqualTo(FadeFrame.Main));
            }
            finally
            {
                Object.DestroyImmediate(avatarRoot);
                Object.DestroyImmediate(mat1);
                Object.DestroyImmediate(mat2);
            }
        }

        [Test]
        public void FindMenusTargeting_MatchesToggleObjects()
        {
            var avatarRoot = new GameObject("Avatar");
            avatarRoot.AddComponent<VRCAvatarDescriptor>();
            var mesh = new GameObject("Mesh");
            mesh.transform.SetParent(avatarRoot.transform);
            var renderer = mesh.AddComponent<SkinnedMeshRenderer>();
            var otherMesh = new GameObject("Other");
            otherMesh.transform.SetParent(avatarRoot.transform);
            var otherRenderer = otherMesh.AddComponent<SkinnedMeshRenderer>();
            try
            {
                var creator = ToggleMenuSetup.Create(avatarRoot, new[] { "Mesh" }, new ToggleMenuSetup.FadeTarget[0], 1f);

                var hits = ToggleMenuSetup.FindMenusTargeting(avatarRoot, renderer);
                Assert.That(hits.Count, Is.EqualTo(1));
                Assert.That(hits[0], Is.EqualTo(creator));

                var misses = ToggleMenuSetup.FindMenusTargeting(avatarRoot, otherRenderer);
                Assert.That(misses.Count, Is.EqualTo(0));
            }
            finally
            {
                Object.DestroyImmediate(avatarRoot);
            }
        }

        [Test]
        public void FindMenusTargeting_MatchesFadeKeys()
        {
            var avatarRoot = new GameObject("Avatar");
            avatarRoot.AddComponent<VRCAvatarDescriptor>();
            var mesh = new GameObject("Mesh");
            mesh.transform.SetParent(avatarRoot.transform);
            var renderer = mesh.AddComponent<SkinnedMeshRenderer>();
            try
            {
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
            finally
            {
                Object.DestroyImmediate(avatarRoot);
            }
        }

        [Test]
        public void FindMenusTargeting_NullSafe()
        {
            var avatarRoot = new GameObject("Avatar");
            avatarRoot.AddComponent<VRCAvatarDescriptor>();
            var outsideMesh = new GameObject("Outside");
            var outsideRenderer = outsideMesh.AddComponent<SkinnedMeshRenderer>();
            try
            {
                ToggleMenuSetup.Create(avatarRoot, new[] { "Mesh" }, new ToggleMenuSetup.FadeTarget[0], 1f);

                // avatarRoot 配下ではないレンダラー -> 相対パスが取れず空リスト
                Assert.That(ToggleMenuSetup.FindMenusTargeting(avatarRoot, outsideRenderer).Count, Is.EqualTo(0));
                // avatarRoot / renderer が null -> 空リスト
                Assert.That(ToggleMenuSetup.FindMenusTargeting(null, outsideRenderer).Count, Is.EqualTo(0));
                Assert.That(ToggleMenuSetup.FindMenusTargeting(avatarRoot, null).Count, Is.EqualTo(0));
            }
            finally
            {
                Object.DestroyImmediate(avatarRoot);
                Object.DestroyImmediate(outsideMesh);
            }
        }

        [Test]
        public void BuildFadeTargets_FrameOverride_Wins()
        {
            var avatarRoot = new GameObject("Avatar");
            avatarRoot.AddComponent<VRCAvatarDescriptor>();
            var mesh = new GameObject("Mesh");
            mesh.transform.SetParent(avatarRoot.transform);
            var smr = mesh.AddComponent<SkinnedMeshRenderer>();
            var shader = AssetDatabase.LoadAssetAtPath<Shader>(AssetDatabase.GUIDToAssetPath(LtsGuid));
            Assert.That(shader, Is.Not.Null, "lilToon (lts.shader) が見つからない");
            // デフォルトマテリアル -> Recommended=Main。override で Third を強制する
            var defaultMat = new Material(shader);
            smr.sharedMaterials = new[] { defaultMat };
            try
            {
                var slots = MaterialSlotScanner.Scan(avatarRoot);
                var overrides = new Dictionary<int, FadeFrame> { { smr.GetInstanceID(), FadeFrame.Third } };
                var fades = ToggleMenuSetup.BuildFadeTargets(avatarRoot, slots, overrides);
                Assert.That(fades.Count, Is.EqualTo(1));
                Assert.That(fades[0].MeshPath, Is.EqualTo("Mesh"));
                Assert.That(fades[0].Frame, Is.EqualTo(FadeFrame.Third));
            }
            finally
            {
                Object.DestroyImmediate(avatarRoot);
                Object.DestroyImmediate(defaultMat);
            }
        }
    }
}
