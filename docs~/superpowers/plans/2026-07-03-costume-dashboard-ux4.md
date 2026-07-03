# Costume Dashboard UX改訂第4弾 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** (1) 素体自動判定の除外条件（無効メッシュ・FaceMesh） (2) AlphaMask 使用中と色フェード（main/3rd/2nd）の干渉判定＋AO ME 自動調整（置き換え→乗算 / 露出防止の無効化） (3) メッシュベースのビューモード（既定）追加。

**Architecture:** 既存3層。Core（FindDefaultBaseMesh 除外・ColorFadeImpact 分析）→ Scanner/Window（グループキー拡張・AO ME override 追加）→ UI（ビューモード）。

**Spec:** `docs~/superpowers/specs/2026-07-02-costume-dashboard-design.md`（UX改訂4反映済み: 機能仕様2の AlphaMask 干渉、機能仕様3の調整 override、機能仕様4b の素体除外、画面のビューモード）

## Global Constraints

- 既存の Global Constraints を全て引き継ぐ。検証手順・CLI・`--on-dialog cancel` 運用従来どおり。現テスト数 71
- **ColorFadeImpact の意味論**（スペック 機能仕様2/3 と一致）:
  - 対象 = 実効枠が Main/Third/Second のときの `_AlphaMaskMode` 干渉
  - Mode=0 → 影響なし（Adjust=None）
  - マスク不活性シェーダー（`IsAlphaMaskInertShader`==true、= opaque[_o] / multi tm==0）で Mode≠0 → main/3rd/2nd 枠への表示影響なし・Adjust=Neutralize（AO ME で `_AlphaMaskMode=0`）
  - マスク有効シェーダーで Mode=2 → 影響なし（Adjust=None）
  - マスク有効シェーダーで Mode=1 → Adjust=ToMultiply（AO ME で `_AlphaMaskMode=2`）。`_MainTex` に alpha チャンネル有り → main/3rd/2nd 枠 △（警告文言: 「AlphaMask 置き換え→乗算に変換されます（メインテクスチャの透過と干渉する可能性）」）。alpha 無し → 無印
  - マスク有効シェーダーで Mode がそれ以外（3以上等）→ Blocked（main/3rd/2nd 枠 ×、理由「AlphaMask が特殊モードで使用中」）
  - `_MainTex` の alpha 判定: TextureImporter.DoesSourceTextureHaveAlpha を優先、取れなければ GraphicsFormatUtility.HasAlphaChannel、非 Texture2D は alpha あり扱い（安全側）、テクスチャ null は alpha なし
- 素体除外: 非アクティブ GameObject（`!smr.gameObject.activeInHierarchy`）/ タグ EditorOnly / `VRCAvatarDescriptor.VisemeSkinnedMesh` と同一
- ビューモード: `enum DashboardViewMode { Mesh, Group }`、`[SerializeField]` で保持、既定 Mesh

---

### Task 1: 素体自動判定の除外条件（Core/Setup）

**Files:**
- Modify: `Editor/Setup/BlendShapeSyncSetup.cs`（FindDefaultBaseMesh）
- Test: `Test/BlendShapeSyncSetupTest.cs`

**Interfaces:** `FindDefaultBaseMesh(GameObject avatarRoot)` のシグネチャ不変。除外: (a) `!smr.gameObject.activeInHierarchy` (b) `smr.gameObject.CompareTag("EditorOnly")` (c) avatarRoot の `VRCAvatarDescriptor.VisemeSkinnedMesh` と同一（descriptor 無し/未設定なら除外なし）。

