using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using CRQ = Narazaka.VRChat.ChangeRenderQueue.ChangeRenderQueue;

namespace Narazaka.VRChat.CostumeDashboard.Editor
{
    public static class RenderQueueSetup
    {
        // ビルドプラグイン (ChangeRenderQueuePlugin) は同一レンダラー・同一スロットを
        // 複数の ChangeRenderQueue (wildcard 展開分を含む) が重複してカバーすると
        // InvalidOperationException("RendererMaterial already set.") でビルドが失敗する。
        // specific (スロット指定) と wildcard (全スロット指定, MaterialIndex == -1) の共存は
        // 常にこの重複を生む不正な状態なので、Set() はこれを作らない
        // (specific 追加時に既存 wildcard を全スロット分の specific へ展開してから削除する)。
        // 以下の first-match は、既存データ等でこの不正な状態が発生していた場合の
        // 表示上の便宜的な規約に過ぎず、ビルド時の動作とは無関係。
        public static int EffectiveQueue(Renderer renderer, int slotIndex, out CRQ source)
        {
            source = null;
            foreach (var comp in renderer.GetComponents<CRQ>())
            {
                if (comp.MaterialIndex == slotIndex || comp.MaterialIndex == -1)
                {
                    source = comp;
                    break;
                }
            }
            if (source != null) return source.RenderQueue;
            var materials = renderer.sharedMaterials;
            if (slotIndex < 0 || slotIndex >= materials.Length || materials[slotIndex] == null) return -1;
            return materials[slotIndex].renderQueue;
        }

        /// <summary>
        /// レンダラーの指定スロット (materialIndex &gt;= 0) または全スロット (materialIndex == -1,
        /// wildcard) の RenderQueue を設定する。
        /// specific (スロット指定) コンポーネントが既に存在する状態で materialIndex == -1 を
        /// 指定すると specific + wildcard の共存 (ビルド時に例外となる不正な状態) を作ってしまう
        /// ため InvalidOperationException を投げる (Costume Dashboard の UI は常に実スロット
        /// index で呼び出しており -1 を渡すことはない)。
        /// materialIndex &gt;= 0 の指定時に既存の wildcard がある場合は、その wildcard を
        /// 対象スロット以外の全スロット分の specific コンポーネントへ展開してから削除する。
        /// </summary>
        public static CRQ Set(Renderer renderer, int materialIndex, int queue)
        {
            if (materialIndex == -1) return SetWildcard(renderer, queue);
            return SetSpecific(renderer, materialIndex, queue);
        }

        static CRQ SetWildcard(Renderer renderer, int queue)
        {
            CRQ wildcard = null;
            foreach (var comp in renderer.GetComponents<CRQ>())
            {
                if (comp.MaterialIndex == -1)
                {
                    wildcard = comp;
                }
                else
                {
                    throw new System.InvalidOperationException("スロット指定の ChangeRenderQueue が存在するため MaterialIndex=-1 は設定できません");
                }
            }
            if (wildcard == null)
            {
                wildcard = Undo.AddComponent<CRQ>(renderer.gameObject);
                wildcard.MaterialIndex = -1;
            }
            else
            {
                Undo.RecordObject(wildcard, "Set Render Queue");
            }
            wildcard.RenderQueue = queue;
            EditorUtility.SetDirty(wildcard);
            return wildcard;
        }

        static CRQ SetSpecific(Renderer renderer, int materialIndex, int queue)
        {
            CRQ target = null;
            var wildcards = new List<CRQ>();
            var existingSpecificIndices = new HashSet<int>();
            foreach (var comp in renderer.GetComponents<CRQ>())
            {
                if (comp.MaterialIndex == materialIndex)
                {
                    if (target == null) target = comp;
                }
                else if (comp.MaterialIndex == -1)
                {
                    wildcards.Add(comp);
                }
                else
                {
                    existingSpecificIndices.Add(comp.MaterialIndex);
                }
            }

            if (wildcards.Count > 0)
            {
                // wildcard を対象スロット以外の全スロット分の specific コンポーネントへ展開する
                var wildcardQueue = wildcards[0].RenderQueue;
                var materials = renderer.sharedMaterials;
                for (var i = 0; i < materials.Length; i++)
                {
                    if (i == materialIndex || existingSpecificIndices.Contains(i)) continue;
                    var expanded = Undo.AddComponent<CRQ>(renderer.gameObject);
                    expanded.MaterialIndex = i;
                    expanded.RenderQueue = wildcardQueue;
                    EditorUtility.SetDirty(expanded);
                }
                foreach (var wildcard in wildcards)
                {
                    Undo.DestroyObjectImmediate(wildcard);
                }
            }

            if (target == null)
            {
                target = Undo.AddComponent<CRQ>(renderer.gameObject);
                target.MaterialIndex = materialIndex;
            }
            else
            {
                Undo.RecordObject(target, "Set Render Queue");
            }
            target.RenderQueue = queue;
            EditorUtility.SetDirty(target);
            return target;
        }

        public static void Remove(CRQ component)
        {
            Undo.DestroyObjectImmediate(component);
        }
    }
}
