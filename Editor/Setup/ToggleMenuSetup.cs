using System.Collections.Generic;
using System.Linq;
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

        /// <summary>チェック対象スロット群からフェード駆動対象を構築する。(meshPath, Frame) 単位で重複除去する</summary>
        public static List<FadeTarget> BuildFadeTargets(GameObject avatarRoot, IEnumerable<SlotInfo> slots) =>
            BuildFadeTargets(avatarRoot, slots, null);

        /// <summary>
        /// チェック対象スロット群からフェード駆動対象を構築する。マテリアルプロパティアニメーションは
        /// レンダラー単位でしかスロットを選べないため、レンダラーごとにグループ化し実効枠を1つだけ決める
        /// （1レンダラーにつき最大1 FadeTarget。同一 meshPath の重複は自動的に成立しなくなる）。
        /// frameOverrides はキーが Renderer.GetInstanceID() の実効枠上書き（UI 側のカスタム枠選択等）。
        /// 実効枠 = override があればそれ、なければそのレンダラーの全スロットに対する
        /// FadeCompatChecker.CommonRecommended。実効枠が null のレンダラーはスキップする
        /// </summary>
        public static List<FadeTarget> BuildFadeTargets(GameObject avatarRoot, IEnumerable<SlotInfo> slots, IReadOnlyDictionary<int, FadeFrame> frameOverrides)
        {
            var fades = new List<FadeTarget>();
            foreach (var group in slots.Where(s => s.Renderer != null).GroupBy(s => s.Renderer))
            {
                var renderer = group.Key;
                FadeFrame? frame = null;
                if (frameOverrides != null && frameOverrides.TryGetValue(renderer.GetInstanceID(), out var overrideFrame))
                {
                    frame = overrideFrame;
                }
                else
                {
                    frame = FadeCompatChecker.CommonRecommended(group);
                }
                if (frame == null) continue;
                var meshPath = AvatarUtil.RelativePath(avatarRoot, renderer.gameObject);
                if (string.IsNullOrEmpty(meshPath)) continue;
                fades.Add(new FadeTarget { MeshPath = meshPath, Frame = frame.Value });
            }
            return fades;
        }

        /// <summary>
        /// avatarRoot 配下（非アクティブ含む）の全 AvatarToggleMenuCreator と、それぞれの対象パス集合
        /// （ToggleObjects のキー、ToggleShaderVectorParameters / ToggleShaderParameters のキー Item1）を1回の走査で収集する。
        /// アバター全体走査を伴うため、呼び出し側（UI の Refresh 等）で結果をキャッシュし、
        /// メッシュ単位の判定ごとに呼び直さないこと
        /// </summary>
        public static List<(AvatarToggleMenuCreator Creator, HashSet<string> TargetPaths)> CollectMenuTargets(GameObject avatarRoot)
        {
            var result = new List<(AvatarToggleMenuCreator, HashSet<string>)>();
            if (avatarRoot == null) return result;
            foreach (var creator in avatarRoot.GetComponentsInChildren<AvatarToggleMenuCreator>(true))
            {
                var menu = creator.AvatarToggleMenu;
                var targets = new HashSet<string>(menu.ToggleObjects.Keys);
                foreach (var key in menu.ToggleShaderVectorParameters.Keys) targets.Add(key.Item1);
                foreach (var key in menu.ToggleShaderParameters.Keys) targets.Add(key.Item1);
                result.Add((creator, targets));
            }
            return result;
        }

        /// <summary>
        /// アバタールート配下（非アクティブ含む）の全 AvatarToggleMenuCreator のうち、
        /// renderer を対象としているものを返す（ToggleObjects のキー、または
        /// ToggleShaderVectorParameters / ToggleShaderParameters のキー Item1 が
        /// renderer のアバタールート相対パスと一致するもの）。
        /// 内部で CollectMenuTargets によるアバター全体走査を行うため、メッシュ行の bind ごとに
        /// 呼ぶような使い方は避けること（UI 側は CollectMenuTargets の結果を Refresh 単位でキャッシュする）
        /// </summary>
        public static List<AvatarToggleMenuCreator> FindMenusTargeting(GameObject avatarRoot, Renderer renderer)
        {
            var result = new List<AvatarToggleMenuCreator>();
            if (avatarRoot == null || renderer == null) return result;
            var meshPath = AvatarUtil.RelativePath(avatarRoot, renderer.gameObject);
            if (string.IsNullOrEmpty(meshPath)) return result;

            foreach (var (creator, targetPaths) in CollectMenuTargets(avatarRoot))
            {
                if (targetPaths.Contains(meshPath)) result.Add(creator);
            }
            return result;
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
                    case FadeFrame.Main:
                        menu.ToggleShaderVectorParameters[(fade.MeshPath, "_Color")] = FadeVector();
                        break;
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