- [ ] **Step 1: テスト追加（RED）**: `FindDefaultBaseMesh_ExcludesInactive`（最多シェイプのSMRを非アクティブ化 → 次点が返る）/ `FindDefaultBaseMesh_ExcludesEditorOnly`（タグ EditorOnly → 次点）/ `FindDefaultBaseMesh_ExcludesFaceMesh`（descriptor.VisemeSkinnedMesh に最多シェイプSMRを設定 → 次点。`lipSync` の設定は不要、フィールド代入のみで可）
- [ ] **Step 2-4: RED → 実装 → GREEN**（71 → 74）
- [ ] **Step 5: コミット** `素体自動判定から無効メッシュとFaceMeshを除外`

---

### Task 2: ColorFadeImpact 分析と枠表示への反映（Core）

**Files:**
- Modify: `Editor/Core/FadeCompatChecker.cs`
- Test: `Test/FadeCompatCheckerTest.cs`

**Interfaces:**
- `public enum AlphaMaskAdjust { None, Neutralize, ToMultiply }`
- `public class ColorFadeImpact { public AlphaMaskAdjust Adjust; public bool Blocked; public bool Warning; public string Reason; }`
- `FadeCompatResult` に `public ColorFadeImpact ColorFadeImpact;`
- `Check()` 内で分析し、Main/Third/Second の3枠へ反映:
  - Blocked → 各枠 `Compatible=false, Warning=false`、ShortReason は既存理由が無ければ impact.Reason（既に非互換ならそのまま）
  - Warning → `Compatible==true` の枠に `Warning=true`、ShortReason に impact.Reason を設定（既に Warning 理由がある場合は「; 」連結）
  - AlphaMask 枠自体には影響させない
- `_MainTex` alpha 判定ヘルパは Global Constraints の規則で実装（`using UnityEditor;` は既にある。`UnityEngine.Experimental.Rendering.GraphicsFormatUtility`）

- [ ] **Step 1: テスト追加（RED）**（lts_trans ベースのマテリアルを使う）:
  - `AlphaMaskReplace_NoMainTexAlpha_ColorFadesToMultiplyNoWarning`: lts_trans + `_AlphaMaskMode=1`（_MainTex null）→ `ColorFadeImpact.Adjust==ToMultiply`、`Main.Compatible==true && Main.Warning==false`
  - `AlphaMaskReplace_MainTexWithAlpha_ColorFadesWarn`: 同上 + `_MainTex` に alpha ありの Texture2D（`new Texture2D(4,4, TextureFormat.RGBA32, false)`。アセット化しないので GraphicsFormatUtility 経路）→ `Main.Warning==true`、`Third.Warning==true`、Reason に「乗算」
  - `AlphaMaskSpecialMode_ColorFadesBlocked`: lts_trans + `_AlphaMaskMode=3` → `Main.Compatible==false`、`Third.Compatible==false`、`Second.Compatible==false`、`AlphaMask` は従来判定（mode≠0 で使用済）
  - `AlphaMaskResidual_OnOpaque_NeutralizeNoFrameEffect`: lts（opaque）+ `_AlphaMaskMode=1` → `ColorFadeImpact.Adjust==Neutralize`、`Main.Compatible==true && Main.Warning==false`（AlphaMask 枠は既存の緩和で △）
  - `AlphaMaskMultiply_NoEffect`: lts_trans + `_AlphaMaskMode=2` → `Adjust==None`、Main 無印（AlphaMask 枠は使用済）
- [ ] **Step 2-4: RED → 実装 → GREEN**（74 → 79）
- [ ] **Step 5: コミット** `AlphaMask使用中と色フェードの干渉判定(ColorFadeImpact)を追加`

---

### Task 3: グループキー拡張と AO ME 調整 override（Scanner/Window）

**Files:**
- Modify: `Editor/Core/MaterialSlotScanner.cs`（SlotGroup に `public AlphaMaskAdjust AlphaMaskAdjust;` 追加、グループキーに impact.Adjust を追加）
- Modify: `Editor/UI/CostumeDashboardWindow.cs`（CreateAOMaterialEditor: 実効枠が Main/Third/Second のとき `group.AlphaMaskAdjust` に応じ `_AlphaMaskMode=0`（Neutralize）/ `=2`（ToMultiply）を properties に追加。opaque/cutout/trans/multi 分岐と onetrans/twotrans 分岐の両方）
- Test: `Test/MaterialSlotScannerTest.cs`

