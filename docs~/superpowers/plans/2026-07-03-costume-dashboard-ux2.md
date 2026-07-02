# Costume Dashboard UX改訂第2弾 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** ユーザーフィードバック5点＋1点の反映: (1) RQのメッシュ一括設定 (2) Select/✓のメッシュ行集約 (3) メッシュ行からの1クリックToggleダイアログ (4) フェード枠のカスタム選択 (5) Main(_Color)フェードの追加と優先度 main>alpha>3rd>2nd (6) 枠が利用不可の理由の簡潔表示。

**Architecture:** 既存3層構成を維持。Core（FadeFrame.Main追加・優先度・ShortReason）→ Setup（プリセット再構成・BuildFadeTargetsのoverride対応・SetAll）→ UI（メッシュ行の導入）。

**Tech Stack:** 既存と同じ（Unity 2022.3 / UIElements / EditMode tests）

**Spec:** `docs~/superpowers/specs/2026-07-02-costume-dashboard-design.md`（UX改訂反映済み）

## Global Constraints

- 既存の Global Constraints（初版計画 `2026-07-02-costume-dashboard.md` 冒頭）をすべて引き継ぐ（namespace / ファイル配置 / コミット規約 / Undo / Materialアセット不変更）
- **Main フェードの判定**: `_Color` が RGBA=(1,1,1,1)（#FFFFFFFF）のときのみ「空き」。駆動は `_Color` の `(1,1,1,0)`↔`(1,1,1,1)` ベクトルフェード。駆動プロパティの有効化は不要（プリセットの枠別駆動プロパティは空）
- **優先度**: Main > AlphaMask > Third > Second
- **実効枠** = メッシュ（レンダラー）単位のカスタム選択（未選択なら推奨枠）。AO ME のグループ分割・プリセットと Toggle Menu フェードの両方に適用
- 検証手順は初版計画の「検証環境の前提」どおり（aibridge CLI = `./.aibridge/cli/AIBridgeCLI.exe`、`--on-dialog cancel` の使用可、他の --on-dialog モード禁止）
- 現テスト数: 49。各タスクで増減を正確に報告する

---

### Task 1: FadeFrame.Main・優先度変更・ShortReason（Core）

**Files:**
- Modify: `Editor/Core/FadeCompatChecker.cs`
- Modify: `Editor/Core/MaterialSlotScanner.cs`（FadeDisabledReason 文言のみ）
- Test: `Test/FadeCompatCheckerTest.cs`（書き換え）, `Test/MaterialSlotScannerTest.cs`（文言追随）

**Interfaces:**
- Consumes: なし
- Produces:
  - `enum FadeFrame { Main, Third, Second, AlphaMask }`（Main を先頭に追加。永続シリアライズ箇所はないため安全）
  - `FadeFrameState` に `public string ShortReason;`（compatible のとき null。使用済のとき1行要約）
  - `FadeCompatResult` に `public FadeFrameState Main;`
  - `Recommended` の優先順位: Main > AlphaMask > Third > Second
  - `MaterialSlotScanner` の理由文言: `"3rd/2nd/AlphaMask 全枠使用済み"` → `"main/AlphaMask/3rd/2nd 全枠使用済み"`

- [ ] **Step 1: テストを書き換える（RED）**

`Test/FadeCompatCheckerTest.cs` の既存6テストを以下に置き換える（SetUp/TearDown は既存のまま）:

