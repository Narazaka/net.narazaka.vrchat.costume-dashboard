using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using nadena.dev.modular_avatar.core;

namespace Narazaka.VRChat.CostumeDashboard.Editor
{
    /// <summary>
    /// MA Reactive Component（Shape Changer / Object Toggle / Mesh Cutter 等 ReactiveComponent 派生）の
    /// フェード対応。Reactive Component がメッシュ自身に付いていると、フェード中（メッシュはアクティブ）に
    /// 素体の shrink 等が即座に適用されて見えてしまう。ホスト配下の (ホスト名)_reactive 子オブジェクトへ
    /// 移設し、Toggle Menu で ON=表示＋変化待機99% にすることで、フェード完了直前まで適用を遅延させる
    /// </summary>
    public static class ReactiveComponentSetup
    {
        public const string RelocateSuffix = "_reactive";
        /// <summary>移設先オブジェクトの変化待機%。ON遷移は99%時点でアクティブ化（フェード完了直前）、
        /// OFF遷移はビルド側の対称化により (100-99)=1% 時点で非アクティブ化（フェード開始直後に解除）</summary>
        public const float WaitOffsetPercent = 99f;

        /// <summary>costumeRoot 配下（非アクティブ含む）の全 Reactive Component。EditorOnly 配下は除外</summary>
        public static List<ReactiveComponent> Scan(GameObject costumeRoot)
        {
            var result = new List<ReactiveComponent>();
            if (costumeRoot == null) return result;
            foreach (var comp in costumeRoot.GetComponentsInChildren<ReactiveComponent>(true))
            {
                if (AvatarUtil.IsEditorOnly(comp.gameObject, costumeRoot)) continue;
                result.Add(comp);
            }
            return result;
        }

        /// <summary>移設済み（(ホスト名)_reactive 子オブジェクト上）か</summary>
        public static bool IsRelocated(ReactiveComponent comp) =>
            comp != null && comp.gameObject.name.EndsWith(RelocateSuffix);

        /// <summary>ホスト GameObject 直下の (ホスト名)_reactive 子オブジェクト（無ければ作成）へ移動する。
        /// 移動後のコンポーネントを返す（移設済みならそのまま返す）</summary>
        public static ReactiveComponent Relocate(ReactiveComponent comp)
        {
            if (IsRelocated(comp)) return comp;
            var host = comp.gameObject;
            var childName = host.name + RelocateSuffix;
            var childTransform = host.transform.Find(childName);
            GameObject child;
            if (childTransform == null)
            {
                child = new GameObject(childName);
                child.transform.SetParent(host.transform, false);
                Undo.RegisterCreatedObjectUndo(child, "Relocate Reactive Component");
            }
            else
            {
                child = childTransform.gameObject;
            }
            UnityEditorInternal.ComponentUtility.CopyComponent(comp);
            var moved = (ReactiveComponent)Undo.AddComponent(child, comp.GetType());
            UnityEditorInternal.ComponentUtility.PasteComponentValues(moved);
            Undo.DestroyObjectImmediate(comp);
            EditorUtility.SetDirty(moved);
            return moved;
        }

        /// <summary>削除する。移設先 (_reactive) 上の最後の Reactive Component だった場合は空になった
        /// 移設先オブジェクトも削除し、そのアバタールート相対パスを返す（呼び出し側で Toggle Menu の
        /// 孤児エントリ掃除に使う）。それ以外（非移設・他コンポーネント残存・ユーザーが子や別コンポーネントを
        /// 足していた場合）は null</summary>
        public static string Remove(ReactiveComponent comp, GameObject avatarRoot)
        {
            var relocated = IsRelocated(comp);
            var host = comp.gameObject;
            Undo.DestroyObjectImmediate(comp);
            if (!relocated) return null;
            if (host.GetComponents<ReactiveComponent>().Length > 0) return null;
            // 移設先には Transform 以外を置かない運用のため空になったら削除する。
            // ユーザーが手動で別コンポーネントや子オブジェクトを足していた場合は削除しない
            if (host.GetComponents<Component>().Length > 1 || host.transform.childCount > 0) return null;
            var path = avatarRoot != null ? AvatarUtil.RelativePath(avatarRoot, host) : null;
            Undo.DestroyObjectImmediate(host);
            return string.IsNullOrEmpty(path) ? null : path;
        }

        /// <summary>移設＋既存 Toggle Menu への配線: 移設先の祖先（メッシュ等）を toggle 対象に含む
        /// 既存メニューへ ON=表示＋変化待機99% を登録する</summary>
        public static ReactiveComponent RelocateAndWire(ReactiveComponent comp, GameObject avatarRoot)
        {
            var moved = Relocate(comp);
            if (avatarRoot == null) return moved;
            var childPath = AvatarUtil.RelativePath(avatarRoot, moved.gameObject);
            if (string.IsNullOrEmpty(childPath)) return moved;
            foreach (var (creator, targets) in ToggleMenuSetup.CollectMenuTargets(avatarRoot))
            {
                if (targets.Any(t => childPath.StartsWith(t + "/")))
                {
                    ToggleMenuSetup.RegisterReactiveWait(creator, childPath);
                }
            }
            return moved;
        }

        /// <summary>削除＋孤児掃除: 移設先 (_reactive) が空になってオブジェクトごと削除された場合、
        /// 既存 Toggle Menu に登録済みの該当エントリ（ON＋変化待機99%）も取り除く</summary>
        public static string RemoveAndUnwire(ReactiveComponent comp, GameObject avatarRoot)
        {
            var orphanPath = Remove(comp, avatarRoot);
            if (orphanPath == null || avatarRoot == null) return orphanPath;
            foreach (var (creator, targets) in ToggleMenuSetup.CollectMenuTargets(avatarRoot))
            {
                if (targets.Contains(orphanPath)) ToggleMenuSetup.UnregisterPath(creator, orphanPath);
            }
            return orphanPath;
        }
    }
}
