using System.Linq;
using NUnit.Framework;
using UnityEditor.Animations;
using UnityEngine;
using VRC.SDK3.Avatars.Components;
using VRC.SDK3.Avatars.ScriptableObjects;
using nadena.dev.modular_avatar.core;

namespace Narazaka.VRChat.CostumeDashboard.Editor.Test
{
    public class AnimatorSourceCollectorTest
    {
        GameObject avatar;
        GameObject costume;
        AnimatorController controller;

        [SetUp]
        public void SetUp()
        {
            avatar = new GameObject("Avatar");
            avatar.AddComponent<VRCAvatarDescriptor>();
            costume = new GameObject("Sailor");
            costume.transform.SetParent(avatar.transform);
            controller = new AnimatorController();
            controller.AddLayer("L1");
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(avatar);
            Object.DestroyImmediate(controller);
        }

        [Test]
        public void CollectCostumeSources_RelativePathMode_ResolvesPrefix()
        {
            var holder = new GameObject("Anim");
            holder.transform.SetParent(costume.transform);
            var merge = holder.AddComponent<ModularAvatarMergeAnimator>();
            merge.animator = controller;
            merge.layerType = VRCAvatarDescriptor.AnimLayerType.FX;
            merge.pathMode = MergeAnimatorPathMode.Relative;
            // relativePathRoot 未設定 → コンポーネントのオブジェクト自身が基準

            var sources = AnimatorSourceCollector.CollectCostumeSources(avatar, new[] { costume });

            var s = sources.Single();
            Assert.That(s.Info.Kind, Is.EqualTo(AnimatorSourceKind.MergeAnimator));
            Assert.That(s.Info.LayerType, Is.EqualTo("FX"));
            Assert.That(s.Info.ComponentPath, Is.EqualTo("Sailor/Anim"));
            Assert.That(s.PathPrefix, Is.EqualTo("Sailor/Anim"));
            Assert.That(s.Warning, Is.Null);
        }

        [Test]
        public void CollectCostumeSources_AbsolutePathMode_EmptyPrefix()
        {
            var merge = costume.AddComponent<ModularAvatarMergeAnimator>();
            merge.animator = controller;
            merge.pathMode = MergeAnimatorPathMode.Absolute;
            var sources = AnimatorSourceCollector.CollectCostumeSources(avatar, new[] { costume });
            Assert.That(sources.Single().PathPrefix, Is.EqualTo(""));
        }

        [Test]
        public void CollectCostumeSources_NoController_Warns()
        {
            costume.AddComponent<ModularAvatarMergeAnimator>();
            var sources = AnimatorSourceCollector.CollectCostumeSources(avatar, new[] { costume });
            Assert.That(sources.Single().Warning, Is.Not.Null);
            Assert.That(sources.Single().Controller, Is.Null);
        }

        [Test]
        public void CollectAvatarSources_DescriptorAndOutsideMergeAnimators()
        {
            // Descriptor FX 枠
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
            // 衣装外の Merge Animator（アバター直下ギミック）
            var gimmick = new GameObject("Gimmick");
            gimmick.transform.SetParent(avatar.transform);
            var merge = gimmick.AddComponent<ModularAvatarMergeAnimator>();
            merge.animator = controller;
            // 衣装配下の Merge Animator は avatar 側には入らない
            var inCostume = costume.AddComponent<ModularAvatarMergeAnimator>();
            inCostume.animator = controller;

            var sources = AnimatorSourceCollector.CollectAvatarSources(avatar, new[] { costume });

            Assert.That(sources.Count, Is.EqualTo(2));
            Assert.That(sources.Any(s => s.Info.Kind == AnimatorSourceKind.PlayableLayer && s.Info.LayerType == "FX"), Is.True);
            Assert.That(sources.Any(s => s.Info.Kind == AnimatorSourceKind.MergeAnimator && s.Info.ComponentPath == "Gimmick"), Is.True);
        }

        [Test]
        public void AnnotateAffectedCostumes_MatchesByPathPrefix()
        {
            var model = new AnimatorLayerModel();
            var state = new StateNode { Id = 0, Name = "S" };
            state.Bindings.Add(new BindingInfo { Path = "Sailor/Top", Category = BindingCategory.GameObjectActive });
            model.States.Add(state);

            AnimatorSourceCollector.AnnotateAffectedCostumes(model, avatar, new[] { costume });

            Assert.That(model.AffectedCostumes, Is.EquivalentTo(new[] { "Sailor" }));
        }

        [Test]
        public void AnnotateExpressionParameters_SetsFlags()
        {
            var parameters = ScriptableObject.CreateInstance<VRCExpressionParameters>();
            parameters.parameters = new[]
            {
                new VRCExpressionParameters.Parameter
                {
                    name = "Toggle",
                    valueType = VRCExpressionParameters.ValueType.Bool,
                    saved = true,
                    networkSynced = true,
                },
            };
            var model = new AnimatorLayerModel();
            model.Parameters.Add(new ParameterInfo { Name = "Toggle", Type = "Bool" });
            model.Parameters.Add(new ParameterInfo { Name = "Internal", Type = "Bool" });

            AnimatorSourceCollector.AnnotateExpressionParameters(model, parameters);

            var toggle = model.Parameters.Single(p => p.Name == "Toggle");
            Assert.That(toggle.InExpressionParameters, Is.True);
            Assert.That(toggle.Saved, Is.True);
            Assert.That(toggle.Synced, Is.True);
            Assert.That(model.Parameters.Single(p => p.Name == "Internal").InExpressionParameters, Is.False);
            Object.DestroyImmediate(parameters);
        }
    }
}