```csharp
        [Test]
        public void Default_AllCompatible_RecommendMain()
        {
            var result = FadeCompatChecker.Check(mat);
            Assert.That(result.Main.Compatible, Is.True);
            Assert.That(result.Third.Compatible, Is.True);
            Assert.That(result.Second.Compatible, Is.True);
            Assert.That(result.AlphaMask.Compatible, Is.True);
            Assert.That(result.Recommended, Is.EqualTo(FadeFrame.Main));
            Assert.That(result.Main.ShortReason, Is.Null);
        }

        [Test]
        public void MainColored_RecommendAlphaMask()
        {
            mat.SetColor("_Color", new Color(1f, 0.5f, 0.5f, 1f));
            var result = FadeCompatChecker.Check(mat);
            Assert.That(result.Main.Compatible, Is.False);
            Assert.That(result.Main.ShortReason, Does.Contain("_Color"));
            Assert.That(result.Main.ShortReason, Does.Contain("白のみ可"));
            Assert.That(result.Recommended, Is.EqualTo(FadeFrame.AlphaMask));
        }

        [Test]
        public void MainAndAlphaMaskUsed_RecommendThird()
        {
            mat.SetColor("_Color", new Color(1f, 0.5f, 0.5f, 1f));
            mat.SetFloat("_AlphaMaskMode", 1);
            var result = FadeCompatChecker.Check(mat);
            Assert.That(result.AlphaMask.ShortReason, Is.Not.Null.And.Not.Empty);
            Assert.That(result.Recommended, Is.EqualTo(FadeFrame.Third));
        }

        [Test]
        public void MainAlphaThirdUsed_RecommendSecond()
        {
            mat.SetColor("_Color", new Color(1f, 0.5f, 0.5f, 1f));
            mat.SetFloat("_AlphaMaskMode", 1);
            mat.SetFloat("_UseMain3rdTex", 1);
            var result = FadeCompatChecker.Check(mat);
            Assert.That(result.Third.ShortReason, Does.Contain("3rd"));
            Assert.That(result.Recommended, Is.EqualTo(FadeFrame.Second));
        }

        [Test]
        public void AllUsed_RecommendNull()
        {
            mat.SetColor("_Color", new Color(1f, 0.5f, 0.5f, 1f));
            mat.SetFloat("_AlphaMaskMode", 1);
            mat.SetFloat("_UseMain3rdTex", 1);
            mat.SetFloat("_UseMain2ndTex", 1);
            var result = FadeCompatChecker.Check(mat);
            Assert.That(result.Recommended, Is.Null);
        }

        [Test]
        public void ThirdUsed_ButMainFree_RecommendMain()
        {
            mat.SetFloat("_UseMain3rdTex", 1);
            var result = FadeCompatChecker.Check(mat);
            Assert.That(result.Third.Compatible, Is.False);
            Assert.That(result.Recommended, Is.EqualTo(FadeFrame.Main));
        }

        [Test]
        public void TextureAssigned_Incompatible_ReasonMentionsTexture()
        {
            var tex = new Texture2D(4, 4);
            try
            {
                mat.SetTexture("_Main3rdTex", tex);
                var result = FadeCompatChecker.Check(mat);
                Assert.That(result.Third.Compatible, Is.False);
                Assert.That(result.Third.ShortReason, Does.Contain("_Main3rdTex"));
            }
            finally
            {
                Object.DestroyImmediate(tex);
            }
        }

        [Test]
        public void NonLilToonMaterial_AllCompatible()
        {
            var std = new Material(Shader.Find("Standard"));
            try
            {
                var result = FadeCompatChecker.Check(std);
                Assert.That(result.Main.Compatible, Is.True); // Standard の _Color は白がデフォルト
                Assert.That(result.Recommended, Is.EqualTo(FadeFrame.Main));
            }
            finally
            {
                Object.DestroyImmediate(std);
            }
        }
```

`Test/MaterialSlotScannerTest.cs`: `GroupByShader_GroupsByFamilyVariant` の `Preset` 期待値を `FadeFrame.Main` に変更。`GroupByShader_SplitsByRecommendedPreset` は `_UseMain3rdTex=1` では分割されなくなる（どちらも Main 推奨）ため、分割条件を `_Color` 変更（`thirdUsed.SetColor("_Color", new Color(1f,0.5f,0.5f,1f))` → 変数名も `mainUsed` に改名）にし、期待グループを `Preset == FadeFrame.Main` と `Preset == FadeFrame.AlphaMask` に変更。

- [ ] **Step 2: コンパイルエラー/テスト失敗（RED）を確認**（`Main` 未定義のコンパイルエラーで良い）

- [ ] **Step 3: 実装**

`Editor/Core/FadeCompatChecker.cs`:

```csharp
    public enum FadeFrame
    {
        Main,
        Third,
        Second,
        AlphaMask,
    }
```

