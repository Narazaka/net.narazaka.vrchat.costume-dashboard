using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using net.narazaka.avatarmenucreator;
using net.narazaka.avatarmenucreator.components;

namespace Narazaka.VRChat.CostumeDashboard.Editor
{
    public static class ToggleMenuSetup
    {
        public class FadeTarget
        {
            /// <summary>アバタールート相対パス（root 名プレフィクスなし）</summary>
            public string MeshPath;
            public FadeFrame Frame;
        }

        public static AvatarToggleMenuCreator Create(GameObject host, IEnumerable<string> togglePaths, IEnumerable<FadeTarget> fades, float transitionSeconds)
        {
            var creator = host.GetComponent<AvatarToggleMenuCreator>();
            if (creator == null) creator = Undo.AddComponent<AvatarToggleMenuCreator>(host);
            else Undo.RecordObject(creator, "Setup Toggle Menu");

            var menu = creator.AvatarToggleMenu;
            menu.TransitionSeconds = transitionSeconds;
            menu.Saved = true;
            menu.Synced = true;
            menu.ToggleDefaultValue = true;

            foreach (var path in togglePaths)
            {
                menu.ToggleObjects[path] = ToggleType.ON;
            }

            foreach (var fade in fades)
            {
                switch (fade.Frame)
                {
                    case FadeFrame.Third:
                        menu.ToggleShaderVectorParameters[(fade.MeshPath, "_Color3rd")] = FadeVector();
                        break;
                    case FadeFrame.Second:
                        menu.ToggleShaderVectorParameters[(fade.MeshPath, "_Color2nd")] = FadeVector();
                        break;
                    case FadeFrame.AlphaMask:
                        menu.ToggleShaderParameters[(fade.MeshPath, "_AlphaMaskValue")] = new ToggleBlendShape
                        {
                            Inactive = -1f,
                            Active = 0f,
                            TransitionOffsetPercent = 0f,
                            TransitionDurationPercent = 100f,
                        };
                        break;
                }
            }

            EditorUtility.SetDirty(creator);
            return creator;
        }

        static ToggleVector4 FadeVector() => new ToggleVector4
        {
            Inactive = new Vector4(1, 1, 1, 0),
            Active = new Vector4(1, 1, 1, 1),
            TransitionOffsetPercent = 0f,
            TransitionDurationPercent = 100f,
        };
    }
}
