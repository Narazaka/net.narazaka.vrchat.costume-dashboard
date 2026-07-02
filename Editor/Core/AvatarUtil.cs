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