`FadeFrameState` に `public string ShortReason;` を追加。`FadeCompatResult` に `public FadeFrameState Main;` を追加。

`Check` を変更:

```csharp
        public static FadeCompatResult Check(Material material)
        {
            var main = CheckMain(material);
            var third = CheckFrame(material, ThirdProps);
            var second = CheckFrame(material, SecondProps);
            var alphaMask = CheckFrame(material, AlphaMaskProps);
            third.ShortReason = third.Compatible ? null : MakeShortReason(third);
            second.ShortReason = second.Compatible ? null : MakeShortReason(second);
            alphaMask.ShortReason = alphaMask.Compatible ? null : MakeShortReason(alphaMask);
            FadeFrame? recommended = null;
            if (main.Compatible) recommended = FadeFrame.Main;
            else if (alphaMask.Compatible) recommended = FadeFrame.AlphaMask;
            else if (third.Compatible) recommended = FadeFrame.Third;
            else if (second.Compatible) recommended = FadeFrame.Second;
            return new FadeCompatResult { Main = main, Third = third, Second = second, AlphaMask = alphaMask, Recommended = recommended };
        }

        // Main 枠: _Color が白 (1,1,1,1) のときのみ空き。RGB焼き込み不要の (1,1,1,0)↔(1,1,1,1) 駆動を成立させるための条件
        static FadeFrameState CheckMain(Material m)
        {
            var state = new FadeFrameState();
            if (m.HasProperty("_Color"))
            {
                var color = m.GetColor("_Color");
                if (!Approx(color, Color.white))
                {
                    state.NonDefaultProps.Add(new NonDefaultProp
                    {
                        Name = "_Color",
                        Current = color.ToString(),
                        Default = Color.white.ToString(),
                    });
                    state.ShortReason = $"_Color が #{ColorUtility.ToHtmlStringRGBA(color)} (白のみ可)";
                }
            }
            state.Compatible = state.NonDefaultProps.Count == 0;
            return state;
        }

        // 代表的な差分を優先して1行要約を作る: 有効化フラグ > テクスチャ割当 > その他
        static string MakeShortReason(FadeFrameState state)
        {
            NonDefaultProp Pick(System.Func<NonDefaultProp, bool> pred) => state.NonDefaultProps.FirstOrDefault(pred);
            var rep = Pick(p => p.Name.StartsWith("_UseMain") || p.Name == "_AlphaMaskMode")
                ?? Pick(p => p.Current.StartsWith("tex=") && !p.Current.StartsWith("tex=null"))
                ?? state.NonDefaultProps[0];
            var others = state.NonDefaultProps.Count - 1;
            var suffix = others > 0 ? $" (他{others}件)" : "";
            return $"{rep.Name}={Summarize(rep.Current)}{suffix}";
        }

        static string Summarize(string current)
        {
            if (current.StartsWith("tex=")) return current.Split(' ')[0];
            return current;
        }
```

（`using System.Linq;` を追加。）

`Editor/Core/MaterialSlotScanner.cs`: `"3rd/2nd/AlphaMask 全枠使用済み"` を `"main/AlphaMask/3rd/2nd 全枠使用済み"` に変更。

- [ ] **Step 4: GREEN 確認**（全テスト。件数は増減を正確に記録）
- [ ] **Step 5: コミット** `Mainフェード枠と優先度main>alpha>3rd>2nd、利用不可理由の要約を追加`

---

### Task 2: プリセット再構成と BuildFadeTargets の実効枠対応（Setup）

**Files:**
- Modify: `Editor/Core/TransparencyPresets.cs`
- Modify: `Editor/Setup/ToggleMenuSetup.cs`
- Modify: `Editor/UI/CostumeDashboardWindow.cs`（`OneTwoTransOverrides()` 呼び出し箇所を `TransparencyPresets.DriverProps(...)` に追随させる最小修正のみ）
- Test: `Test/TransparencyPresetsTest.cs`, `Test/ToggleMenuSetupTest.cs`

