using System.Linq;
using NUnit.Framework;
using UnityEditor.Animations;
using UnityEngine;

namespace Narazaka.VRChat.CostumeDashboard.Editor.Test
{
    public class LayerModelBuilderTest
    {
        AnimatorController controller;

        [SetUp]
        public void SetUp()
        {
            // アセット保存せずメモリ上で構築（テスト後の掃除は DestroyImmediate のみ）
            controller = new AnimatorController();
            controller.AddLayer("TestLayer");
            controller.AddParameter("Toggle", AnimatorControllerParameterType.Bool);
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(controller);
        }

        SourceInfo Src() => new SourceInfo { Kind = AnimatorSourceKind.MergeAnimator, LayerType = "FX", ControllerName = "TestController" };

        [Test]
        public void Build_TwoStateMutualToggle_BuildsGraph()
        {
            var sm = controller.layers[0].stateMachine;
            var on = sm.AddState("ON");
            var off = sm.AddState("OFF");
            sm.defaultState = off;
            var toOn = off.AddTransition(on);
            toOn.AddCondition(AnimatorConditionMode.If, 0, "Toggle");
            var toOff = on.AddTransition(off);
            toOff.AddCondition(AnimatorConditionMode.IfNot, 0, "Toggle");

            var model = LayerModelBuilder.Build(controller, 0, "", Src());

            Assert.That(model.LayerName, Is.EqualTo("TestLayer"));
            Assert.That(model.Weight, Is.EqualTo(1f)); // layer 0 は常に実効 1
            var real = model.States.Where(s => s.Pseudo == PseudoNodeKind.None).ToList();
            Assert.That(real.Select(s => s.Name), Is.EquivalentTo(new[] { "ON", "OFF" }));
            Assert.That(real.Single(s => s.Name == "OFF").IsDefault, Is.True);
            // 擬似ノード: ルートの Entry/Exit/AnyState
            Assert.That(model.States.Count(s => s.Pseudo == PseudoNodeKind.Entry), Is.EqualTo(1));
            Assert.That(model.States.Count(s => s.Pseudo == PseudoNodeKind.AnyState), Is.EqualTo(1));
            // エッジ: ON→OFF, OFF→ON + Entry→OFF (default)
            var onId = real.Single(s => s.Name == "ON").Id;
            var offId = real.Single(s => s.Name == "OFF").Id;
            var e = model.Transitions.Single(t => t.FromId == offId && t.ToId == onId);
            Assert.That(e.Conditions.Single().Parameter, Is.EqualTo("Toggle"));
            Assert.That(e.Conditions.Single().Mode, Is.EqualTo("If"));
            var entryId = model.States.Single(s => s.Pseudo == PseudoNodeKind.Entry).Id;
            Assert.That(model.Transitions.Any(t => t.FromId == entryId && t.ToId == offId && t.IsDefault), Is.True);
            // 参照パラメーター
            var p = model.Parameters.Single();
            Assert.That(p.Name, Is.EqualTo("Toggle"));
            Assert.That(p.Type, Is.EqualTo("Bool"));
        }

        [Test]
        public void Build_ExitAndSubStateMachine_ResolvesPseudoNodes()
        {
            var sm = controller.layers[0].stateMachine;
            var a = sm.AddState("A");
            sm.defaultState = a;
            var exit = a.AddExitTransition();
            exit.AddCondition(AnimatorConditionMode.IfNot, 0, "Toggle");
            var sub = sm.AddStateMachine("Sub");
            var b = sub.AddState("B");
            var toSub = a.AddTransition(sub);
            toSub.AddCondition(AnimatorConditionMode.If, 0, "Toggle");

            var model = LayerModelBuilder.Build(controller, 0, "", Src());

            var bNode = model.States.Single(s => s.Name == "B");
            Assert.That(bNode.Scope, Is.EqualTo("Sub"));
            // Sub スコープにも Entry/Exit 擬似ノードがある
            Assert.That(model.States.Any(s => s.Pseudo == PseudoNodeKind.Entry && s.Scope == "Sub"), Is.True);
            // A→(root Exit)、A→(Sub の Entry) が張られる
            var aId = model.States.Single(s => s.Name == "A").Id;
            var rootExit = model.States.Single(s => s.Pseudo == PseudoNodeKind.Exit && s.Scope == "").Id;
            var subEntry = model.States.Single(s => s.Pseudo == PseudoNodeKind.Entry && s.Scope == "Sub").Id;
            Assert.That(model.Transitions.Any(t => t.FromId == aId && t.ToId == rootExit), Is.True);
            Assert.That(model.Transitions.Any(t => t.FromId == aId && t.ToId == subEntry), Is.True);
        }

        [Test]
        public void Build_MotionTimeAndBlendTree_CapturesMotionInfo()
        {
            controller.AddParameter("Radial", AnimatorControllerParameterType.Float);
            var sm = controller.layers[0].stateMachine;
            var s = sm.AddState("S");
            s.timeParameterActive = true;
            s.timeParameter = "Radial";
            var tree = new BlendTree { blendType = BlendTreeType.Simple1D, blendParameter = "Radial" };
            var clip = new AnimationClip { name = "clip0" };
            tree.AddChild(clip, 0f);
            s.motion = tree;

            var model = LayerModelBuilder.Build(controller, 0, "", Src());

            var node = model.States.Single(x => x.Name == "S");
            Assert.That(node.MotionTimeParameter, Is.EqualTo("Radial"));
            Assert.That(node.Motion.IsBlendTree, Is.True);
            Assert.That(node.Motion.BlendType, Is.EqualTo("Simple1D"));
            Assert.That(node.Motion.BlendParameter, Is.EqualTo("Radial"));
            Assert.That(node.Motion.Children.Single().Motion.ClipName, Is.EqualTo("clip0"));
            // BlendTree の駆動パラメーターも Parameters に入る
            Assert.That(model.Parameters.Any(p => p.Name == "Radial" && p.Type == "Float"), Is.True);
            Object.DestroyImmediate(clip);
            Object.DestroyImmediate(tree);
        }
    }
}
