# Costume Dashboard UX改訂第3弾 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** (1) フェード枠判定の緩和 — ゲート無効・不透明シェーダーで実質未使用の差分は「利用可（警告△）」にする (2) BlendShape 一覧表示と MA BlendshapeSync の一括付与（素体 = アバター直下で最多シェイプのメッシュ、変更可）。

**Architecture:** 既存3層。Core（FadeCompatChecker のゲート認識判定・BlendShapeUtil）→ Setup（BlendShapeSyncSetup）→ UI（△表示・素体フィールド・BS列・[BS Sync]）。

**Tech Stack:** 既存と同じ + `nadena.dev.modular-avatar.core`（asmdef 直接参照を追加）

**Spec:** `docs~/superpowers/specs/2026-07-02-costume-dashboard-design.md`（UX改訂3反映済み。判定緩和 = 機能仕様2、BlendshapeSync = 機能仕様4b）

## Global Constraints

- 既存の Global Constraints（初版計画冒頭）を全て引き継ぐ
- 検証手順・CLI・`--on-dialog cancel` 運用は従来どおり。現テスト数 58
- 判定緩和の意味論（スペック 機能仕様2 に一致させる）:
  - 2nd/3rd: `_UseMainNTex == 0`（デフォルト）なら配下差分は Warning 付き Compatible。`== 1` なら従来どおり使用済（Use フラグ自体が非デフォルトのため）
  - AlphaMask: `_AlphaMaskMode == 0` なら配下差分は Warning 付き Compatible。`!= 0` でも**シェーダーが不透明**（`ShaderCatalog` variant が `opaque[_o]`/`cutout[_o]`、または `lilToon_multi` で `_TransparentMode` が 0/1）なら Warning 付き Compatible。`!= 0` かつ透過シェーダーなら使用済
  - Main は従来どおり（白のみ、緩和なし）
  - Warning 付き Compatible は `Recommended` 決定で通常の空きと同順（優先度 main > alpha > 3rd > 2nd は不変）
- BlendshapeSync の意味論（スペック 機能仕様4b、`BulkSetupBlendShapeSync` と同じ update-or-add）:
  - バインド: `BlendshapeBinding { ReferenceMesh = AvatarObjectReference.Set(素体.gameObject), Blendshape = 名前, LocalBlendshape = "" }`
  - 既存 `ModularAvatarBlendshapeSync` があれば同名エントリ（`LocalBlendshape == 名前 || (LocalBlendshape空 && Blendshape == 名前)`）を置換、なければ追加。コンポーネント新規時は `Undo.AddComponent`、更新時は `Undo.RecordObject`

---

### Task 1: FadeCompatChecker のゲート認識判定（Core）

**Files:**
- Modify: `Editor/Core/FadeCompatChecker.cs`
- Test: `Test/FadeCompatCheckerTest.cs`（追加・一部修正）

**Interfaces:**
- Produces:
  - `FadeFrameState` に `public bool Warning;`（Compatible=true かつ実質未使用の残存値があるとき true。ShortReason に残存値の要約を入れる — Warning 時も ShortReason を使う）
  - `FadeCompatChecker.Check(Material material)` の判定変更（Global Constraints の意味論）。シェーダー不透明判定は `ShaderCatalog.Resolve(material.shader)` の Variant（`opaque`/`opaque_o`/`cutout`/`cutout_o`）と、`lilToon_multi` の場合 `_TransparentMode`（`Mathf.RoundToInt(material.GetFloat(...))` が 0/1）で行う。unknown family は透過扱い（緩和しない = 安全側）

- [ ] **Step 1: テスト追加（RED）**

