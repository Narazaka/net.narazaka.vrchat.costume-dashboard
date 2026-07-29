using NUnit.Framework;
using Newtonsoft.Json.Linq;

namespace Narazaka.VRChat.CostumeDashboard.Editor.Test
{
    public class LayerModelSerializerTest
    {
        static AnimatorLayerModel Model()
        {
            var m = new AnimatorLayerModel
            {
                Source = new SourceInfo { Kind = AnimatorSourceKind.MergeAnimator, LayerType = "FX", ComponentPath = "Sailor/Anim", ControllerName = "C" },
                LayerName = "Toggle",
                Weight = 1f,
            };
            var s = new StateNode { Id = 0, Name = "ON" };
            s.Bindings.Add(new BindingInfo { Path = "Sailor/Top", Type = "GameObject", Property = "m_IsActive", Category = BindingCategory.GameObjectActive, ValueSummary = "1" });
            m.States.Add(s);
            m.Transitions.Add(new TransitionEdge { FromId = 1, ToId = 0 });
            m.Parameters.Add(new ParameterInfo { Name = "T", Type = "Bool" });
            return m;
        }

        [Test]
        public void LayerId_IncludesSourceAndName()
        {
            Assert.That(LayerModelSerializer.LayerId(Model()), Is.EqualTo("FX[Sailor/Anim]/Toggle"));
            var playable = Model();
            playable.Source = new SourceInfo { Kind = AnimatorSourceKind.PlayableLayer, LayerType = "FX" };
            Assert.That(LayerModelSerializer.LayerId(playable), Is.EqualTo("FX/Toggle"));
        }

        [Test]
        public void ToJson_StructuredOutput()
        {
            var json = JObject.Parse(LayerModelSerializer.ToJson(Model(), false));
            Assert.That((string)json["id"], Is.EqualTo("FX[Sailor/Anim]/Toggle"));
            Assert.That((string)json["LayerName"], Is.EqualTo("Toggle"));
            Assert.That((string)json["Source"]["Kind"], Is.EqualTo("MergeAnimator")); // enum は文字列
            Assert.That((string)json["States"][0]["Bindings"][0]["Category"], Is.EqualTo("GameObjectActive"));
            // null プロパティ（MaskName 等）は省略される
            Assert.That(json["MaskName"], Is.Null);
            // 既定値（Pseudo=None 等）も省略される
            Assert.That(json["States"][0]["Pseudo"], Is.Null);
        }

        [Test]
        public void ToCompact_LineOrientedFormat()
        {
            var m = Model();
            m.Parameters[0].InExpressionParameters = true;
            m.Parameters[0].Synced = true;
            m.States.Add(new StateNode { Id = 1, Name = "Entry", Pseudo = PseudoNodeKind.Entry });
            m.Transitions[0].Conditions.Add(new ConditionInfo { Parameter = "T", Mode = "If", Threshold = 0 });
            m.AffectedCostumes.Add("Sailor");

            var compact = LayerModelSerializer.ToCompact(m);

            Assert.That(compact, Does.Contain("# layer FX[Sailor/Anim]/Toggle"));
            Assert.That(compact, Does.Contain("source: MergeAnimator FX @Sailor/Anim controller=C"));
            Assert.That(compact, Does.Contain("layer: Toggle index=0 weight=1"));
            Assert.That(compact, Does.Contain("param: T Bool default=0 [EP synced]"));
            Assert.That(compact, Does.Contain("node: 0 ON"));
            Assert.That(compact, Does.Contain("node: 1 Entry"));
            Assert.That(compact, Does.Contain("bind: active Sailor/Top m_IsActive = 1"));
            Assert.That(compact, Does.Contain("edge: 1->0 T If 0"));
            Assert.That(compact, Does.Contain("affects: Sailor"));
            // JSONより小さいことを確認（トークン圧縮が目的）
            Assert.That(compact.Length, Is.LessThan(LayerModelSerializer.ToJson(m, false).Length));
        }

        [Test]
        public void ToCompact_BlendTreeChildren()
        {
            var m = Model();
            m.States[0].Motion = new MotionInfo
            {
                IsBlendTree = true,
                BlendType = "Simple1D",
                BlendParameter = "Radial",
                Children = new System.Collections.Generic.List<BlendTreeChildInfo>
                {
                    new BlendTreeChildInfo { Threshold = 0, Motion = new MotionInfo { ClipName = "min" } },
                    new BlendTreeChildInfo { Threshold = 1, Motion = new MotionInfo { ClipName = "max" } },
                },
            };
            var compact = LayerModelSerializer.ToCompact(m);
            Assert.That(compact, Does.Contain("motion: tree Simple1D param=Radial"));
            Assert.That(compact, Does.Contain("child: 0 clip min"));
            Assert.That(compact, Does.Contain("child: 1 clip max"));
        }
    }
}
