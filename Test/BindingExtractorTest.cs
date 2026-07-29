using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Narazaka.VRChat.CostumeDashboard.Editor.Test
{
    public class BindingExtractorTest
    {
        AnimationClip clip;

        [SetUp]
        public void SetUp()
        {
            clip = new AnimationClip { name = "test" };
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(clip);
        }

        [Test]
        public void Extract_GameObjectActive_ConstantValue()
        {
            clip.SetCurve("Costume/Top", typeof(GameObject), "m_IsActive", AnimationCurve.Constant(0, 1, 1));
            var bindings = BindingExtractor.Extract(clip, "");
            var b = bindings.Single();
            Assert.That(b.Path, Is.EqualTo("Costume/Top"));
            Assert.That(b.Type, Is.EqualTo("GameObject"));
            Assert.That(b.Category, Is.EqualTo(BindingCategory.GameObjectActive));
            Assert.That(b.ValueSummary, Is.EqualTo("1"));
        }

        [Test]
        public void Extract_PathPrefix_Applied()
        {
            clip.SetCurve("Top", typeof(GameObject), "m_IsActive", AnimationCurve.Constant(0, 1, 0));
            var bindings = BindingExtractor.Extract(clip, "Outfits/Sailor");
            Assert.That(bindings.Single().Path, Is.EqualTo("Outfits/Sailor/Top"));
        }

        [Test]
        public void Extract_Categories()
        {
            clip.SetCurve("Mesh", typeof(SkinnedMeshRenderer), "blendShape.Big", AnimationCurve.Linear(0, 0, 1, 100));
            clip.SetCurve("Mesh", typeof(SkinnedMeshRenderer), "material._Color.a", AnimationCurve.Linear(0, 0, 1, 1));
            clip.SetCurve("Obj", typeof(Transform), "m_LocalPosition.x", AnimationCurve.Constant(0, 1, 2));
            // Humanoid: パス空 + Animator 型
            clip.SetCurve("", typeof(Animator), "LeftHand.Thumb.1 Stretched", AnimationCurve.Constant(0, 1, 0.5f));
            var bindings = BindingExtractor.Extract(clip, "");
            Assert.That(bindings.Single(b => b.Property == "blendShape.Big").Category, Is.EqualTo(BindingCategory.BlendShape));
            Assert.That(bindings.Single(b => b.Property == "blendShape.Big").ValueSummary, Is.EqualTo("0→100"));
            Assert.That(bindings.Single(b => b.Property == "material._Color.a").Category, Is.EqualTo(BindingCategory.MaterialProperty));
            Assert.That(bindings.Single(b => b.Property == "m_LocalPosition.x").Category, Is.EqualTo(BindingCategory.Transform));
            var humanoid = bindings.Single(b => b.Category == BindingCategory.Humanoid);
            Assert.That(humanoid.Path, Is.Null);
            Assert.That(humanoid.Property, Is.EqualTo("LeftHand.Thumb.1 Stretched"));
        }

        [Test]
        public void Extract_MaterialSwap_FromObjectReferenceCurve()
        {
            var mat1 = new Material(Shader.Find("Standard")) { name = "MatA" };
            var mat2 = new Material(Shader.Find("Standard")) { name = "MatB" };
            var binding = EditorCurveBinding.PPtrCurve("Mesh", typeof(MeshRenderer), "m_Materials.Array.data[0]");
            AnimationUtility.SetObjectReferenceCurve(clip, binding, new[]
            {
                new ObjectReferenceKeyframe { time = 0, value = mat1 },
                new ObjectReferenceKeyframe { time = 1, value = mat2 },
            });
            var bindings = BindingExtractor.Extract(clip, "");
            var b = bindings.Single();
            Assert.That(b.Category, Is.EqualTo(BindingCategory.MaterialSwap));
            Assert.That(b.ValueSummary, Is.EqualTo("MatA→MatB"));
            Object.DestroyImmediate(mat1);
            Object.DestroyImmediate(mat2);
        }
    }
}
