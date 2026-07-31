namespace Narazaka.VRChat.CostumeDashboard.Editor
{
    /// <summary>
    /// セットアップ処理中に発生した1件の失敗・対応不可を表す共通の値。
    ///
    /// 抽出済みの Setup API 群は `string` / タプル / 専用クラス等バラバラな形で失敗を表現しており、
    /// エージェント経路（GUI 以外）から呼び出した結果を JSON へ変換する段になるとこの数だけ別々の
    /// マッピングコードが要る。ここへ寄せることで失敗表現の JSON マッピングを一本化する。
    /// </summary>
    public class SetupIssue
    {
        /// <summary>失敗の対象（相対パス・グループ表示名など。API により意味が異なる）</summary>
        public string Target;
        /// <summary>スロット単位の失敗のときのスロット番号。スロット単位でないときは -1</summary>
        public int SlotIndex = -1;
        public string Reason;

        public override string ToString()
        {
            var target = string.IsNullOrEmpty(Target) ? "(ルート)" : Target;
            return SlotIndex >= 0 ? $"{target} [スロット{SlotIndex}] {Reason}" : $"{target} {Reason}";
        }
    }
}
