using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using VRC.SDK3.Avatars.Components;
using nadena.dev.modular_avatar.core;

namespace Narazaka.VRChat.CostumeDashboard.Editor.Test
{
    public class BlendShapeSyncSetupTest
    {
        GameObject avatar;
        List<Mesh> meshes = new List<Mesh>();

        [SetUp]
        public void SetUp()
        {
            avatar = new GameObject("Avatar");
            avatar.AddComponent<VRCAvatarDescriptor>();
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(avatar);
            foreach (var mesh in meshes)
            {
                Object.DestroyImmediate(mesh);
            }
            meshes.Clear();
        }

        GameObject Child(GameObject parent, string name)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent.transform);
            return go;
        }

        SkinnedMeshRenderer AddSkinnedMesh(GameObject go, params string[] blendShapeNames)
        {
            var mesh = new Mesh();
            mesh.vertices = new Vector3[3];
            foreach (var name in blendShapeNames)
            {
                mesh.AddBlendShapeFrame(name, 100f, new Vector3[3], null, null);
            }
            meshes.Add(mesh);
            var smr = go.AddComponent<SkinnedMeshRenderer>();
            smr.sharedMesh = mesh;
            return smr;
        }

        [Test]
        public void FindDefaultBaseMesh_PicksMostShapes()
        {
            var few = Child(avatar, "Few");
            AddSkinnedMesh(few, "A");
            var many = Child(avatar, "Many");
            AddSkinnedMesh(many, "A", "B", "C");
            var grandchildHost = Child(avatar, "GrandchildHost");
            var grandchild = Child(grandchildHost, "Grandchild");
            AddSkinnedMesh(grandchild, "A", "B", "C", "D", "E");

            var result = BlendShapeSyncSetup.FindDefaultBaseMesh(avatar);

            Assert.That(result, Is.EqualTo(many.GetComponent<SkinnedMeshRenderer>()));
        }

        [Test]
        public void FindDefaultBaseMesh_NoDirectChildShapes_ReturnsNull()
        {
            var childNoShapes = Child(avatar, "NoShapes");
            childNoShapes.AddComponent<SkinnedMeshRenderer>();

            var result = BlendShapeSyncSetup.FindDefaultBaseMesh(avatar);

            Assert.That(result, Is.Null);
        }

        [Test]
        public void FindDefaultBaseMesh_ExcludesInactive()
        {
            var few = Child(avatar, "Few");
            AddSkinnedMesh(few, "A");
            var many = Child(avatar, "Many");
            AddSkinnedMesh(many, "A", "B", "C");
            many.SetActive(false);

            var result = BlendShapeSyncSetup.FindDefaultBaseMesh(avatar);

            Assert.That(result, Is.EqualTo(few.GetComponent<SkinnedMeshRenderer>()));
        }

        [Test]
        public void FindDefaultBaseMesh_ExcludesEditorOnly()
        {
            var few = Child(avatar, "Few");
            AddSkinnedMesh(few, "A");
            var many = Child(avatar, "Many");
            AddSkinnedMesh(many, "A", "B", "C");
            many.tag = "EditorOnly";

            var result = BlendShapeSyncSetup.FindDefaultBaseMesh(avatar);

            Assert.That(result, Is.EqualTo(few.GetComponent<SkinnedMeshRenderer>()));
        }

        [Test]
        public void FindDefaultBaseMesh_ExcludesFaceMesh()
        {
            var few = Child(avatar, "Few");
            AddSkinnedMesh(few, "A");
            var many = Child(avatar, "Many");
            var manySmr = AddSkinnedMesh(many, "A", "B", "C");
            avatar.GetComponent<VRCAvatarDescriptor>().VisemeSkinnedMesh = manySmr;

            var result = BlendShapeSyncSetup.FindDefaultBaseMesh(avatar);

            Assert.That(result, Is.EqualTo(few.GetComponent<SkinnedMeshRenderer>()));
        }

        [Test]
        public void MatchingNames_IntersectsInOrder()
        {
            var targetGo = Child(avatar, "Target");
            var target = AddSkinnedMesh(targetGo, "A", "B", "C");
            var baseGo = Child(avatar, "Base");
            var baseMesh = AddSkinnedMesh(baseGo, "B", "C", "D");

            var result = BlendShapeSyncSetup.MatchingNames(target, baseMesh);

            Assert.That(result, Is.EqualTo(new List<string> { "B", "C" }));
        }

        [Test]
        public void Apply_AddsBindings()
        {
            var targetGo = Child(avatar, "Target");
            var target = AddSkinnedMesh(targetGo, "A", "B");
            var baseGo = Child(avatar, "Base");
            var baseMesh = AddSkinnedMesh(baseGo, "A", "B");

            var count = BlendShapeSyncSetup.Apply(target, baseMesh);

            Assert.That(count, Is.EqualTo(2));
            var sync = targetGo.GetComponent<ModularAvatarBlendshapeSync>();
            Assert.That(sync, Is.Not.Null);
            Assert.That(sync.Bindings.Count, Is.EqualTo(2));
            foreach (var binding in sync.Bindings)
            {
                Assert.That(binding.ReferenceMesh.Get(sync), Is.EqualTo(baseGo));
                Assert.That(binding.LocalBlendshape, Is.Empty);
                Assert.That(binding.Blendshape, Is.EqualTo("A").Or.EqualTo("B"));
            }
        }

        [Test]
        public void Apply_Twice_UpdatesNotDuplicates()
        {
            var targetGo = Child(avatar, "Target");
            var target = AddSkinnedMesh(targetGo, "A", "B");
            var baseGo = Child(avatar, "Base");
            var baseMesh = AddSkinnedMesh(baseGo, "A", "B");

            BlendShapeSyncSetup.Apply(target, baseMesh);
            BlendShapeSyncSetup.Apply(target, baseMesh);

            var sync = targetGo.GetComponent<ModularAvatarBlendshapeSync>();
            Assert.That(sync.Bindings.Count, Is.EqualTo(2));
        }

        [Test]
        public void Apply_PreservesUnrelatedBindings()
        {
            var targetGo = Child(avatar, "Target");
            var target = AddSkinnedMesh(targetGo, "A");
            var baseGo = Child(avatar, "Base");
            var baseMesh = AddSkinnedMesh(baseGo, "A");
            var otherGo = Child(avatar, "Other");
            AddSkinnedMesh(otherGo, "Z");

            var sync = targetGo.AddComponent<ModularAvatarBlendshapeSync>();
            var otherRef = new AvatarObjectReference();
            otherRef.Set(otherGo);
            sync.Bindings.Add(new BlendshapeBinding
            {
                ReferenceMesh = otherRef,
                Blendshape = "Z",
                LocalBlendshape = "Unrelated",
            });

            BlendShapeSyncSetup.Apply(target, baseMesh);

            Assert.That(sync.Bindings.Count, Is.EqualTo(2));
            Assert.That(sync.Bindings, Has.Some.Matches<BlendshapeBinding>(b => b.LocalBlendshape == "Unrelated" && b.Blendshape == "Z"));
        }
    }
}
