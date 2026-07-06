# Costume Dashboard UX改訂第6弾 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 視認性改善: (1) 操作系列をオブジェクト列直後へ移動＋メッシュ/グループ行の背景 tint (2) [Q] ボタンの設定済み緑表示 (3) シェーダー/AO ME/グループ表示名の日本語化（ホスト GameObject 名は ASCII のまま）。

**Spec:** `docs~/superpowers/specs/2026-07-02-costume-dashboard-design.md`「視認性改善（UX改訂6）」

## Global Constraints

- 既存の Global Constraints を全て引き継ぐ。**git ステージは明示パス指定のみ**（`-A`/`.`/`-u` 禁止、status --short 確認後コミット）。モーダルダイアログは `dialog status` 報告で停止（回避操作禁止）
- 検証手順・CLI・`--on-dialog cancel`・benign エラー3種は従来どおり。現テスト数 89
- bindCell 規律（スタイルは毎 bind 無条件リセット）維持
- **表示名マッピング（スペックと一致させる）**:
  - variant: opaque=不透明 / cutout=カットアウト / trans=半透明 / onetrans=半透明 1パス / twotrans=半透明 2パス / unknown=不明
  - `_o` 付き → 末尾に「 Outline」
  - family 修飾: lilToon_std=（無印）/ lilToon_tess=「 Tess」/ lilToon_lite=「 Lite」/ lilToon_multi=「 Multi」/ motchiri_std=「もっちり 」接頭 / motchiri_tess=「もっちり 」接頭+「 Tess」
  - multi は実効 `_TransparentMode` から variant 日本語を決める（0=不透明, 1=カットアウト, 2=半透明, 3以上=対象外表示のまま）+「 Multi」
  - 枠: Main=main / AlphaMask=Alpha / Third=3rd / Second=2nd
  - 調整: Neutralize=「 (マスク無効化)」/ ToMultiply=「 (マスク乗算化)」
  - グループ表示名 = variant表示名 + 「 → 」 + 枠 + 調整（枠なしグループは variant のみ + 対象外理由）

---

### Task 1: DisplayNames ヘルパ（Core）

**Files:**
- Create: `Editor/Core/DisplayNames.cs`
- Test: `Test/DisplayNamesTest.cs`

**Interfaces:**
```csharp
    public static class DisplayNames
    {
        /// <summary>スロットのシェーダー表示名（例: 不透明 Outline Tess / 半透明 Multi / もっちり カットアウト）</summary>
        public static string Shader(SlotInfo slot);
        /// <summary>variant/family/multiTm からの表示名（Shader(slot) の実体。テスト容易性のため分離）</summary>
        public static string Variant(string family, string variant, int multiTransparentMode);
        /// <summary>フェード枠の表示名: main / Alpha / 3rd / 2nd</summary>
        public static string Frame(FadeFrame frame);
        /// <summary>AO ME グループ表示名（例: 半透明 2パス → Alpha (マスク乗算化)）。Preset null は variant のみ</summary>
        public static string Group(SlotGroup group);
    }
```
- unknown family: `Variant` は「不明」+（shader名は呼び出し側で補う）。`Shader(slot)` は unknown のとき従来どおり shader 名を返す（既存 FormatShader の unknown 分岐を移設）

- [ ] **Step 1: テスト（RED）**: `Variant_Opaque_O_Tess`（lilToon_tess, opaque_o → "不透明 Outline Tess"）/ `Variant_TwoTrans`（lilToon_std, twotrans → "半透明 2パス"）/ `Variant_Motchiri_Cutout`（motchiri_std, cutout → "もっちり カットアウト"）/ `Variant_Multi_Trans`（lilToon_multi, multi_o, tm=2 → "半透明 Multi Outline" ※Outline/Multi の並びは実装定義でよいが assert と一致させる）/ `Frame_AlphaMask_IsAlpha` / `Group_WithAdjust`（twotrans + AlphaMask + ToMultiply → "半透明 2パス → Alpha (マスク乗算化)"）
- [ ] **Step 2-4: RED → 実装 → GREEN**（89 → 95）
- [ ] **Step 5: コミット** `DisplayNames: シェーダー/枠/グループの日本語表示名`

---

### Task 2: UI — 列順・行背景・Q緑・表示名適用

**Files:**
- Modify: `Editor/UI/CostumeDashboardWindow.cs`

**要求:**
1. **列順変更**: オブジェクト / Select / ✓ / 操作 / 枠セレクタ / BS数+[BS Sync]（操作列に含まれるならそのまま） / スロット / マテリアル / シェーダー / main/AM/3rd/2nd / 推奨 / AO ME / Queue の順（Make*Column 呼び出し順の並べ替えのみ。列定義自体は不変）
2. **行背景 tint**: メッシュ行 = 例 `new Color(0.25f, 0.30f, 0.38f, 0.35f)`、グループ行 = 例 `new Color(0.32f, 0.27f, 0.38f, 0.35f)`、それ以外 = `StyleKeyword.Null`。全列の bindCell 冒頭で共通ヘルパ `ApplyRowTint(VisualElement cell, Row row)` を呼ぶ（無条件リセット）。既存の警告色（枠セレクタ）はセル個別スタイルなので共存可
3. **[Q] 緑表示**: メッシュ行 [Q] = `renderer.GetComponents<CRQ>().Length > 0` で緑＋「Q✓」表記、スロット行 [Q] = `RenderQueueSetup.EffectiveQueue(renderer, slot, out var src)` の `src != null` で緑＋「Q✓」。色は既存 ConfiguredColor を共用、毎 bind 無条件リセット
4. **表示名適用**: シェーダー列 → `DisplayNames.Shader(slot)`、AO ME 列 → `DisplayNames.Group(group)`（✓ 前置と — は従来）、AO MEビューのグループ行ラベル → `DisplayNames.Group(group)` + スロット数（従来の family/variant 生文字列を置換。理由表示は従来どおり）。ツールチップに従来の ASCII ホスト suffix（`AOMEHostSuffix(group)`）を出して GameObject 名と対応付けられるようにする
5. 挙動（作成・判定・ホスト名）は一切不変。regression 95/95

検証: compile → 実データスモーク（両ビュー） → regression 95/95

- [ ] **Step 1: 実装** → **Step 2: compile+smoke+regression** → **Step 3: コミット** `列順・行背景・Q設定済み表示・日本語表示名を適用`

---

### Task 3: README 追記と最終確認

- [ ] README 機能セクションの表示関連記述を更新（日本語表示名・設定済み表示の拡充）
- [ ] compile + 全テスト GREEN（95/95）
- [ ] コミット `README更新（UX改訂6）`
