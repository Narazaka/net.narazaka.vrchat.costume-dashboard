using System.Collections.Generic;
using UnityEngine;
using VRC.SDK3.Avatars.Components;

namespace Narazaka.VRChat.CostumeDashboard.Editor
{
    public static class AvatarUtil
    {
        public static GameObject FindAvatarRoot(GameObject any)
        {
            if (any == null) return null;
            for (var t = any.transform; t != null; t = t.parent)
            {
                if (t.GetComponent<VRCAvatarDescriptor>() != null) return t.gameObject;
            }
            return null;
        }

        /// <summary>go から limit（含む）まで親を辿り、いずれかが EditorOnly タグなら true。
        /// EditorOnly はビルド時に自身と配下ごと除去されアップロード後のアバターに存在しないため、
        /// EditorOnly 配下のメッシュも改変ツールの対象外（扱わない）とする。</summary>
        public static bool IsEditorOnly(GameObject go, GameObject limit)
        {
            if (go == null) return false;
            for (var t = go.transform; t != null; t = t.parent)
            {
                if (t.gameObject.CompareTag("EditorOnly")) return true;
                if (limit != null && t.gameObject == limit) break;
            }
            return false;
        }

        public static string RelativePath(GameObject root, GameObject target)
        {
            if (root == null || target == null) return null;
            if (root == target) return "";
            var names = new List<string>();
            for (var t = target.transform; t != null; t = t.parent)
            {
                if (t.gameObject == root)
                {
                    names.Reverse();
                    return string.Join("/", names);
                }
                names.Add(t.name);
            }
            return null;
        }
    }
}
