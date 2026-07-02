using NUnit.Framework;
using UnityEngine;
using CRQ = Narazaka.VRChat.ChangeRenderQueue.ChangeRenderQueue;

namespace Narazaka.VRChat.CostumeDashboard.Editor.Test
{
    public class RenderQueueSetupTest
    {
        GameObject go;
        SkinnedMeshRenderer renderer;
        Material mat;

        [SetUp]
        public void SetUp()
        {
            go = new GameObject("Mesh");
            renderer = go.AddComponent<SkinnedMeshRenderer>();
            mat = new Material(Shader.Find("Standard"));
            mat.renderQueue = 2000;
            renderer.sharedMaterials = new[] { mat, mat };
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(go);
            Object.DestroyImmediate(mat);
        }

        [Test]
        public void EffectiveQueue_NoComponent_MaterialValue()
        {
            var queue = RenderQueueSetup.EffectiveQueue(renderer, 0, out var source);
            Assert.That(queue, Is.EqualTo(2000));
            Assert.That(source, Is.Null);
        }

        [Test]
        public void Set_AddsComponent()
        {
            var comp = RenderQueueSetup.Set(renderer, 0, 2460);
            Assert.That(comp.RenderQueue, Is.EqualTo(2460));
            Assert.That(comp.MaterialIndex, Is.EqualTo(0));
            var queue = RenderQueueSetup.EffectiveQueue(renderer, 0, out var source);
            Assert.That(queue, Is.EqualTo(2460));
            Assert.That(source, Is.EqualTo(comp));
            // slot 1 には効かない
            Assert.That(RenderQueueSetup.EffectiveQueue(renderer, 1, out _), Is.EqualTo(2000));
        }

        [Test]
        public void Set_MinusOne_AppliesToAllSlots()
        {
            RenderQueueSetup.Set(renderer, -1, 2450);
            Assert.That(RenderQueueSetup.EffectiveQueue(renderer, 0, out _), Is.EqualTo(2450));
            Assert.That(RenderQueueSetup.EffectiveQueue(renderer, 1, out _), Is.EqualTo(2450));
        }

        [Test]
        public void Set_SameIndexTwice_UpdatesExisting()
        {
            var c1 = RenderQueueSetup.Set(renderer, 0, 2460);
            var c2 = RenderQueueSetup.Set(renderer, 0, 2470);
            Assert.That(c1, Is.EqualTo(c2));
            Assert.That(c2.RenderQueue, Is.EqualTo(2470));
            Assert.That(renderer.GetComponents<CRQ>().Length, Is.EqualTo(1));
        }

        [Test]
        public void Remove_DeletesComponent()
        {
            var comp = RenderQueueSetup.Set(renderer, 0, 2460);
            RenderQueueSetup.Remove(comp);
            Assert.That(renderer.GetComponents<CRQ>().Length, Is.EqualTo(0));
        }

        [Test]
        public void Set_SpecificAfterWildcard_SpecificWinsOnItsSlot_WildcardRemainsFallback()
        {
            RenderQueueSetup.Set(renderer, -1, 2450);
            RenderQueueSetup.Set(renderer, 0, 2470);
            // specific が自スロットで勝ち、-1 は他スロットの fallback として生きる
            Assert.That(RenderQueueSetup.EffectiveQueue(renderer, 0, out _), Is.EqualTo(2470));
            Assert.That(RenderQueueSetup.EffectiveQueue(renderer, 1, out _), Is.EqualTo(2450));
            // ビルドプラグインは first-wins のため、specific がコンポーネント順で -1 より先頭に来ること
            var comps = renderer.GetComponents<CRQ>();
            Assert.That(comps.Length, Is.EqualTo(2));
            Assert.That(comps[0].MaterialIndex, Is.EqualTo(0));
            Assert.That(comps[1].MaterialIndex, Is.EqualTo(-1));
        }

        [Test]
        public void EffectiveQueue_DuplicateComponents_FirstWins()
        {
            // ビルドプラグイン (ChangeRenderQueuePlugin) はコンポーネント順で最初にスロットを埋めたものが勝つ
            var first = go.AddComponent<CRQ>();
            first.MaterialIndex = 0;
            first.RenderQueue = 2460;
            var second = go.AddComponent<CRQ>();
            second.MaterialIndex = 0;
            second.RenderQueue = 2480;
            var queue = RenderQueueSetup.EffectiveQueue(renderer, 0, out var source);
            Assert.That(queue, Is.EqualTo(2460));
            Assert.That(source, Is.EqualTo(first));
        }
    }
}