**Interfaces:**
- Produces:
  - `static List<PresetProperty> TransparencyPresets.DriverProps(FadeFrame frame)` — 枠別駆動プロパティのみ。Main→**空リスト**、Third→`_UseMain3rdTex=1,_Main3rdTexBlendMode=3,_Main3rdTexAlphaMode=2`、Second→2nd同様、AlphaMask→`_AlphaMaskMode=2,_AlphaMaskValue=0`
  - `For(frame)` = 透過共通37項目 + `DriverProps(frame)`（既存挙動は Main 以外不変）
  - `OneTwoTransOverrides()` は**削除**し、呼び出し側（window の onetrans/twotrans 分岐）は `TransparencyPresets.DriverProps(effectiveFrame)` を使う
  - `ToggleMenuSetup.Create`: `FadeFrame.Main` → `ToggleShaderVectorParameters[(meshPath, "_Color")] = FadeVector()`
  - `static List<FadeTarget> ToggleMenuSetup.BuildFadeTargets(GameObject avatarRoot, IEnumerable<SlotInfo> slots, IReadOnlyDictionary<int, FadeFrame> frameOverrides)` — キーは `Renderer.GetInstanceID()`。実効枠 = override があればそれ、なければ `slot.FadeCompat?.Recommended`。既存2引数版は `frameOverrides: null` として残す

- [ ] **Step 1: テスト追加・修正（RED）**

`Test/TransparencyPresetsTest.cs`:
- `For_Main_CommonOnly`: `For(FadeFrame.Main)` に `_DstBlend`(=10) は含まれ、`_UseMain3rdTex`/`_UseMain2ndTex`/`_AlphaMaskMode` は含まれない
- `DriverProps_Main_Empty`: `DriverProps(FadeFrame.Main)` は空
- `DriverProps_Third_ThreeProps`: 3項目（既存 OneTwoTransOverrides テストを改名・置換）
- `DriverProps_AlphaMask`: `_AlphaMaskMode=2`, `_AlphaMaskValue=0`

`Test/ToggleMenuSetupTest.cs`:
- `Create_MainFrame_UsesColor`: Main の FadeTarget で `ToggleShaderVectorParameters[("Costume/Top", "_Color")]` が Inactive (1,1,1,0) / Active (1,1,1,1)
- `BuildFadeTargets_FrameOverride_Wins`: デフォルトマテリアル（推奨Main）のメッシュに override で `FadeFrame.Third` を与えると Third の FadeTarget が返る

- [ ] **Step 2: RED 確認** → **Step 3: 実装** → **Step 4: GREEN 確認**（window の `OneTwoTransOverrides` 呼び出しは `DriverProps(group.Preset ?? FadeFrame.Third)` に置換。onetrans/twotrans グループの `Preset` が null の場合は従来同様 Third 駆動にフォールバック）
- [ ] **Step 5: コミット** `プリセットをDriverProps分離しMain対応、BuildFadeTargetsに実効枠overrideを追加`

---

### Task 3: RenderQueueSetup.SetAll（メッシュ一括）

**Files:**
- Modify: `Editor/Setup/RenderQueueSetup.cs`
- Test: `Test/RenderQueueSetupTest.cs`

**Interfaces:**
- Produces: `static CRQ RenderQueueSetup.SetAll(Renderer renderer, int queue)` — レンダラーの**全ての既存 CRQ コンポーネント（specific/wildcard とも）を Undo 付きで削除**し、`MaterialIndex=-1, RenderQueue=queue` の wildcard 1個を作成して返す。specific+wildcard 併存を作らないという既存不変条件を維持する最も単純な形

- [ ] **Step 1: テスト（RED）**: `SetAll_ReplacesAllWithSingleWildcard`（specific 2個ある状態で SetAll → CRQ は1個・MaterialIndex=-1・全スロットの EffectiveQueue が新値）、`SetAll_NoExisting_CreatesWildcard`
- [ ] **Step 2-4: RED → 実装 → GREEN**
- [ ] **Step 5: コミット** `RenderQueueSetup.SetAll: メッシュ単位の一括設定`

---

### Task 4: UI 再構成（メッシュ行・カスタム枠・1クリックToggle・Q一括・理由表示）