**Interfaces:** キーは既存 (Family, Variant, ShaderGuid, Preset, MultiBlocked) + `AlphaMaskAdjust`（slot.FadeCompat?.ColorFadeImpact?.Adjust ?? None）。AO ME ホスト suffix は変えない（Adjust 混在はキー分割で防がれ、同名ホストに後勝ち上書きされる懸念があるが、同一 variant×同一実効枠で Adjust が異なるケースでは suffix 衝突が起きる — **suffix に Adjust を含める**: Neutralize → `_amoff`、ToMultiply → `_ammul` を末尾に付与）

- [ ] **Step 1: テスト追加（RED）**: `GroupByShader_SplitsByAlphaMaskAdjust`（lts_trans マテリアル2枚: mode=0 と mode=1 → Preset が同じでも2グループになり、片方の `AlphaMaskAdjust==ToMultiply`）
- [ ] **Step 2-4: RED → 実装 → GREEN**（79 → 80）+ window smoke（実データスモーク setup/cleanup 流用）
- [ ] **Step 5: コミット** `AO MEにAlphaMask調整overrideを追加しグループキーを拡張`

---

### Task 4: メッシュビュー（既定）と AO ME一括作成

**Files:**
- Modify: `Editor/UI/CostumeDashboardWindow.cs`

**Interfaces / 要求:**
1. `enum DashboardViewMode { Mesh, Group }`、`[SerializeField] DashboardViewMode viewMode = DashboardViewMode.Mesh;`。ツールバーに切替（`EnumField` か PopupField、ラベル「表示: メッシュ / AO ME」）。変更で Refresh()
2. **メッシュビュー（既定）**: 行階層 衣装 > メッシュ > スロット。メッシュはレンダラー単位で1回だけ現れる（衣装スキャン全体を renderer でバケット）。メッシュ行のウィジェット（Select/✓/[Toggle]/[Q]/BS/[BS Sync]/枠セレクタ）は既存のまま。スロット行も既存列のまま（シェーダー列に variant が出る）
3. **AO MEビュー（従来）**: 現在の 衣装 > グループ > メッシュ > スロット をそのまま維持（[AO ME] グループボタン含む）
4. メッシュビューの衣装行の操作列に **[AO ME一括]** ボタン: その衣装の全グループを `GroupByShader(scan, EffectiveFrame)` で計算し、`AOMEAvailability` が有効なグループ全てに `CreateAOMaterialEditor` を実行。`ShowNotification($"AO ME: {created}グループ作成 / {skipped}スキップ")`。有効グループが0なら disabled + tooltip
5. AOMEAvailability / CreateAOMaterialEditor が Row 依存なら (costume, avatarRoot, group) を取る形へ内部リファクタしてよい（挙動不変）
6. bindCell 規律維持（既存パターン）。checkedMeshes / frameOverrides / baseMeshOverrides はビュー間で共有（renderer/avatar ID キーのため自然に共有される）

- [ ] **Step 1: 実装** → **Step 2: compile + 実データスモーク（両ビューで例外なし。code execute smoke 後にビュー切替は手動不可のため、`viewMode` の初期値 Mesh でのスモーク＋Group への切替はリフレクションで `viewMode` を変更し `Refresh()` を呼ぶ csx を一時作成して確認してよい）+ regression 80/80** → **Step 3: コミット** `メッシュビュー(既定)とAO ME一括作成を追加`

---

### Task 5: README 更新と最終確認

- [ ] README: ビューモード、AlphaMask 干渉の自動調整、素体除外条件を追記
- [ ] compile + 全テスト GREEN（80/80）
- [ ] コミット `README更新（UX改訂4）`
