using UnityEditor;
using UnityEngine;
using CRQ = Narazaka.VRChat.ChangeRenderQueue.ChangeRenderQueue;

namespace Narazaka.VRChat.CostumeDashboard.Editor
{
    public static class RenderQueueSetup
    {
        // ビルドプラグイン (ChangeRenderQueuePlugin) はコンポーネント順で最初にスロットを
        // 埋めたものが勝つ (first-wins)。実効値の表示・設定はこれに一致させる。
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

        public static CRQ Set(Renderer renderer, int materialIndex, int queue)
        {
            CRQ target = null;
            foreach (var comp in renderer.GetComponents<CRQ>())
            {
                if (comp.MaterialIndex == materialIndex) target = comp;
            }
            if (target == null)
            {
                target = Undo.AddComponent<CRQ>(renderer.gameObject);
                target.MaterialIndex = materialIndex;
                // first-wins のため、specific は既存の -1 (全スロット) コンポーネントより
                // 前に来るよう移動する。-1 は他スロットの fallback として生きる
                if (materialIndex != -1)
                {
                    while (HasWildcardBefore(renderer, target))
                    {
                        if (!UnityEditorInternal.ComponentUtility.MoveComponentUp(target)) break;
                    }
                }
            }
            else
            {
                Undo.RecordObject(target, "Set Render Queue");
            }
            target.RenderQueue = queue;
            EditorUtility.SetDirty(target);
            return target;
        }

        static bool HasWildcardBefore(Renderer renderer, CRQ target)
        {
            foreach (var comp in renderer.GetComponents<CRQ>())
            {
                if (comp == target) return false;
                if (comp.MaterialIndex == -1) return true;
            }
            return false;
        }

        public static void Remove(CRQ component)
        {
            Undo.DestroyObjectImmediate(component);
        }
    }
}