`Test/FadeCompatCheckerTest.cs` に追加（既存テストの期待も確認 — `ThirdUsed_ButMainFree_RecommendMain` は `_UseMain3rdTex=1` なので従来どおり使用済で変化なし。`AllUsed_RecommendNull` / `MainAlphaThirdUsed_RecommendSecond` 等は Use フラグ / Mode を立てているので変化なし。ただし `MainAndAlphaMaskUsed_RecommendThird` は `_AlphaMaskMode=1` を立てるが **lts（不透明）マテリアルなので緩和により AlphaMask が Warning 付き利用可となり Recommended=AlphaMask に変わる** — このテストは `mat.shader` を透過版 `lts_trans`（GUID `165365ab7100a044ca85fc8c33548a62`）に差し替えて意図を維持する。同様に `AllUsed_RecommendNull` / `MainAlphaThirdUsed_RecommendSecond` も lts_trans ベースに変更する）:

```csharp
        [Test]
        public void ThirdValuesLeftButGateOff_CompatibleWithWarning()
        {
            mat.SetFloat("_Main3rdTexBlendMode", 3); // ゲート(_UseMain3rdTex)は0のまま
            var result = FadeCompatChecker.Check(mat);
            Assert.That(result.Third.Compatible, Is.True);
            Assert.That(result.Third.Warning, Is.True);
            Assert.That(result.Third.ShortReason, Does.Contain("_Main3rdTexBlendMode"));
        }

        [Test]
        public void AlphaMaskValuesLeftButModeOff_CompatibleWithWarning()
        {
            mat.SetFloat("_AlphaMaskValue", 0.5f); // _AlphaMaskMode は 0 のまま
            var result = FadeCompatChecker.Check(mat);
            Assert.That(result.AlphaMask.Compatible, Is.True);
            Assert.That(result.AlphaMask.Warning, Is.True);
        }

        [Test]
        public void AlphaMaskModeOn_OpaqueShader_CompatibleWithWarning()
        {
            // lts (opaque) なので AlphaMask は出力に効かない
            mat.SetFloat("_AlphaMaskMode", 2);
            var result = FadeCompatChecker.Check(mat);
            Assert.That(result.AlphaMask.Compatible, Is.True);
            Assert.That(result.AlphaMask.Warning, Is.True);
        }

        [Test]
        public void AlphaMaskModeOn_TransShader_Incompatible()
        {
            var transShader = AssetDatabase.LoadAssetAtPath<Shader>(AssetDatabase.GUIDToAssetPath("165365ab7100a044ca85fc8c33548a62"));
            var transMat = new Material(transShader);
            try
            {
                transMat.SetFloat("_AlphaMaskMode", 2);
                var result = FadeCompatChecker.Check(transMat);
                Assert.That(result.AlphaMask.Compatible, Is.False);
                Assert.That(result.AlphaMask.Warning, Is.False);
            }
            finally
            {
                Object.DestroyImmediate(transMat);
            }
        }

        [Test]
        public void GateOn_Incompatible_NoWarningFlag()
        {
            mat.SetFloat("_UseMain3rdTex", 1);
            var result = FadeCompatChecker.Check(mat);
            Assert.That(result.Third.Compatible, Is.False);
            Assert.That(result.Third.Warning, Is.False);
        }

        [Test]
        public void CleanFrame_NoWarning()
        {
            var result = FadeCompatChecker.Check(mat);
            Assert.That(result.Third.Warning, Is.False);
            Assert.That(result.Third.ShortReason, Is.Null);
        }
```

- [ ] **Step 2: RED 確認**（Warning 未定義のコンパイルエラー）
- [ ] **Step 3: 実装**

`CheckFrame` を拡張（案 — 実装者は同等の挙動なら整理してよい）:

```csharp
        static FadeFrameState CheckFrame(Material m, List<PropDef> defs, string gateName, bool gateInert)
        {
            // gateName: このグループの有効化ゲートプロパティ（_UseMainNTex / _AlphaMaskMode）
            // gateInert: ゲートが有効でもレンダリングに影響しない状況（不透明シェーダーの AlphaMask）
            var state = new FadeFrameState();
            foreach (var def in defs) { /* 既存の差分収集 */ }
            if (state.NonDefaultProps.Count == 0)
            {
                state.Compatible = true;
                return state;
            }
            var gateProp = state.NonDefaultProps.FirstOrDefault(p => p.Name == gateName);
            var gateOff = gateProp == null; // ゲート自体がデフォルト(無効)のまま
            if (gateOff || gateInert)
            {
                state.Compatible = true;
                state.Warning = true;
                state.ShortReason = "未使用の設定値あり: " + MakeShortReason(state);
            }
            else
            {
                state.Compatible = false;
                state.ShortReason = MakeShortReason(state);
            }
            return state;
        }
```

