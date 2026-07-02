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
        public void Set_SpecificAfterWildcard_ExpandsWildcardIntoSpecificComponents()
        {
            // ビルドプラグイン (ChangeRenderQueuePlugin) は specific + wildcard の共存を許さず
            // (同一スロットの重複カバーで InvalidOperationException) ビルドが失敗する。
            // そのため Set() は specific 追加時に既存 wildcard を残りスロット分の specific へ
            // 展開してから wildcard を削除する。
            RenderQueueSetup.Set(renderer, -1, 2450);
            RenderQueueSetup.Set(renderer, 0, 2470);

            var comps = renderer.GetComponents<CRQ>();
            Assert.That(comps, Has.None.Matches<CRQ>(c => c.MaterialIndex == -1));
            Assert.That(comps.Length, Is.EqualTo(2));
            Assert.That(comps, Has.Exactly(1).Matches<CRQ>(c => c.MaterialIndex == 0 && c.RenderQueue == 2470));
            Assert.That(comps, Has.Exactly(1).Matches<CRQ>(c => c.MaterialIndex == 1 && c.RenderQueue == 2450));

            Assert.That(RenderQueueSetup.EffectiveQueue(renderer, 0, out _), Is.EqualTo(2470));
            Assert.That(RenderQueueSetup.EffectiveQueue(renderer, 1, out _), Is.EqualTo(2450));
        }

        [Test]
        public void Set_WildcardWhileSpecificExists_Throws()
        {
            // specific が既に存在する状態で MaterialIndex=-1 を設定すると specific + wildcard の
            // 共存 (ビルド時に例外となる不正な状態) を作ってしまうため、明示的に禁止する
            RenderQueueSetup.Set(renderer, 0, 2460);
            Assert.That(() => RenderQueueSetup.Set(renderer, -1, 2450), Throws.InvalidOperationException);
        }

        [Test]
        public void EffectiveQueue_DuplicateComponents_ShowsFirstMatch()
        {
            // 同一スロットを複数コンポーネントがカバーする状態は、ビルドプラグイン
            // (ChangeRenderQueuePlugin) 上では InvalidOperationException("RendererMaterial
            // already set.") で NDMF ビルドが失敗する不正な状態であり、Set() はこの状態を
            // 作らない。ここでの first-match は、既存データ等で不正な状態が生じていた場合の
            // 表示上の便宜的な規約に過ぎず、ビルド時の動作を表すものではない。
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

        [Test]
        public void SetAll_ReplacesAllWithSingleWildcard()
        {
            // specific 2個（slot0/slot1 別値）がある状態で SetAll を呼ぶ
            // → CRQ は1個・MaterialIndex=-1・RenderQueue=2500
            // → 全スロットの EffectiveQueue が 2500
            RenderQueueSetup.Set(renderer, 0, 2460);
            RenderQueueSetup.Set(renderer, 1, 2470);

            var comp = RenderQueueSetup.SetAll(renderer, 2500);

            var comps = renderer.GetComponents<CRQ>();
            Assert.That(comps.Length, Is.EqualTo(1));
            Assert.That(comp.MaterialIndex, Is.EqualTo(-1));
            Assert.That(comp.RenderQueue, Is.EqualTo(2500));
            Assert.That(RenderQueueSetup.EffectiveQueue(renderer, 0, out _), Is.EqualTo(2500));
            Assert.That(RenderQueueSetup.EffectiveQueue(renderer, 1, out _), Is.EqualTo(2500));
        }

        [Test]
        public void SetAll_NoExisting_CreatesWildcard()
        {
            // コンポーネント無しから SetAll を呼ぶ
            // → CRQ は1個・MaterialIndex=-1・RenderQueue=2460
            var comp = RenderQueueSetup.SetAll(renderer, 2460);

            Assert.That(renderer.GetComponents<CRQ>().Length, Is.EqualTo(1));
            Assert.That(comp.MaterialIndex, Is.EqualTo(-1));
            Assert.That(comp.RenderQueue, Is.EqualTo(2460));
        }
    }
}
