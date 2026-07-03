# Costume Dashboard UX改訂第5弾 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** (1) スロット行に AO ME グループ（ホスト名）列を両ビューで表示＋設定済み✓ (2) 設定済みボタン（AO ME✓/BS✓/Toggle✓）の背景色差別化 (3) メッシュを対象とする既存 Toggle Menu の検出表示（アバター全体走査）。

**Spec:** `docs~/superpowers/specs/2026-07-02-costume-dashboard-design.md`「設定済み表示の強化（UX改訂5）」

## Global Constraints

- 既存の Global Constraints を全て引き継ぐ。**git ステージは明示パス指定のみ**（`-A`/`.`/`-u` 禁止、status --short 確認後コミット）。モーダルダイアログは `dialog status` 報告で停止（クリック・シーン操作での回避禁止）
- 検証手順・CLI・`--on-dialog cancel`・benign エラー3種は従来どおり。現テスト数 86
- bindCell 規律（userData unregister-before-register / cell.Clear()+新規 Button / スタイルは毎 bind 無条件リセット）維持

---

### Task 1: ToggleMenuLocator（既存メニュー検出、Setup）

**Files:**
- Modify: `Editor/Setup/ToggleMenuSetup.cs`（メソッド追加）
- Test: `Test/ToggleMenuSetupTest.cs`

**Interfaces:**
- `public static List<AvatarToggleMenuCreator> FindMenusTargeting(GameObject avatarRoot, Renderer renderer)` — アバタールート配下（非アクティブ含む）の全 `AvatarToggleMenuCreator` を走査し、`AvatarUtil.RelativePath(avatarRoot, renderer.gameObject)` が以下のいずれかに含まれるものを返す:
  - `AvatarToggleMenu.ToggleObjects` のキー（string）
  - `AvatarToggleMenu.ToggleShaderVectorParameters` のキー Item1
  - `AvatarToggleMenu.ToggleShaderParameters` のキー Item1
  - avatarRoot / renderer が null、または相対パスが null/空 → 空リスト

- [ ] **Step 1: テスト（RED）**: `FindMenusTargeting_MatchesToggleObjects`（Create でメニューを作り、対象メッシュで1件ヒット・非対象メッシュで0件）/ `FindMenusTargeting_MatchesFadeKeys`（ToggleObjects 空で shaderVectorFades のみのメニューがヒット）/ `FindMenusTargeting_NullSafe`（avatarRoot 外レンダラー → 空）
- [ ] **Step 2-4: RED → 実装 → GREEN**（86 → 89）
- [ ] **Step 5: コミット** `ToggleMenuSetup.FindMenusTargeting: メッシュを対象とする既存メニュー検出`

---

### Task 2: UI — AO ME グループ列・設定済みボタン色・Toggle✓

**Files:**
- Modify: `Editor/UI/CostumeDashboardWindow.cs`

**要求:**
1. **AO ME 列（スロット行、両ビュー）**: スロットが属するグループの `AOMEHostSuffix` を表示。設定済み（`AOMaterialEditorSetup.HasComponent(FindAOMEHost(costume, group))`）なら「✓ 」を前置。フェード対象外グループ（CanSetupFade=false かつ onetrans 特例にも該当しない）は「—」。実装: BuildMeshViewItems / BuildGroupViewItems の両方でスロット→グループの対応を Row に保持（メッシュビューは CostumeGroups から slot 参照で逆引きした Dictionary を作る。グループビューは既存 row.Group）。ホスト存在チェックは per-Refresh キャッシュ（グループ数ぶんの Find なので軽量、bind 内での再計算は不可）
2. **設定済みボタンの背景色**: `AO ME✓`（グループ行・従来）/ `BS✓` / `Toggle✓` のとき `button.style.backgroundColor = new Color(0.18f, 0.42f, 0.2f)`（緑系）、未設定は `StyleKeyword.Null` に毎 bind 無条件リセット
3. **Toggle✓**: メッシュ行の [Toggle] ボタン: `ToggleMenuSetup.FindMenusTargeting(avatarRoot, renderer)` の結果が非空なら表記 `Toggle✓`＋緑背景＋tooltip にメニュー GameObject のアバタールート相対パス一覧（改行区切り）。クリック挙動は従来どおり（ダイアログを開く）。検出はアバター全体走査のため **per-Refresh キャッシュ必須**: Refresh 時に「アバタールートごとの全 AvatarToggleMenuCreator 一覧」を1回だけ取得し、メッシュ行 bind ではキー照合のみ行う（`FindMenusTargeting` を bind ごとに呼ばない — 内部の GetComponentsInChildren が全体走査のため。キャッシュ構造例: `Dictionary<int /*avatarRootID*/, List<(AvatarToggleMenuCreator creator, HashSet<string> targetPaths)>>` を Refresh で構築し、bind では renderer の相対パスを HashSet 照合）
4. ツールバー・他ボタンの挙動不変。列追加に伴う幅調整可

検証: compile → 実データスモーク（cd_smoke_setup/cleanup。Toggle✓ 確認は smoke csx を一時拡張して ToggleMenuSetup.Create を1件作成→列/ボタン状態を get_logs 例外なしで確認する程度でよい）→ regression 89/89

- [ ] **Step 1: 実装** → **Step 2: compile+smoke+regression** → **Step 3: コミット** `AO MEグループ列と設定済み表示(色/Toggle✓)を追加`

---

### Task 3: README 追記と最終確認

- [ ] README 機能セクションに設定済み表示の強化を1行追記
- [ ] compile + 全テスト GREEN（89/89）
- [ ] コミット `README更新（UX改訂5）`
