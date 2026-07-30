using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor;

namespace Narazaka.VRChat.CostumeDashboard.Editor.Test
{
    /// <summary>
    /// Undo 登録操作（Setup 系の Undo.AddComponent / Undo.DestroyObjectImmediate 等）を行うテストの基底。
    /// Unity Test Framework は EditMode テスト後に Undo スタックを巻き戻すため、テストが素の
    /// DestroyImmediate で破棄したオブジェクトへの Undo 操作が残っていると、破棄済みオブジェクトが
    /// ゾンビとしてシーンルートに復活し、テスト実行のたびにシーンにゴミが蓄積する。
    ///
    /// 対策: テスト開始時（基底 SetUp は派生より先に実行される）に Undo グループを区切り、
    /// TearDown（基底は派生より後に実行される）で「テスト中に積まれた操作だけ」を
    /// オブジェクトが生きているうちに RevertAllDownToGroup で巻き戻してから、
    /// Track() 登録されたフィクスチャオブジェクトを破棄する。ユーザーのエディタ Undo 履歴には触れない。
    ///
    /// 注意: 派生クラス側では Undo 操作の対象になりうるオブジェクトを素の DestroyImmediate で
    /// 破棄しないこと（巻き戻し前の破棄はゾンビ復活の原因に戻る）。破棄は Track() に寄せる。
    /// </summary>
    public abstract class UndoCleanupTestBase
    {
        int undoGroup;
        readonly List<UnityEngine.Object> tracked = new List<UnityEngine.Object>();

        [SetUp]
        public void BeginUndoGroup()
        {
            Undo.IncrementCurrentGroup();
            undoGroup = Undo.GetCurrentGroup();
        }

        [TearDown]
        public void RevertUndoAndDestroyTracked()
        {
            Undo.RevertAllDownToGroup(undoGroup);
            foreach (var obj in tracked)
            {
                if (obj != null) UnityEngine.Object.DestroyImmediate(obj);
            }
            tracked.Clear();
        }

        /// <summary>テスト終了時（Undo 巻き戻し後）に破棄するオブジェクトを登録する</summary>
        protected T Track<T>(T obj) where T : UnityEngine.Object
        {
            tracked.Add(obj);
            return obj;
        }
    }
}
