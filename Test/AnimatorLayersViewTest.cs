using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEditor.Animations;
using UnityEngine;
using VRC.SDK3.Avatars.Components;
using nadena.dev.modular_avatar.core;

namespace Narazaka.VRChat.CostumeDashboard.Editor.Test
{
    public class AnimatorLayersViewTest
    {
        GameObject avatar;
        GameObject costume;
        AnimatorController controller;
        AnimationClip onClip;
        AnimationClip offClip;

        [SetUp]
        public void SetUp()
        {
            avatar = new GameObject("Avatar");
            avatar.AddComponent<VRCAvatarDescriptor>();
            costume = new GameObject("Sailor");
            costume.transform.SetParent(avatar.transform);
            var top = new GameObject("Top");
            top.transform.SetParent(costume.transform);

            controller = new AnimatorController();
            controller.AddLayer("TopToggle");
            controller.AddParameter("TopOn", AnimatorControllerParameterType.Bool);
            var sm = controller.layers[0].stateMachine;
            onClip = new AnimationClip { name = "on" };
            onClip.SetCurve("Top", typeof(GameObject), "m_IsActive", AnimationCurve.Constant(0, 1, 1));
            offClip = new AnimationClip { name = "off" };
            offClip.SetCurve("Top", typeof(GameObject), "m_IsActive", AnimationCurve.Constant(0, 1, 0));
            var on = sm.AddState("ON");
            on.motion = onClip;
            var off = sm.AddState("OFF");
            off.motion = offClip;
            sm.defaultState = off;
            var toOn = off.AddTransition(on);
            toOn.AddCondition(AnimatorConditionMode.If, 0, "TopOn");
            var toOff = on.AddTransition(off);
            toOff.AddCondition(AnimatorConditionMode.IfNot, 0, "TopOn");

            var merge = costume.AddComponent<ModularAvatarMergeAnimator>();
            merge.animator = controller;
            merge.layerType = VRCAvatarDescriptor.AnimLayerType.FX;
            merge.pathMode = MergeAnimatorPathMode.Relative;
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(avatar);
            Object.DestroyImmediate(controller);
            Object.DestroyImmediate(onClip);
            Object.DestroyImmediate(offClip);
        }

        [Test]
        public void BuildEntries_CostumeMergeAnimator_ClassifiedAndAnnotated()
        {
            var entries = AnimatorLayersView.BuildEntries(new List<GameObject> { costume }, false);

            var source = entries.Single();
            Assert.That(source.Source.Info.Kind, Is.EqualTo(AnimatorSourceKind.MergeAnimator));
            var layer = source.Layers.Single();
            Assert.That(layer.Id, Is.EqualTo("FX[Sailor]/TopToggle"));
            Assert.That(layer.Classification.Kind, Is.EqualTo(LayerPatternKind.BoolToggle));
            // Relative パス解決: Sailor/Top に作用 → 登録衣装 Sailor が作用先
            Assert.That(layer.Model.AffectedCostumes, Is.EquivalentTo(new[] { "Sailor" }));
            Assert.That(layer.Json, Does.Contain("Sailor/Top"));
        }

        [Test]
        public void BuildEntries_AnalyzeAvatarOff_ExcludesDescriptorLayers()
        {
            var descriptor = avatar.GetComponent<VRCAvatarDescriptor>();
            descriptor.customizeAnimationLayers = true;
            descriptor.baseAnimationLayers = new[]
            {
                new VRCAvatarDescriptor.CustomAnimLayer
                {
                    type = VRCAvatarDescriptor.AnimLayerType.FX,
                    animatorController = controller,
                    isDefault = false,
                },
            };
            descriptor.specialAnimationLayers = new VRCAvatarDescriptor.CustomAnimLayer[0];

            Assert.That(AnimatorLayersView.BuildEntries(new List<GameObject> { costume }, false).Count, Is.EqualTo(1));
            var withAvatar = AnimatorLayersView.BuildEntries(new List<GameObject> { costume }, true);
            Assert.That(withAvatar.Count, Is.EqualTo(2));
            Assert.That(withAvatar.Any(e => e.Source.Info.Kind == AnimatorSourceKind.PlayableLayer), Is.True);
        }
    }
}
