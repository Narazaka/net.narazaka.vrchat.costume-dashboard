using NUnit.Framework;

namespace Narazaka.VRChat.CostumeDashboard.Editor.Test
{
    public class LayerPatternClassifierTest
    {
        static AnimatorLayerModel NewModel()
        {
            return new AnimatorLayerModel { LayerName = "L", Weight = 1f };
        }

        static StateNode AddState(AnimatorLayerModel m, int id, string name, PseudoNodeKind pseudo = PseudoNodeKind.None)
        {
            var node = new StateNode { Id = id, Name = name, Pseudo = pseudo };
            m.States.Add(node);
            return node;
        }

        static void AddEdge(AnimatorLayerModel m, int from, int to, string param = null, string mode = null, float threshold = 0)
        {
            var e = new TransitionEdge { FromId = from, ToId = to };
            if (param != null) e.Conditions.Add(new ConditionInfo { Parameter = param, Mode = mode, Threshold = threshold });
            m.Transitions.Add(e);
        }

        static BindingInfo Active(string path, string value) =>
            new BindingInfo { Path = path, Type = "GameObject", Property = "m_IsActive", Category = BindingCategory.GameObjectActive, ValueSummary = value };

        [Test]
        public void Classify_MutualBoolToggle()
        {
            var m = NewModel();
            m.Parameters.Add(new ParameterInfo { Name = "Toggle", Type = "Bool" });
            var on = AddState(m, 0, "ON");
            var off = AddState(m, 1, "OFF");
            on.Bindings.Add(Active("Costume/Top", "1"));
            off.Bindings.Add(Active("Costume/Top", "0"));
            AddEdge(m, 1, 0, "Toggle", "If");
            AddEdge(m, 0, 1, "Toggle", "IfNot");

            var result = LayerPatternClassifier.Classify(m);

            Assert.That(result.Kind, Is.EqualTo(LayerPatternKind.BoolToggle));
            Assert.That(result.Summary, Does.Contain("`Toggle`"));
            Assert.That(result.Summary, Does.Contain("`Top`"));
            Assert.That(result.Summary, Does.Contain("ON/OFF"));
        }

        [Test]
        public void Classify_ExitEntryBoolToggle()
        {
            // Entry 再評価形: Entry→ON(If) / Entry→OFF(default) / 各状態→Exit
            var m = NewModel();
            m.Parameters.Add(new ParameterInfo { Name = "Toggle", Type = "Bool" });
            var entry = AddState(m, 0, "Entry", PseudoNodeKind.Entry);
            var exit = AddState(m, 1, "Exit", PseudoNodeKind.Exit);
            var on = AddState(m, 2, "ON");
            var off = AddState(m, 3, "OFF");
            on.Bindings.Add(Active("Top", "1"));
            off.Bindings.Add(Active("Top", "0"));
            AddEdge(m, 0, 2, "Toggle", "If");
            m.Transitions.Add(new TransitionEdge { FromId = 0, ToId = 3, IsDefault = true });
            AddEdge(m, 2, 1, "Toggle", "IfNot");
            AddEdge(m, 3, 1, "Toggle", "If");

            var result = LayerPatternClassifier.Classify(m);

            Assert.That(result.Kind, Is.EqualTo(LayerPatternKind.BoolToggle));
        }

        [Test]
        public void Classify_IntSelect()
        {
            var m = NewModel();
            m.Parameters.Add(new ParameterInfo { Name = "Cloth", Type = "Int" });
            var a = AddState(m, 0, "Red");
            var b = AddState(m, 1, "Blue");
            a.Bindings.Add(Active("Red", "1"));
            b.Bindings.Add(Active("Blue", "1"));
            AddEdge(m, 1, 0, "Cloth", "Equals", 0);
            AddEdge(m, 0, 1, "Cloth", "Equals", 1);

            var result = LayerPatternClassifier.Classify(m);

            Assert.That(result.Kind, Is.EqualTo(LayerPatternKind.IntSelect));
            Assert.That(result.Summary, Does.Contain("`Cloth`"));
            Assert.That(result.Summary, Does.Contain("0"));
            Assert.That(result.Summary, Does.Contain("`Red`"));
        }

        [Test]
        public void Classify_FloatContinuous_MotionTime()
        {
            var m = NewModel();
            m.Parameters.Add(new ParameterInfo { Name = "Radial", Type = "Float" });
            var s = AddState(m, 0, "S");
            s.MotionTimeParameter = "Radial";
            s.Bindings.Add(new BindingInfo { Path = "Mesh", Type = "SkinnedMeshRenderer", Property = "blendShape.Big", Category = BindingCategory.BlendShape, ValueSummary = "0→100" });

            var result = LayerPatternClassifier.Classify(m);

            Assert.That(result.Kind, Is.EqualTo(LayerPatternKind.FloatContinuous));
            Assert.That(result.Summary, Does.Contain("`Radial`"));
            Assert.That(result.Summary, Does.Contain("連続変化"));
        }

        [Test]
        public void Classify_Empty_NoBindings()
        {
            var m = NewModel();
            AddState(m, 0, "S");
            var result = LayerPatternClassifier.Classify(m);
            Assert.That(result.Kind, Is.EqualTo(LayerPatternKind.Empty));
        }

        [Test]
        public void Classify_Complex_MultipleParameters()
        {
            var m = NewModel();
            m.Parameters.Add(new ParameterInfo { Name = "A", Type = "Bool" });
            m.Parameters.Add(new ParameterInfo { Name = "B", Type = "Bool" });
            var s1 = AddState(m, 0, "S1");
            var s2 = AddState(m, 1, "S2");
            s1.Bindings.Add(Active("X", "1"));
            s2.Bindings.Add(Active("X", "0"));
            AddEdge(m, 0, 1, "A", "If");
            AddEdge(m, 1, 0, "B", "If");

            var result = LayerPatternClassifier.Classify(m);

            Assert.That(result.Kind, Is.EqualTo(LayerPatternKind.Complex));
        }

        [Test]
        public void Classify_Complex_ParameterDriver()
        {
            var m = NewModel();
            m.Parameters.Add(new ParameterInfo { Name = "Toggle", Type = "Bool" });
            var on = AddState(m, 0, "ON");
            var off = AddState(m, 1, "OFF");
            on.Bindings.Add(Active("Top", "1"));
            off.Bindings.Add(Active("Top", "0"));
            on.Behaviours.Add(new BehaviourInfo { Type = "VRCAvatarParameterDriver" });
            AddEdge(m, 1, 0, "Toggle", "If");
            AddEdge(m, 0, 1, "Toggle", "IfNot");

            var result = LayerPatternClassifier.Classify(m);

            Assert.That(result.Kind, Is.EqualTo(LayerPatternKind.Complex));
        }

        [Test]
        public void BuildTargetSummary_CountsByCategory()
        {
            var bindings = new[]
            {
                Active("A", "1"),
                Active("B", "1"),
                new BindingInfo { Path = "Mesh", Property = "blendShape.Big", Category = BindingCategory.BlendShape },
            };
            Assert.That(LayerPatternClassifier.BuildTargetSummary(bindings), Is.EqualTo("オブジェクト2件、BlendShape 1件"));
        }
    }
}
