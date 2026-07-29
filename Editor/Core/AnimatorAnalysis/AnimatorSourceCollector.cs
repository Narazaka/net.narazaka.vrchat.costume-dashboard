using System.Collections.Generic;
using System.Linq;
using UnityEditor.Animations;
using UnityEngine;
using VRC.SDK3.Avatars.Components;
using VRC.SDK3.Avatars.ScriptableObjects;
using nadena.dev.modular_avatar.core;

namespace Narazaka.VRChat.CostumeDashboard.Editor
{
    public class AnimatorSource
    {
        public SourceInfo Info;
        public AnimatorController Controller;
        public string PathPrefix;
        /// <summary>解析不能理由（Controller 未設定 / AnimatorOverrideController 等）。非 null のとき Controller は null</summary>
        public string Warning;
    }

    /// <summary>解析対象の AnimatorController ソースを列挙する。
    /// 既定は登録衣装配下の MA Merge Animator のみ、オプションで Descriptor playable layers + 衣装外 Merge Animator</summary>
    public static class AnimatorSourceCollector
    {
        public static List<AnimatorSource> CollectCostumeSources(GameObject avatarRoot, IEnumerable<GameObject> costumeRoots)
        {
            var result = new List<AnimatorSource>();
            foreach (var costume in costumeRoots)
            {
                if (costume == null) continue;
                foreach (var merge in costume.GetComponentsInChildren<ModularAvatarMergeAnimator>(true))
                {
                    if (AvatarUtil.IsEditorOnly(merge.gameObject, avatarRoot)) continue;
                    result.Add(FromMergeAnimator(avatarRoot, merge));
                }
            }
            return result;
        }

        public static List<AnimatorSource> CollectAvatarSources(GameObject avatarRoot, IEnumerable<GameObject> costumeRoots)
        {
            var result = new List<AnimatorSource>();
            if (avatarRoot == null) return result;
            var descriptor = avatarRoot.GetComponent<VRCAvatarDescriptor>();
            if (descriptor != null)
            {
                var layers = (descriptor.baseAnimationLayers ?? new VRCAvatarDescriptor.CustomAnimLayer[0])
                    .Concat(descriptor.specialAnimationLayers ?? new VRCAvatarDescriptor.CustomAnimLayer[0]);
                foreach (var layer in layers)
                {
                    if (layer.isDefault || layer.animatorController == null) continue;
                    var source = new AnimatorSource
                    {
                        Info = new SourceInfo
                        {
                            Kind = AnimatorSourceKind.PlayableLayer,
                            LayerType = layer.type.ToString(),
                            ControllerName = layer.animatorController.name,
                        },
                        PathPrefix = "",
                    };
                    if (layer.animatorController is AnimatorController ac) source.Controller = ac;
                    else source.Warning = "AnimatorOverrideController は解析対象外";
                    result.Add(source);
                }
            }
            // 衣装外の Merge Animator（衣装配下は CollectCostumeSources 側で列挙する）
            var costumeSet = costumeRoots.Where(c => c != null).ToList();
            foreach (var merge in avatarRoot.GetComponentsInChildren<ModularAvatarMergeAnimator>(true))
            {
                if (AvatarUtil.IsEditorOnly(merge.gameObject, avatarRoot)) continue;
                if (costumeSet.Any(c => merge.transform == c.transform || merge.transform.IsChildOf(c.transform))) continue;
                result.Add(FromMergeAnimator(avatarRoot, merge));
            }
            return result;
        }

        static AnimatorSource FromMergeAnimator(GameObject avatarRoot, ModularAvatarMergeAnimator merge)
        {
            var source = new AnimatorSource
            {
                Info = new SourceInfo
                {
                    Kind = AnimatorSourceKind.MergeAnimator,
                    LayerType = merge.layerType.ToString(),
                    ComponentPath = AvatarUtil.RelativePath(avatarRoot, merge.gameObject),
                    ControllerName = merge.animator != null ? merge.animator.name : null,
                },
            };
            if (merge.animator == null)
            {
                source.Warning = "AnimatorController 未設定";
            }
            else if (merge.animator is AnimatorController ac)
            {
                source.Controller = ac;
                source.PathPrefix = ResolvePathPrefix(avatarRoot, merge);
            }
            else
            {
                source.Warning = "AnimatorOverrideController は解析対象外";
            }
            return source;
        }

        /// <summary>Relative パスモードの基準オブジェクト: relativePathRoot 設定時はそれ、未設定はコンポーネントのオブジェクト自身（MA 仕様）</summary>
        static string ResolvePathPrefix(GameObject avatarRoot, ModularAvatarMergeAnimator merge)
        {
            if (merge.pathMode == MergeAnimatorPathMode.Absolute) return "";
            GameObject rootGo = null;
            if (merge.relativePathRoot != null) rootGo = merge.relativePathRoot.Get(merge);
            if (rootGo == null) rootGo = merge.gameObject;
            return AvatarUtil.RelativePath(avatarRoot, rootGo) ?? "";
        }

        public static List<AnimatorLayerModel> BuildModels(AnimatorSource source)
        {
            var result = new List<AnimatorLayerModel>();
            if (source.Controller == null) return result;
            for (var i = 0; i < source.Controller.layers.Length; i++)
            {
                result.Add(LayerModelBuilder.Build(source.Controller, i, source.PathPrefix ?? "", source.Info));
            }
            return result;
        }

        public static void AnnotateExpressionParameters(AnimatorLayerModel model, VRCExpressionParameters parameters)
        {
            if (parameters == null || parameters.parameters == null) return;
            foreach (var p in model.Parameters)
            {
                var found = parameters.parameters.FirstOrDefault(x => x != null && x.name == p.Name);
                if (found == null) continue;
                p.InExpressionParameters = true;
                p.Saved = found.saved;
                p.Synced = found.networkSynced;
            }
        }

        public static void AnnotateAffectedCostumes(AnimatorLayerModel model, GameObject avatarRoot, IEnumerable<GameObject> costumeRoots)
        {
            model.AffectedCostumes.Clear();
            foreach (var costume in costumeRoots)
            {
                if (costume == null) continue;
                var costumePath = AvatarUtil.RelativePath(avatarRoot, costume);
                if (costumePath == null) continue;
                var hit = model.States.SelectMany(s => s.Bindings).Any(b =>
                    b.Path != null && (b.Path == costumePath || b.Path.StartsWith(costumePath + "/")));
                if (hit) model.AffectedCostumes.Add(costume.name);
            }
        }
    }
}
