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

        /// <summary>host を対象とした Toggle Menu を作成する。slots のメッシュを ON=表示の対象にし、
        /// 対象メッシュ配下の移設済み Reactive Component は ON=表示＋変化待機99% で自動包含する
        /// （フェード完了直前まで適用を遅延させ、フェード中に素体の変化が見えるのを防ぐ）。
        /// host をどう用意するか（新規作成か既存流用か等）は呼び出し側の責務</summary>
        public static AvatarToggleMenuCreator CreateForSlots(GameObject host, GameObject avatarRoot,
            List<SlotInfo> slots, IReadOnlyDictionary<int, FadeFrame> frameOverrides, float transitionSeconds)
        {
            var togglePaths = slots
                .Where(s => s.Renderer != null)
                .Select(s => AvatarUtil.RelativePath(avatarRoot, s.Renderer.gameObject))
                .Where(p => !string.IsNullOrEmpty(p))
                .Distinct()
                .ToList();
            var fades = BuildFadeTargets(avatarRoot, slots, frameOverrides);

            // 対象メッシュ配下の移設済み Reactive Component（(ホスト名)_reactive）を
            // ON=表示＋変化待機99% で自動包含する（フェード完了直前まで適用を遅延させる）
            var reactiveWaitPaths = slots
                .Select(s => s.Renderer).Where(r => r != null).Distinct()
                .SelectMany(r => ReactiveComponentSetup.Scan(r.gameObject).Where(ReactiveComponentSetup.IsRelocated))
                .Select(c => AvatarUtil.RelativePath(avatarRoot, c.gameObject))
                .Where(p => !string.IsNullOrEmpty(p))
                .Distinct()
                .ToList();

            return Create(host, togglePaths, fades, transitionSeconds, reactiveWaitPaths);
        }

        /// <summary>衣装配下に menuName の新規ホストを作り、上の <see cref="CreateForSlots(GameObject, GameObject, List{SlotInfo}, IReadOnlyDictionary{int, FadeFrame}, float)"/> へ委譲する。
        /// ホストは常に新規作成する（既存ホストの探索・再利用はしない）</summary>
        public static AvatarToggleMenuCreator CreateForSlots(GameObject costume, GameObject avatarRoot,
            List<SlotInfo> slots, IReadOnlyDictionary<int, FadeFrame> frameOverrides, string menuName, float transitionSeconds)
        {
            var host = new GameObject(menuName);
            host.transform.SetParent(costume.transform, false);
            Undo.RegisterCreatedObjectUndo(host, "Create Toggle Menu");
            return CreateForSlots(host, avatarRoot, slots, frameOverrides, transitionSeconds);
        }

        public static AvatarToggleMenuCreator Create(GameObject host, IEnumerable<string> togglePaths, IEnumerable<FadeTarget> fades, float transitionSeconds, IEnumerable<string> reactiveWaitPaths = null)
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

            if (reactiveWaitPaths != null)
            {
                foreach (var path in reactiveWaitPaths)
                {
                    RegisterReactiveWait(menu, path);
                }
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

        /// <summary>Reactive Component 移設先オブジェクトを ON=表示＋変化待機99% で既存メニューに登録する。
        /// フェード ON 遷移の99%時点（完了直前）まで Reactive Component の適用を遅延させるため</summary>
        public static void RegisterReactiveWait(AvatarToggleMenuCreator creator, string path)
        {
            Undo.RecordObject(creator, "Register Reactive Wait");
            RegisterReactiveWait(creator.AvatarToggleMenu, path);
            EditorUtility.SetDirty(creator);
        }

        static void RegisterReactiveWait(net.narazaka.avatarmenucreator.AvatarToggleMenu menu, string path)
        {
            menu.ToggleObjects[path] = ToggleType.ON;
            menu.ToggleObjectTransitionOffsetPercents[path] = ReactiveComponentSetup.WaitOffsetPercent;
        }

        /// <summary>path を対象とする項目（ToggleObjects・変化待機・フェード等の全エントリ）を既存メニューから取り除く。
        /// Reactive Component 移設先オブジェクトの削除時に孤児エントリを残さないため</summary>
        public static void UnregisterPath(AvatarToggleMenuCreator creator, string path)
        {
            Undo.RecordObject(creator, "Unregister Toggle Path");
            creator.AvatarToggleMenu.RemoveStoredChild(path);
            EditorUtility.SetDirty(creator);
        }

        /// <summary>
        /// Toggle Menu 作成対象スロットを検証する。対象が1件も無ければ NG（Reason:
        /// 「✓ 列でメッシュをチェックしてください」）。アバタールートが複数種類に分かれる、または
        /// アバタールートが解決できないスロットがあれば NG（Reason:
        /// 「チェックしたメッシュは同一アバター配下である必要があります」）。
        /// 判定内容・文言は旧 CostumeDashboardWindow.CreateToggleMenu と同一
        /// </summary>
        public static (bool Ok, string Reason, GameObject AvatarRoot) ValidateSlots(IReadOnlyList<(SlotInfo Slot, GameObject Costume, GameObject AvatarRoot)> slots)
        {
            if (slots.Count == 0)
            {
                return (false, "✓ 列でメッシュをチェックしてください", null);
            }
            var avatarRoots = slots.Select(s => s.AvatarRoot).Distinct().ToList();
            if (avatarRoots.Count != 1 || avatarRoots[0] == null)
            {
                return (false, "チェックしたメッシュは同一アバター配下である必要があります", null);
            }
            return (true, null, avatarRoots[0]);
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
