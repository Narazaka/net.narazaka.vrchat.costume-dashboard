using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace Narazaka.VRChat.CostumeDashboard.Editor
{
    /// <summary>クリップ/BlendTree 配下の全カーブを BindingInfo（解決済みパス×型×プロパティ×値要約）へ展開する</summary>
    public static class BindingExtractor
    {
        public static List<BindingInfo> Extract(Motion motion, string pathPrefix)
        {
            var result = new List<BindingInfo>();
            var seen = new HashSet<(string, string, string)>();
            ExtractInto(motion, pathPrefix, result, seen);
            return result;
        }

        static void ExtractInto(Motion motion, string pathPrefix, List<BindingInfo> result, HashSet<(string, string, string)> seen)
        {
            if (motion == null) return;
            if (motion is BlendTree tree)
            {
                foreach (var child in tree.children) ExtractInto(child.motion, pathPrefix, result, seen);
                return;
            }
            if (!(motion is AnimationClip clip)) return;

            foreach (var binding in AnimationUtility.GetCurveBindings(clip))
            {
                if (!seen.Add((binding.path, binding.type.Name, binding.propertyName))) continue;
                var curve = AnimationUtility.GetEditorCurve(clip, binding);
                result.Add(Build(binding, pathPrefix, SummarizeCurve(curve)));
            }
            foreach (var binding in AnimationUtility.GetObjectReferenceCurveBindings(clip))
            {
                if (!seen.Add((binding.path, binding.type.Name, binding.propertyName))) continue;
                var keys = AnimationUtility.GetObjectReferenceCurve(clip, binding);
                result.Add(Build(binding, pathPrefix, SummarizeObjectCurve(keys)));
            }
        }

        static BindingInfo Build(EditorCurveBinding binding, string pathPrefix, string valueSummary)
        {
            var category = Categorize(binding);
            return new BindingInfo
            {
                // Humanoid（Animator 直・パス空）はパス概念がないので null のまま
                Path = category == BindingCategory.Humanoid ? null : ResolvePath(binding.path, pathPrefix),
                Type = binding.type.Name,
                Property = binding.propertyName,
                Category = category,
                ValueSummary = valueSummary,
            };
        }

        static string ResolvePath(string path, string pathPrefix)
        {
            if (string.IsNullOrEmpty(pathPrefix)) return path;
            return string.IsNullOrEmpty(path) ? pathPrefix : pathPrefix + "/" + path;
        }

        static BindingCategory Categorize(EditorCurveBinding binding)
        {
            if (binding.type == typeof(GameObject) && binding.propertyName == "m_IsActive") return BindingCategory.GameObjectActive;
            if (binding.type == typeof(Animator) && string.IsNullOrEmpty(binding.path)) return BindingCategory.Humanoid;
            if (binding.propertyName.StartsWith("blendShape.")) return BindingCategory.BlendShape;
            if (binding.propertyName.StartsWith("material.")) return BindingCategory.MaterialProperty;
            if (binding.propertyName.StartsWith("m_Materials.Array")) return BindingCategory.MaterialSwap;
            if (binding.type == typeof(Transform)) return BindingCategory.Transform;
            return BindingCategory.Other;
        }

        static string SummarizeCurve(AnimationCurve curve)
        {
            if (curve == null || curve.keys.Length == 0) return "";
            var first = curve.keys[0].value;
            var last = curve.keys[curve.keys.Length - 1].value;
            if (curve.keys.All(k => Mathf.Approximately(k.value, first))) return Format(first);
            return Format(first) + "→" + Format(last);
        }

        static string SummarizeObjectCurve(ObjectReferenceKeyframe[] keys)
        {
            if (keys == null || keys.Length == 0) return "";
            var names = keys.Select(k => k.value != null ? k.value.name : "(なし)").ToList();
            if (names.Distinct().Count() == 1) return names[0];
            return string.Join("→", names);
        }

        static string Format(float value) => value.ToString("0.###", CultureInfo.InvariantCulture);
    }
}
