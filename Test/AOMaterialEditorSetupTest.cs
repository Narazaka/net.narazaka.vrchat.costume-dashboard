using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Narazaka.VRChat.CostumeDashboard.Editor.Test
{
    public class AOMaterialEditorSetupTest
    {
        GameObject host;

        [SetUp]
        public void SetUp()
        {
            host = new GameObject("trans_host");
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(host);
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
    }
}
