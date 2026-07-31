using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Narazaka.VRChat.CostumeDashboard.Editor.Test
{
    public class AOMaterialEditorSetupTest : UndoCleanupTestBase
    {
        GameObject host;

        [SetUp]
        public void SetUp()
        {
            host = Track(new GameObject("trans_host"));
        }

        [Test]
        public void IsAvailable_DoesNotThrow()
        {
            // 導入有無に関わらず bool を返す（例外にならない）
            Assert.DoesNotThrow(() => { var _ = AOMaterialEditorSetup.IsAvailable; });
        }

        [Test]
        public void Apply_Unavailable_Throws()
        {
            Assume.That(AOMaterialEditorSetup.IsAvailable, Is.False, "aoyon.material-editor 導入環境では skip");
            Assert.Throws<System.InvalidOperationException>(() =>
                AOMaterialEditorSetup.Apply(host, new List<AOMaterialEditorSetup.SlotTarget>(), null, new List<PresetProperty>()));
        }

        [Test]
        public void Apply_CreatesComponentWithSlotTargets()
        {
            Assume.That(AOMaterialEditorSetup.IsAvailable, Is.True, "aoyon.material-editor 未導入なら skip");
            var slots = new List<AOMaterialEditorSetup.SlotTarget>
            {
                new AOMaterialEditorSetup.SlotTarget { RendererPath = "Costume/Top", MaterialIndex = -1 },
                new AOMaterialEditorSetup.SlotTarget { RendererPath = "Costume/Skirt", MaterialIndex = 1 },
            };
            var shader = Shader.Find("Standard");
            var props = new List<PresetProperty>
            {
                new PresetProperty { Name = "_UseMain3rdTex", Type = PresetPropertyType.Float, FloatValue = 1 },
            };
            var comp = AOMaterialEditorSetup.Apply(host, slots, shader, props);
            Assert.That(comp, Is.Not.Null);

            // SerializedObject でシリアライズ結果を検証（internal 型のため）
            var so = new SerializedObject(comp);
            Assert.That(so.FindProperty("TargetSettings.Mode").enumNames[so.FindProperty("TargetSettings.Mode").enumValueIndex], Is.EqualTo("SlotTargets"));
            var targetSlots = so.FindProperty("TargetSettings.SlotTargets.TargetSlots");
            Assert.That(targetSlots.arraySize, Is.EqualTo(2));
            Assert.That(targetSlots.GetArrayElementAtIndex(0).FindPropertyRelative("RendererReference.referencePath").stringValue, Is.EqualTo("Costume/Top"));
            Assert.That(targetSlots.GetArrayElementAtIndex(1).FindPropertyRelative("MaterialIndex").intValue, Is.EqualTo(1));
            Assert.That(so.FindProperty("OverrideSettings.OverrideShader").boolValue, Is.True);
            Assert.That(so.FindProperty("OverrideSettings.TargetShader").objectReferenceValue, Is.EqualTo(shader));
            Assert.That(so.FindProperty("OverrideSettings.OverrideRenderQueue").boolValue, Is.False);
            var overrides = so.FindProperty("OverrideSettings.PropertyOverrides");
            Assert.That(overrides.arraySize, Is.EqualTo(1));
            Assert.That(overrides.GetArrayElementAtIndex(0).FindPropertyRelative("PropertyName").stringValue, Is.EqualTo("_UseMain3rdTex"));
        }

        [Test]
        public void Apply_NullShader_NoOverrideShader()
        {
            Assume.That(AOMaterialEditorSetup.IsAvailable, Is.True, "aoyon.material-editor 未導入なら skip");
            var comp = AOMaterialEditorSetup.Apply(host, new List<AOMaterialEditorSetup.SlotTarget>(), null, new List<PresetProperty>());
            var so = new SerializedObject(comp);
            Assert.That(so.FindProperty("OverrideSettings.OverrideShader").boolValue, Is.False);
        }

        [Test]
        public void Apply_Twice_ReusesComponent()
        {
            Assume.That(AOMaterialEditorSetup.IsAvailable, Is.True, "aoyon.material-editor 未導入なら skip");
            Assert.That(AOMaterialEditorSetup.HasComponent(host), Is.False);
            var c1 = AOMaterialEditorSetup.Apply(host, new List<AOMaterialEditorSetup.SlotTarget>(), null, new List<PresetProperty>());
            var c2 = AOMaterialEditorSetup.Apply(host, new List<AOMaterialEditorSetup.SlotTarget>(), null, new List<PresetProperty>());
            Assert.That(c1, Is.EqualTo(c2));
            Assert.That(AOMaterialEditorSetup.HasComponent(host), Is.True);
        }

        [Test]
        public void Availability_UnknownFamilyOneTwoTrans_ReturnsDisabledWithReason()
        {
            Assume.That(AOMaterialEditorSetup.IsAvailable, Is.True, "aoyon.material-editor 未導入なら skip");
            var avatarRoot = Track(new GameObject("Avatar"));
            var group = new SlotGroup
            {
                Family = "unknown",
                Variant = "onetrans",
                SupportsFade = true,
                FadeDisabledReason = "未知のシェーダー",
            };

            var (enabled, reason) = AOMaterialEditorSetup.Availability(avatarRoot, group);

            Assert.That(enabled, Is.False);
            Assert.That(reason, Is.EqualTo("未知のシェーダー"));
        }

        [Test]
        public void Availability_NullAvatarRoot_ReturnsDisabled()
        {
            Assume.That(AOMaterialEditorSetup.IsAvailable, Is.True, "aoyon.material-editor 未導入なら skip");
            var group = new SlotGroup { SupportsFade = true, CanSetupFade = true };

            var (enabled, reason) = AOMaterialEditorSetup.Availability(null, group);

            Assert.That(enabled, Is.False);
            Assert.That(reason, Is.EqualTo("アバタールートが見つかりません"));
        }

        [Test]
        public void CreateBatch_ShaderResolutionFailure_ReturnsErrorWithoutDialog()
        {
            Assume.That(AOMaterialEditorSetup.IsAvailable, Is.True, "aoyon.material-editor 未導入なら skip");
            var costume = Track(new GameObject("Costume"));
            var avatarRoot = Track(new GameObject("Avatar"));
            // Availability を通すが、透過版シェーダー GUID が解決できず CreateForGroup がエラーを返すケース
            // （旧 CreateAOMEBatch は戻り値チェックが無く常に created++、かつ CreateAOMaterialEditor 内で
            // DisplayDialog をループ中に出していた。新実装は Errors に積んで返し、ダイアログは呼び出し元が1回だけ出す）
            var group = new SlotGroup
            {
                Family = "lilToon",
                Variant = "opaque",
                SupportsFade = true,
                CanSetupFade = true,
                Preset = FadeFrame.Third,
                NeedsShaderOverride = true,
                TransparentGuid = "00000000000000000000000000000000",
            };

            var (created, skipped, errors) = AOMaterialEditorSetup.CreateBatch(costume, avatarRoot, new List<SlotGroup> { group });

            Assert.That(created, Is.EqualTo(0));
            Assert.That(skipped, Is.EqualTo(1));
            Assert.That(errors, Has.Count.EqualTo(1));
            Assert.That(errors[0], Does.Contain("透過版シェーダーが見つかりません"));
        }

        [Test]
        public void CreateForGroup_Unavailable_ReturnsReasonWithoutCreatingHost()
        {
            // Availability が false のグループで CreateForGroup を直接呼んでも、
            // GUI の button.SetEnabled 相当の防壁が無いエージェント経路で空ホストを残さないこと
            Assume.That(AOMaterialEditorSetup.IsAvailable, Is.True, "aoyon.material-editor 未導入なら skip（未導入時は別の理由で不可になるため）");
            var costume = Track(new GameObject("Costume"));
            var avatarRoot = Track(new GameObject("Avatar"));
            var group = new SlotGroup
            {
                Variant = "opaque",
                SupportsFade = false,
                FadeDisabledReason = "メッシュ以外のRenderer",
            };

            var error = AOMaterialEditorSetup.CreateForGroup(costume, avatarRoot, group);

            Assert.That(error, Is.EqualTo("メッシュ以外のRenderer"));
            Assert.That(costume.transform.Find("trans"), Is.Null, "失敗時に trans ホストが作成されないこと");
        }

        [Test]
        public void FindHost_NoHost_ReturnsNull()
        {
            var costume = Track(new GameObject("Costume"));
            var group = new SlotGroup { Variant = "opaque", Preset = FadeFrame.Third };

            Assert.That(AOMaterialEditorSetup.FindHost(costume, group), Is.Null);
        }

        [Test]
        public void FindHost_NullCostume_ReturnsNull()
        {
            var group = new SlotGroup { Variant = "opaque", Preset = FadeFrame.Third };
            Assert.That(AOMaterialEditorSetup.FindHost(null, group), Is.Null);
        }

        [Test]
        public void IsConfigured_NoHost_ReturnsFalse()
        {
            var costume = Track(new GameObject("Costume"));
            var group = new SlotGroup { Variant = "opaque", Preset = FadeFrame.Third };

            Assert.That(AOMaterialEditorSetup.IsConfigured(costume, group), Is.False);
        }

        [Test]
        public void FindHost_IsConfigured_ReflectCreateForGroupResult()
        {
            // CreateForGroup が作るホストパス（trans/<HostSuffix>）と FindHost/IsConfigured が同じ知識を共有していること
            Assume.That(AOMaterialEditorSetup.IsAvailable, Is.True, "aoyon.material-editor 未導入なら skip");
            var costume = Track(new GameObject("Costume"));
            var avatarRoot = Track(new GameObject("Avatar"));
            costume.transform.SetParent(avatarRoot.transform);
            var mesh = Track(new GameObject("Mesh"));
            mesh.transform.SetParent(costume.transform);
            var renderer = mesh.AddComponent<SkinnedMeshRenderer>();
            var mat = Track(new Material(Shader.Find("Standard")));
            var group = new SlotGroup
            {
                Family = "lilToon",
                Variant = "opaque",
                SupportsFade = true,
                CanSetupFade = true,
                Preset = FadeFrame.Third,
                Slots = new List<SlotInfo> { new SlotInfo { Renderer = renderer, SlotIndex = 0, Material = mat } },
            };

            Assert.That(AOMaterialEditorSetup.IsConfigured(costume, group), Is.False, "作成前は未設定");

            var error = AOMaterialEditorSetup.CreateForGroup(costume, avatarRoot, group);

            Assert.That(error, Is.Null);
            var found = AOMaterialEditorSetup.FindHost(costume, group);
            Assert.That(found, Is.Not.Null);
            Assert.That(found.name, Is.EqualTo(AOMaterialEditorSetup.HostSuffix(group)));
            Assert.That(AOMaterialEditorSetup.IsConfigured(costume, group), Is.True, "作成後は設定済み");
        }

        [Test]
        public void HostSuffix_AdjustAppendedForColorFrames()
        {
            var group = new SlotGroup { Variant = "opaque", Preset = FadeFrame.Third, AlphaMaskAdjust = AlphaMaskAdjust.ToMultiply };
            Assert.That(AOMaterialEditorSetup.HostSuffix(group), Is.EqualTo("opaque_3rd_ammul"));
            group.AlphaMaskAdjust = AlphaMaskAdjust.Neutralize;
            Assert.That(AOMaterialEditorSetup.HostSuffix(group), Is.EqualTo("opaque_3rd_amoff"));
            group.AlphaMaskAdjust = AlphaMaskAdjust.None;
            Assert.That(AOMaterialEditorSetup.HostSuffix(group), Is.EqualTo("opaque_3rd"));
        }

        [Test]
        public void HostSuffix_AlphaMaskPreset_NoAdjustSuffix()
        {
            // AlphaMask 枠は調整 override を適用しない（DriverProps が mode=2 を設定済み）ため suffix も付かない
            var group = new SlotGroup { Variant = "opaque", Preset = FadeFrame.AlphaMask, AlphaMaskAdjust = AlphaMaskAdjust.ToMultiply };
            Assert.That(AOMaterialEditorSetup.HostSuffix(group), Is.EqualTo("opaque_alpha_mask"));
        }

        [Test]
        public void HostSuffix_OneTwoTrans_IncludesEffectivePreset()
        {
            // onetrans/twotrans も実効枠（group.Preset ?? Third）で suffix を分け、
            // Preset 違い（DriverProps 内容が違う）グループが同一ホストに衝突しないようにする
            // onetrans 特例はフェード対応 Renderer 専用のため SupportsFade=true が前提
            var group = new SlotGroup { Variant = "onetrans", Preset = FadeFrame.Third, SupportsFade = true };
            Assert.That(AOMaterialEditorSetup.HostSuffix(group), Is.EqualTo("onetrans_3rd"));
            group.Preset = null;
            Assert.That(AOMaterialEditorSetup.HostSuffix(group), Is.EqualTo("onetrans_3rd"));
            group.Preset = FadeFrame.AlphaMask;
            Assert.That(AOMaterialEditorSetup.HostSuffix(group), Is.EqualTo("onetrans_alpha_mask"));
        }
    }
}
