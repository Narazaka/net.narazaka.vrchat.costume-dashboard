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
    }
}
