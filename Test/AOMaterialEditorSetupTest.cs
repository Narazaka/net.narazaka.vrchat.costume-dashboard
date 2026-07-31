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
    }
}
