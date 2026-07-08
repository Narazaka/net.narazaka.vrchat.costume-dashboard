using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using VRC.SDK3.Avatars.Components;
using nadena.dev.modular_avatar.core;

namespace Narazaka.VRChat.CostumeDashboard.Editor
{
    public static class BlendShapeSyncSetup
    {
        /// <summary>アバター直下（直接の子）の SkinnedMeshRenderer のうち BlendShape 数最大のものを素体候補として返す（0件なら null）。
        /// 非アクティブ / タグ EditorOnly / Avatar Descriptor の Face Mesh (VisemeSkinnedMesh) は除外する</summary>
        public static SkinnedMeshRenderer FindDefaultBaseMesh(GameObject avatarRoot)
        {
            SkinnedMeshRenderer best = null;
            var bestCount = 0;
            var descriptor = avatarRoot.GetComponent<VRCAvatarDescriptor>();
            var faceMesh = descriptor != null ? descriptor.VisemeSkinnedMesh : null;
            foreach (Transform t in avatarRoot.transform)
            {
                var smr = t.GetComponent<SkinnedMeshRenderer>();
                if (smr == null || smr.sharedMesh == null) continue;
                if (!smr.gameObject.activeInHierarchy) continue;
                if (AvatarUtil.IsEditorOnly(smr.gameObject, avatarRoot)) continue;
                if (faceMesh != null && smr == faceMesh) continue;
                var count = smr.sharedMesh.blendShapeCount;
                if (count > bestCount)
                {
                    best = smr;
                    bestCount = count;
                }
            }
            return bestCount > 0 ? best : null;
        }

        /// <summary>mesh の BlendShape 名一覧（sharedMesh null なら空）</summary>
        public static List<string> GetBlendShapeNames(SkinnedMeshRenderer mesh)
        {
            var names = new List<string>();
            var sharedMesh = mesh != null ? mesh.sharedMesh : null;
            if (sharedMesh == null) return names;
            for (var i = 0; i < sharedMesh.blendShapeCount; i++)
            {
                names.Add(sharedMesh.GetBlendShapeName(i));
            }
            return names;
        }

        /// <summary>target と baseMesh の同名 BlendShape 名一覧</summary>
        public static List<string> MatchingNames(SkinnedMeshRenderer target, SkinnedMeshRenderer baseMesh)
        {
            var baseNames = new HashSet<string>(GetBlendShapeNames(baseMesh));
            return GetBlendShapeNames(target).Where(name => baseNames.Contains(name)).ToList();
        }

        /// <summary>target に ModularAvatarBlendshapeSync を付与/更新し、baseMesh と同名の全シェイプをバインドする（update-or-add、Undo対応）。バインド件数を返す</summary>
        public static int Apply(SkinnedMeshRenderer target, SkinnedMeshRenderer baseMesh)
        {
            var names = MatchingNames(target, baseMesh);

            var blendShapeSync = target.GetComponent<ModularAvatarBlendshapeSync>();
            if (blendShapeSync == null)
            {
                blendShapeSync = Undo.AddComponent<ModularAvatarBlendshapeSync>(target.gameObject);
            }
            else
            {
                Undo.RecordObject(blendShapeSync, "Update ModularAvatarBlendshapeSync");
            }

            foreach (var name in names)
            {
                var referenceMesh = new AvatarObjectReference();
                referenceMesh.Set(baseMesh.gameObject);
                var binding = new BlendshapeBinding
                {
                    ReferenceMesh = referenceMesh,
                    Blendshape = name,
                    LocalBlendshape = "",
                };
                var existIndex = blendShapeSync.Bindings.FindIndex(b => b.LocalBlendshape == name || (string.IsNullOrEmpty(b.LocalBlendshape) && b.Blendshape == name));
                if (existIndex >= 0)
                {
                    blendShapeSync.Bindings[existIndex] = binding;
                }
                else
                {
                    blendShapeSync.Bindings.Add(binding);
                }
            }

            EditorUtility.SetDirty(blendShapeSync);

            return names.Count;
        }
    }
}