`Check` 側: `bool opaque = IsOpaqueShader(material);`（ShaderCatalog.Resolve で variant 判定 + multi の `_TransparentMode` 0/1。unknown は false）→ `CheckFrame(m, ThirdProps, "_UseMain3rdTex", false)` / `CheckFrame(m, SecondProps, "_UseMain2ndTex", false)` / `CheckFrame(m, AlphaMaskProps, "_AlphaMaskMode", opaque)`。既存の Check 内の ShortReason 代入行は CheckFrame 内に移したため削除。

- [ ] **Step 4: GREEN 確認**（既存テストの意図修正3件を含め全件）
- [ ] **Step 5: コミット** `フェード枠判定を緩和: ゲート無効・不透明シェーダーの残存値は警告付き利用可に`

---

### Task 2: △表示（UI 小変更）

**Files:**
- Modify: `Editor/UI/CostumeDashboardWindow.cs`（MakeFrameColumn のみ）

**Interfaces:**
- Consumes: `FadeFrameState.Warning` / `ShortReason`
- Produces: 枠列セル表示: 完全空き=○ / Warning付き利用可=△ / 使用済=×。tooltip は従来（ShortReason 先頭 + 全差分）

- [ ] **Step 1: 実装**（`state.Compatible ? (state.Warning ? "△" : "○") : "×"`）
- [ ] **Step 2: compile + window smoke + 全テスト GREEN**（新テストなし）
- [ ] **Step 3: コミット** `枠列に警告付き利用可の△表示を追加`

---

### Task 3: BlendShapeSyncSetup（Setup）と asmdef 参照追加

**Files:**
- Modify: `Editor/Narazaka.VRChat.CostumeDashboard.Editor.asmdef`（references に `"nadena.dev.modular-avatar.core"` を追加）
- Modify: `Test/Narazaka.VRChat.CostumeDashboard.Test.asmdef`（同上）
- Create: `Editor/Setup/BlendShapeSyncSetup.cs`
- Test: `Test/BlendShapeSyncSetupTest.cs`

**Interfaces:**
- Produces（namespace は従来どおり `Narazaka.VRChat.CostumeDashboard.Editor`）:

```csharp
    public static class BlendShapeSyncSetup
    {
        /// <summary>アバター直下（直接の子）の SkinnedMeshRenderer のうち BlendShape 数最大のものを素体候補として返す（0件なら null）</summary>
        public static SkinnedMeshRenderer FindDefaultBaseMesh(GameObject avatarRoot);

        /// <summary>mesh の BlendShape 名一覧（sharedMesh null なら空）</summary>
        public static List<string> GetBlendShapeNames(SkinnedMeshRenderer mesh);

        /// <summary>target と baseMesh の同名 BlendShape 名一覧</summary>
        public static List<string> MatchingNames(SkinnedMeshRenderer target, SkinnedMeshRenderer baseMesh);

        /// <summary>target に ModularAvatarBlendshapeSync を付与/更新し、baseMesh と同名の全シェイプをバインドする（update-or-add、Undo対応）。バインド件数を返す</summary>
        public static int Apply(SkinnedMeshRenderer target, SkinnedMeshRenderer baseMesh);
    }
```

Apply の実装は `BulkSetupBlendShapeSync.Apply` の意味論に一致させる: `AvatarObjectReference.Set(baseMesh.gameObject)`、`Blendshape = 名前`、`LocalBlendshape = ""`、既存 Bindings の同名エントリ（`b.LocalBlendshape == 名前 || (string.IsNullOrEmpty(b.LocalBlendshape) && b.Blendshape == 名前)`）は置換。コンポーネント新規時 `Undo.AddComponent<ModularAvatarBlendshapeSync>`、既存時 `Undo.RecordObject`。`EditorUtility.SetDirty`。

