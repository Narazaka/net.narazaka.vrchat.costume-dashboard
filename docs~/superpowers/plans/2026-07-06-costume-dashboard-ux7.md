# Costume Dashboard UX改訂第7弾 Implementation Plan（小規模・列レイアウト）

> **For agentic workers:** REQUIRED SUB-SKILL: superpowers:subagent-driven-development / executing-plans。

**Goal:** 列レイアウト調整: (1) [BS Sync] ボタン表記を「BS」（設定済み「BS✓」）に短縮 (2) 枠セレクタ・BS・スロット等の列幅を必要最小限に (3) 列順を「オブジェクト / 選択 / ✓ / 操作 / Queue / 枠 / 推奨 / AO ME / スロット / main / AM / 3rd / 2nd / マテリアル / シェーダー / BS」に変更。

**Spec:** 画面節の列順記述をこの順に更新済みとみなす（本計画が正）。

## Global Constraints
- 既存の Global Constraints を全て引き継ぐ（明示パス add、ダイアログ停止方針、bindCell 規律）。現テスト数 95。挙動・判定・ホスト名は一切不変（表示のみ）

### Task 1: 列レイアウト調整
**Files:** Modify `Editor/UI/CostumeDashboardWindow.cs`
1. 操作列内の BS Sync ボタン: text を `"BS Sync"`→`"BS"`、設定済み `"BS✓"`（tooltip は従来のまま）
2. 列幅の最小化（目安。実機で崩れなければ調整可）: 選択 56 / ✓ 28 / 枠セレクタ 72 / 推奨 44 / AO ME 130 / スロット 34 / main・AM・3rd・2nd 各 30 / Queue 56 / BS 34 / マテリアル・シェーダーは現状維持
3. 列順: オブジェクト → 選択 → ✓ → 操作 → Queue → 枠セレクタ → 推奨 → AO ME → スロット → main → AM → 3rd → 2nd → マテリアル → シェーダー → BS（Make*Column 呼び出し順の並べ替えのみ）
4. 検証: compile → 実データスモーク（両ビュー、例外なし）→ regression 95/95 → コミット `列順と列幅を調整しBS Syncボタンを短縮`