**Files:**
- Modify: `Editor/UI/CostumeDashboardWindow.cs`（大規模改修）

**Interfaces:**
- Consumes: Task 1-3 の全成果
- Produces: 最終UI。実装者は既存コードを読み、以下の要求を満たすよう改修する（既存の列生成・アクション配線パターンを踏襲。bindCell でのコールバック蓄積禁止 = ✓列は unregister-before-register / Button は cell.Clear()+新規作成 の既存パターンを守る）

要求仕様:
1. **行階層**: 衣装 > グループ > **メッシュ（RowKind.Mesh 追加。同一グループ内スロットを Renderer ごとに束ねる）** > スロット
2. **メッシュ行**: レンダラー名表示 / [Select]（既存 SelectRenderer をメッシュ行へ移動）/ ✓（`HashSet<long> checkedSlots` を `HashSet<int> checkedMeshes`（Renderer instanceID）に置換。SlotKey は不要になるため削除）/ **[Toggle] ボタン** = そのメッシュのスロット群だけを対象に `ToggleMenuCreateDialog.Show` を直接開く（同一アバター配下チェックは単一メッシュなので不要）/ **[Q] ボタン** = 一括設定ポップアップ（`QueuePopup` を拡張し、メッシュ行からは `RenderQueueSetup.SetAll(renderer, value)`、初期値は slot0 の実効値）/ **フェード枠セレクタ** = `PopupField<string>` で `推奨 / main / alpha / 3rd / 2nd`。選択値は `Dictionary<int, FadeFrame> frameOverrides`（Renderer instanceID キー、ウィンドウの非シリアライズフィールド）へ保存・解除（推奨選択で Remove）。変更時 `Refresh()`
3. **スロット行**: マテリアル / シェーダー / 枠状況4列（main/AM/3rd/2nd の順に並べ替え。tooltip 先頭に `ShortReason`、続けて全差分一覧）/ 推奨列（override 時は `main*` のように `*` 付与で実効枠を表示）/ Queue列 / スロット単位 [Q]（既存）。**Select・✓ ボタンは置かない**
4. **実効枠のグルーピング反映**: `MaterialSlotScanner.GroupByShader` に `Func<SlotInfo, FadeFrame?> effectiveFrame = null` 引数を追加（null なら従来どおり Recommended）。window は `slot => frameOverrides.TryGetValue(slot.Renderer.GetInstanceID(), out var f) ? f : slot.FadeCompat?.Recommended` を渡す。グループの `Preset`・AO ME 分岐・`CreateAOMaterialEditor` のプリセット選択はこの実効枠を使う（`MaterialSlotScanner.cs` の変更もこのタスクに含む。既存テストへの影響なし = デフォルト引数）
5. **ToggleMenuCreateDialog**: 行ごと（メッシュごと）に枠のドロップダウンを表示（初期値 = 実効枠）。作成時は `ToggleMenuSetup.BuildFadeTargets(avatarRoot, slots, dialogOverrides)` を使う
6. **override 枠が使用済の場合**: メッシュ行セレクタの背景/文字を警告色にし tooltip に該当スロットの ShortReason を表示（作成は妨げない = ユーザーの明示選択）
7. ツールバーの [✓ から Toggle Menu作成] は checkedMeshes ベースで維持（複数メッシュまとめ用）

検証: compile → window smoke（開く+実データ確認は `code execute` スモークスクリプト `.aibridge/code/cd_smoke_setup.csx` を流用可、終わったら `cd_smoke_cleanup.csx`）→ 全テスト GREEN（このタスクでの新規テストはなし）

- [ ] **Step 1: 実装** → **Step 2: compile+smoke** → **Step 3: 全テスト GREEN** → **Step 4: コミット** `UI再構成: メッシュ行・カスタム枠セレクタ・1クリックToggle・Q一括・理由表示`

---

### Task 5: README 更新と最終確認

- [ ] README.md の機能一覧に UX改訂内容を反映（mainフェード標準・メッシュ行UI・カスタム枠・Q一括）
- [ ] 全テスト GREEN・compile クリーン確認
- [ ] コミット `README更新（UX改訂）`