- [ ] **Step 1: テスト（RED）** — `Test/BlendShapeSyncSetupTest.cs`:
  - テスト用に BlendShape 付き Mesh を動的生成するヘルパ（`new Mesh()` + `mesh.AddBlendShapeFrame(name, 100, new Vector3[頂点数], null, null)`。頂点は `mesh.vertices = new Vector3[3]` 程度で可）
  - `FindDefaultBaseMesh_PicksMostShapes`: アバター直下に shapes 3個のSMRと1個のSMR → 3個の方が返る。孫階層のSMR（大量shape）は無視される
  - `MatchingNames_IntersectsInOrder`: target(A,B,C) × base(B,C,D) → [B,C]（target の順）
  - `Apply_AddsBindings`: バインド2件、`ReferenceMesh` が base を指し（`Get(component)` で解決確認 or `referencePath` 検証）、`Blendshape` 一致・`LocalBlendshape` 空
  - `Apply_Twice_UpdatesNotDuplicates`: 2回 Apply → Bindings 件数不変
  - `Apply_PreservesUnrelatedBindings`: 手で無関係バインドを追加後 Apply → 残存
- [ ] **Step 2: RED 確認** → **Step 3: 実装** → **Step 4: GREEN**（asmdef 変更が compile に反映されない場合 `asset refresh`）
- [ ] **Step 5: コミット** `BlendShapeSyncSetup: 素体検出と同名シェイプのMA BlendshapeSync一括バインド`

---

### Task 4: UI — 素体フィールド・BS 表示・[BS Sync]（メッシュ行＋✓一括）

**Files:**
- Modify: `Editor/UI/CostumeDashboardWindow.cs`

**Interfaces:**
- Consumes: `BlendShapeSyncSetup.*`
- Produces（要求仕様。実装者は既存パターンを踏襲）:
  1. **素体フィールド**: 非シリアライズ `Dictionary<int, SkinnedMeshRenderer> baseMeshOverrides`（アバタールート instanceID キー）。実効素体 = override ?? `FindDefaultBaseMesh(avatarRoot)`。衣装リストの下（ツリーの上）に、登録衣装から解決された各アバターにつき1行「素体: [ObjectField]」を表示（既定値をプレースホルダとして表示し、変更で override 登録・null で解除）
  2. **メッシュ行に BS 表示**: BlendShape 数（0 は非表示）。tooltip にシェイプ名一覧（50件超は先頭50件+件数）
  3. **メッシュ行 [BS Sync] ボタン**（操作列に追加）: `BlendShapeSyncSetup.Apply(smr, 実効素体)` 実行 → `Refresh()`。無効条件＋理由tooltip: SMRでない/BlendShapeなし → 「BlendShapeなし」、アバタールート不明 → 既存文言、対象==素体 → 「素体自身」、`MatchingNames` 空 → 「素体と同名のシェイプなし」。既に `ModularAvatarBlendshapeSync` が付いていればボタン表記を「BS✓」にし tooltip「設定済み（再実行で同名バインドを更新）」
  4. **ツールバー [✓ から BS Sync]**: checkedMeshes の各 SMR に Apply（無効条件該当はスキップし、実行後に「n件付与/スキップm件」を `ShowNotification` か HelpBox で通知）
  5. Renderer が MeshRenderer のメッシュ行では BS 系 UI 非表示

検証: compile → window smoke（実データスモーク: `.aibridge/code/cd_smoke_setup.csx` は BlendShape を持たないため、スクリプトを一時拡張せず既存のまま「例外なし」の確認でよい）→ 全テスト GREEN

- [ ] **Step 1: 実装** → **Step 2: compile + smoke + 全テスト** → **Step 3: コミット** `素体フィールドとBlendShape一覧・BS Sync付与UIを追加`

---

### Task 5: README 更新と最終確認

- [ ] README の機能一覧に判定緩和（△）と BlendshapeSync 付与を追記
- [ ] compile + 全テスト GREEN 最終確認
- [ ] コミット `README更新（UX改訂3）`
