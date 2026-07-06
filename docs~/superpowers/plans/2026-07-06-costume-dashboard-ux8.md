# Costume Dashboard UX改訂第8弾 Implementation Plan（メッシュ単位推奨への統一）

> **For agentic workers:** REQUIRED SUB-SKILL: superpowers:subagent-driven-development / executing-plans。

**Goal:** マテリアルアニメーションがレンダラー単位である事実に合わせ、フェードメソッドの推奨・実効枠をメッシュ単位の「全スロット共通で利用可な枠」に統一する。

**Spec:** 機能仕様2「メッシュ単位の推奨（UX改訂8で変更）」

## Global Constraints
- 既存の Global Constraints を全て引き継ぐ（明示パス add、ダイアログ停止、bindCell 規律、クリーン単発テスト報告）。現テスト数 95
- **共通推奨の定義**: `CommonRecommended(slots)` = レンダラーの既知ファミリー（`Family.IsKnown && FadeCompat != null`）スロット全てで当該枠が `Compatible`（Warning 含む）となる最初の枠（優先度 Main > AlphaMask > Third > Second）。既知スロットが0件なら null。共通枠なしも null
- スロット別の ○△× 列は診断用に不変。**スロット行の推奨列は空欄化**し、メッシュ行に共通推奨（override 時 `*`）を表示
- メッシュ行の main/AM/3rd/2nd セルは**スロット集約表示**: 全○=○ / 全て利用可だがどれか△=△ / どれか×=× （tooltip に「不可: <スロット名>: <理由>」内訳）

---

### Task 1: Core — CommonRecommended とワイヤリング

**Files:**
- Modify: `Editor/Core/FadeCompatChecker.cs`（static helper 追加）または `Editor/Core/MaterialSlotScanner.cs` — 実装者判断（テスト容易な static 純関数として置く）
- Modify: `Editor/Setup/ToggleMenuSetup.cs`（BuildFadeTargets のフォールバックをメッシュ共通推奨に）
- Test: `Test/FadeCompatCheckerTest.cs`（または MaterialSlotScannerTest）, `Test/ToggleMenuSetupTest.cs`

**Interfaces:**
- `public static FadeFrame? FadeCompatChecker.CommonRecommended(IEnumerable<SlotInfo> slots)`（定義は Global Constraints）
- `ToggleMenuSetup.BuildFadeTargets(avatarRoot, slots, frameOverrides)`: renderer ごとにグループ化し、実効枠 = override ?? `CommonRecommended(そのrendererのslots)`。**1レンダラーにつき最大1 FadeTarget**（従来の (meshPath, frame) 重複除去はそのまま成立）
- `FadeFrameState` 単位で枠の可否を見るため、`FadeCompatResult` から枠→state を引くヘルパ（`GetFrame(FadeFrame)`）を追加してよい

- [ ] **Step 1: テスト（RED）**:
  - `CommonRecommended_AllSlotsFree_Main`（2スロットともデフォルト → Main）
  - `CommonRecommended_Intersection`（slot0: main×（_Color色変え）alpha○ / slot1: 全○ → AlphaMask）
  - `CommonRecommended_NoCommon_Null`（slot0 main のみ○ / slot1 main ×・alpha のみ○ → null）
  - `CommonRecommended_IgnoresUnknownSlots`（既知1+unknown1 → 既知slotのみで判定）
  - **既存テスト書き換え**: `BuildFadeTargets_SameRenderer_DifferentRecommendedFrames_BothKept` → `BuildFadeTargets_SameRenderer_UsesCommonFrame_Single` に改名し、同一レンダラー slot0=デフォルト(main○)/slot1=_Color色変え(main×, alpha○) → FadeTarget は **1件・AlphaMask** を assert
- [ ] **Step 2-4: RED → 実装 → GREEN**（95 → 99 目安、増減正確に報告）
- [ ] **Step 5: コミット** `フェード推奨をメッシュ単位の全スロット共通枠に統一`

---

### Task 2: UI — メッシュ行推奨・集約表示・ダイアログ既定値

**Files:**
- Modify: `Editor/UI/CostumeDashboardWindow.cs`

**要求:**
1. `EffectiveFrame` 系: window の実効枠フォールバックをメッシュ共通推奨に変更。per-Refresh でレンダラー→共通推奨のキャッシュを構築し、`EffectiveFrame(slot)` は override ?? そのキャッシュ（GroupByShader へ渡す func・AO ME・推奨列すべてこの経路）
2. 推奨列: **メッシュ行**に共通推奨（`main`/`Alpha`/`3rd`/`2nd`/`なし`、override 時 `*`）を表示。スロット行は空欄
3. メッシュ行の main/AM/3rd/2nd セル: スロット集約（全○=○ / 全利用可かつ△あり=△ / ×あり=×。tooltip に不可スロットの内訳）
4. ToggleMenuCreateDialog のメッシュごと枠ドロップダウン初期値もメッシュ共通推奨ベース（既存の実効枠受け渡しが1の変更で自然にそうなるはず — 確認）
5. 枠セレクタの警告判定（選択枠が使用済みスロットを含む場合の警告色）は従来どおりスロット単位で判定（集約と整合）
6. 挙動確認: AO ME グループが「メッシュ内でスロットごとに枠が割れて複数グループ化する」ことがなくなる（同一レンダラーのスロットは全て同じ実効枠）

検証: compile → 実データスモーク（両ビュー。smoke setup の Top は lts+std の2スロットなので、共通推奨がスロット構成で機能する実データになる）→ regression（Task 1 後の件数を維持）
- [ ] **Step 1: 実装** → **Step 2: 検証** → **Step 3: コミット** `UI: メッシュ行推奨と枠集約表示、実効枠のメッシュ共通化`

---

### Task 3: README 追記と最終確認

- [ ] README: フェードメソッドがメッシュ単位（全スロット共通枠）である旨を1-2行追記
- [ ] compile + 全テスト GREEN
- [ ] コミット `README更新（UX改訂8）`
