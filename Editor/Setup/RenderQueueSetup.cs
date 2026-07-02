using UnityEditor;
using UnityEngine;
using CRQ = Narazaka.VRChat.ChangeRenderQueue.ChangeRenderQueue;

namespace Narazaka.VRChat.CostumeDashboard.Editor
{
    public static class RenderQueueSetup
    {
        public static int EffectiveQueue(Renderer renderer, int slotIndex, out CRQ source)
        {
            source = null;
            foreach (var comp in renderer.GetComponents<CRQ>())
            {
                if (comp.MaterialIndex == slotIndex || comp.MaterialIndex == -1) source = comp;
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
