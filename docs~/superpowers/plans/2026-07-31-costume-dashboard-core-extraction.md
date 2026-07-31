# Costume Dashboard Core 抽出 実装計画（Plan 1 / 全3計画）

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** `CostumeDashboardWindow` / `AnimatorLayersView` / `ChooseMenuCreateDialog` の UI クラスに埋まっている組み立て・判定ロジックを `Editor/Core/` と `Editor/Setup/` へ移し、GUI 以外（エージェント経路）からも同じ結果を再現できるようにする。

**Architecture:** 既存の UI クラスから純ロジック部を静的メソッドとして切り出し、UI 側はその呼び出しに置き換える。`EditorUtility.DisplayDialog` / `ShowNotification` / `Selection` などの UI 副作用は呼び出し側（UI）に残し、Core は結果を戻り値で返す。この計画では **Dashboard の外部インターフェースは変更しない**（GUI の挙動は完全に不変）。

**Tech Stack:** Unity 2022.3.22f1 / C# 9.0 / Unity Test Framework (EditMode) / AIBridge CLI

## 全体の位置づけ

設計 spec: `D:\make\devel\claude-vrchat-avatar-skills\docs\specs\2026-07-31-costume-dashboard-agent-integration-design.md`

- **Plan 1（本計画）**: Dashboard 側の Core 抽出。単独で完結し、既存テストで守られる
- **Plan 2**: `net.narazaka.vrchat.avatar-agent-tools` に `costume_*` コマンド 9 本を実装、旧コマンド 7 本を削除
- **Plan 3**: `claude-vrchat-avatar-skills` の旧 exe 4 本削除と SKILL.md 7 本の書き換え

Plan 2 / 3 は Plan 1 完了後、確定した API 形状を見てから書く。

## Global Constraints

- Unity 2022.3.22f1。**C# 9.0 互換**の構文のみ使用（それより新しい構文は使わない）
- 対象ブランチ: `feature/costume-dashboard-agent-integration`（作成済み）
- **version bump / CHANGELOG の release entry は絶対に含めない**（デフォルトブランチでのみ行う）
- `Undo` を使う Setup 系を叩く新規テストフィクスチャは **必ず `Test/UndoCleanupTestBase.cs` を継承**し、破棄は `Track()` に寄せる（素の `DestroyImmediate` はゾンビ復活を招く）
- テスト実行は AIBridge 経由。プロジェクトルート `x:/make/devel/vrchat-AVATAR-SANDBOX` から:
  ```bash
  ./.aibridge/cli/AIBridgeCLI.exe menu_item --menuPath "Assets/Refresh"
  ./.aibridge/cli/AIBridgeCLI.exe menu_item --menuPath "Tools/Costume Dashboard/Run Tests"
  ```
  結果は `Library/CostumeDashboardTestResults.json`（`{"passed":N,"failed":N,"failures":[]}`）を読む
- 新規ファイル作成後は先に `Assets/Refresh` を叩いてから compile / test する。compile 直後の Run Tests はドメインリロードに巻き込まれて結果ファイルが出ないことがあるので、その場合はもう一度叩く
- シーンを変更する操作をしたら、バッチ検証の前にユーザーへ保存判断を仰ぐ（dirty だとセーブダイアログで Unity と AIBridge がブロックする）
- GUI の挙動は変えない。移行後も同じ操作で同じ結果になること

---

### Task 1: Animator 解析ロジックを Core へ移す

`AnimatorLayersView` の解析部は既に `public static` かつ UI 非依存（コメントに「純ロジック部（UI非依存・テスト対象）」と明記済み）。クラスごと Core へ移す。

**Files:**
- Create: `Editor/Core/AnimatorAnalysis/AnimatorEntryBuilder.cs`
- Modify: `Editor/UI/AnimatorLayersView.cs`（16-33 の型定義、92-171 の3メソッドを削除）
- Modify: `Test/AnimatorLayersViewTest.cs`（65, 93, 94 行の参照更新）

**Interfaces:**
- Consumes: `AnimatorSourceCollector` / `LayerPatternClassifier` / `LayerModelSerializer` / `AvatarUtil.FindAvatarRoot`（すべて既存）
- Produces:
  - `Narazaka.VRChat.CostumeDashboard.Editor.SourceEntry`（フィールド: `GameObject AvatarRoot`, `AnimatorSource Source`, `List<LayerEntry> Layers`）
  - `Narazaka.VRChat.CostumeDashboard.Editor.LayerEntry`（フィールド: `AnimatorLayerModel Model`, `ClassificationResult Classification`, `string Id`, `string Json`, `string Compact`, `string TriggerTooltip`）
  - `AnimatorEntryBuilder.Build(List<GameObject> costumeRoots, bool analyzeAvatar) -> List<SourceEntry>`

- [ ] **Step 1: `AnimatorEntryBuilder.cs` を作る**

`Editor/Core/AnimatorAnalysis/AnimatorEntryBuilder.cs` を新規作成し、`AnimatorLayersView.cs` から以下をそのまま移す。

- 16-21 行の `SourceEntry` クラス → ネストではなくトップレベル（`namespace Narazaka.VRChat.CostumeDashboard.Editor`）
- 23-33 行の `LayerEntry` クラス → 同上
- 92-130 行の `BuildEntries` → `AnimatorEntryBuilder.Build` に改名（本体は変更なし）
- 134-160 行の `BuildMenuNameIndex` → `AnimatorEntryBuilder` の `static`（private のまま）
- 162-171 行の `BuildTriggerTooltip` → 同上

`using` は移行元から必要なものを持ってくる（`VRC.SDK3.Avatars.Components` / `VRC.SDK3.Avatars.ScriptableObjects` を含む）。**ロジックは一切変更しない。**

- [ ] **Step 2: `AnimatorLayersView.cs` を委譲に変える**

16-33 の型定義と 92-171 の3メソッドを削除。`Refresh` 内（176 行）の呼び出しを差し替える。

```csharp
entries = AnimatorEntryBuilder.Build(costumeRoots, AnalyzeAvatar);
```

`SourceEntry` / `LayerEntry` を参照している箇所（`Row` クラス、`entries` フィールド、`ExplanationText`、`UpdateDetail`、`UncachedComplex`、`GenerateSource`、`GenerateOne`、`Generate` など）は、同一 namespace のトップレベル型になるので型名の記述はそのまま通る。コンパイルエラーが出た箇所だけ直す。

- [ ] **Step 3: 既存テストの参照を更新**

`Test/AnimatorLayersViewTest.cs` の3箇所を書き換える。

```csharp
// 65 行
var entries = AnimatorEntryBuilder.Build(new List<GameObject> { costume }, false);
// 93 行
Assert.That(AnimatorEntryBuilder.Build(new List<GameObject> { costume }, false).Count, Is.EqualTo(1));
// 94 行
var withAvatar = AnimatorEntryBuilder.Build(new List<GameObject> { costume }, true);
```

- [ ] **Step 4: コンパイルとテスト**

```bash
./.aibridge/cli/AIBridgeCLI.exe menu_item --menuPath "Assets/Refresh"
./.aibridge/cli/AIBridgeCLI.exe compile unity
./.aibridge/cli/AIBridgeCLI.exe menu_item --menuPath "Tools/Costume Dashboard/Run Tests"
```

期待: `Library/CostumeDashboardTestResults.json` が `"failed":0`。移動のみなので全テストが従来どおり通ること。

- [ ] **Step 5: コミット**

```bash
git add Editor/Core/AnimatorAnalysis/AnimatorEntryBuilder.cs Editor/Core/AnimatorAnalysis/AnimatorEntryBuilder.cs.meta Editor/UI/AnimatorLayersView.cs Test/AnimatorLayersViewTest.cs
git commit -m "Animator 解析の純ロジックを AnimatorEntryBuilder へ抽出"
```

---

### Task 2: Reactive Component の配線ロジックを Setup へ移す

`RelocateReactive` / `RemoveReactive` は `ReactiveComponentSetup` の呼び出しに加えて既存 Toggle Menu への配線・孤児掃除を行う。この組み合わせが Window にあるため、GUI 以外から正しく再現できない。

**Files:**
- Modify: `Editor/Setup/ReactiveComponentSetup.cs`（メソッド追加）
- Modify: `Editor/UI/CostumeDashboardWindow.cs`（1097-1122 を委譲に）
- Modify: `Test/ReactiveComponentSetupTest.cs`（テスト追加）

**Interfaces:**
- Consumes: `ReactiveComponentSetup.Relocate` / `.Remove`、`ToggleMenuSetup.CollectMenuTargets` / `.RegisterReactiveWait` / `.UnregisterPath`、`AvatarUtil.RelativePath`（すべて既存）
- Produces:
  - `ReactiveComponentSetup.RelocateAndWire(ReactiveComponent comp, GameObject avatarRoot) -> ReactiveComponent`（移設後のコンポーネントを返す）
  - `ReactiveComponentSetup.RemoveAndUnwire(ReactiveComponent comp, GameObject avatarRoot) -> string`（孤児になったホストのパス。無ければ null）

- [ ] **Step 1: 失敗するテストを書く**

`Test/ReactiveComponentSetupTest.cs` に追加する。既存フィクスチャが `UndoCleanupTestBase` を継承していることを確認し、していなければ継承させる。

```csharp
[Test]
public void RelocateAndWire_RegistersReactiveWaitOnMatchingToggleMenu()
{
    // アバタールート > 衣装 > メッシュ（Renderer 付き）に Reactive Component を置く
    var avatarRoot = Track(new GameObject("Avatar"));
    var costume = Track(new GameObject("Costume"));
    costume.transform.SetParent(avatarRoot.transform);
    var mesh = Track(new GameObject("Mesh"));
    mesh.transform.SetParent(costume.transform);
    var comp = Undo.AddComponent<ModularAvatarShapeChanger>(mesh);

    // メッシュを toggle 対象に含む既存 Toggle Menu を作る
    var menuHost = Track(new GameObject("Menu"));
    menuHost.transform.SetParent(costume.transform);
    var creator = ToggleMenuSetup.Create(menuHost, new[] { "Costume/Mesh" },
        new List<ToggleMenuSetup.FadeTarget>(), 1f);

    var moved = ReactiveComponentSetup.RelocateAndWire(comp, avatarRoot);

    Assert.That(moved, Is.Not.Null);
    var childPath = AvatarUtil.RelativePath(avatarRoot, moved.gameObject);
    var (_, targets) = ToggleMenuSetup.CollectMenuTargets(avatarRoot).First();
    Assert.That(targets, Does.Contain(childPath), "移設先が既存メニューへ変化待機として登録されること");
}
```

`ModularAvatarShapeChanger` の実型名・`ToggleMenuSetup.Create` / `CollectMenuTargets` / `FadeTarget` の正確なシグネチャは、実装前に `Editor/Setup/ToggleMenuSetup.cs` と既存の `Test/ReactiveComponentSetupTest.cs` を読んで合わせること。既存テストが使っている生成ヘルパがあればそれを流用する。

- [ ] **Step 2: テストが失敗することを確認**

```bash
./.aibridge/cli/AIBridgeCLI.exe menu_item --menuPath "Assets/Refresh"
./.aibridge/cli/AIBridgeCLI.exe menu_item --menuPath "Tools/Costume Dashboard/Run Tests"
```

期待: `RelocateAndWire` が存在せずコンパイルエラー、または該当テストが FAIL。

- [ ] **Step 3: `RelocateAndWire` / `RemoveAndUnwire` を実装**

`Editor/Setup/ReactiveComponentSetup.cs` に追加する。中身は `CostumeDashboardWindow` の 1097-1122 と同一。

```csharp
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
```

- [ ] **Step 4: テストが通ることを確認**

Step 2 と同じコマンド。期待: `"failed":0`。

- [ ] **Step 5: Window を委譲に変える**

`CostumeDashboardWindow.cs` の 1097-1122 を削除し、`ReactivePopup` からの呼び出し（`window.RemoveReactive(...)` / `window.RelocateReactive(...)`）を直接 `ReactiveComponentSetup.RemoveAndUnwire(...)` / `.RelocateAndWire(...)` に置き換える。`ReactivePopup` が `window` 参照を他に使っていなければフィールドごと削除してよい（`Refresh` 呼び出しに使っている場合は残す）。

- [ ] **Step 6: コンパイルとテスト、コミット**

```bash
./.aibridge/cli/AIBridgeCLI.exe menu_item --menuPath "Assets/Refresh"
./.aibridge/cli/AIBridgeCLI.exe compile unity
./.aibridge/cli/AIBridgeCLI.exe menu_item --menuPath "Tools/Costume Dashboard/Run Tests"

git add Editor/Setup/ReactiveComponentSetup.cs Editor/UI/CostumeDashboardWindow.cs Test/ReactiveComponentSetupTest.cs
git commit -m "Reactive Component の Toggle Menu 配線を ReactiveComponentSetup へ抽出"
```

---

### Task 3: AO Material Editor の組み立てを Setup へ移す

`AOMEAvailability`（判定）、`AOMEHostSuffix`（ホスト名生成）、`CreateAOMaterialEditor`（preset・AlphaMask 調整の組み立て）、`CreateAOMEBatch`（一括）、`FindOrCreateChild` を移す。`EditorUtility.DisplayDialog` は戻り値に変える。

**Files:**
- Modify: `Editor/Setup/AOMaterialEditorSetup.cs`（メソッド追加）
- Modify: `Editor/UI/CostumeDashboardWindow.cs`（1199-1215, 1236-1351, 1353-1361 を削除・委譲）
- Modify: `Test/AOMaterialEditorSetupTest.cs`（テスト追加）

**Interfaces:**
- Consumes: `AOMaterialEditorSetup.Apply` / `.IsAvailable`、`TransparencyPresets.For` / `.OneTwoTransProps` / `.TransparentModeOverride` / `.AlphaMaskModeOverride`、`AvatarUtil.RelativePath`、`SlotGroup`（すべて既存）
- Produces:
  - `AOMaterialEditorSetup.Availability(GameObject avatarRoot, SlotGroup group) -> (bool Enabled, string Reason)`
  - `AOMaterialEditorSetup.HostSuffix(SlotGroup group) -> string`
  - `AOMaterialEditorSetup.CreateForGroup(GameObject costume, GameObject avatarRoot, SlotGroup group) -> string`（成功時 null、失敗時はエラーメッセージ）
  - `AOMaterialEditorSetup.CreateBatch(GameObject costume, GameObject avatarRoot, List<SlotGroup> groups) -> (int Created, int Skipped)`

`Availability` の引数から `costume` を落としている点に注意（現行の `AOMEAvailability` は `costume` を受け取るが本体で使っていない）。

- [ ] **Step 1: 失敗するテストを書く**

`Test/AOMaterialEditorSetupTest.cs` に追加する。既存テストの `SlotGroup` 生成ヘルパを流用すること。

```csharp
[Test]
public void Availability_UnknownFamilyOneTwoTrans_ReturnsDisabledWithReason()
{
    var avatarRoot = Track(new GameObject("Avatar"));
    var group = new SlotGroup
    {
        Family = "unknown",
        Variant = "onetrans",
        SupportsFade = true,
        FadeDisabledReason = "未知のシェーダー",
    };

    var (enabled, reason) = AOMaterialEditorSetup.Availability(avatarRoot, group);

    Assert.That(enabled, Is.False);
    Assert.That(reason, Is.EqualTo("未知のシェーダー"));
}

[Test]
public void Availability_NullAvatarRoot_ReturnsDisabled()
{
    var group = new SlotGroup { SupportsFade = true, CanSetupFade = true };

    var (enabled, reason) = AOMaterialEditorSetup.Availability(null, group);

    Assert.That(enabled, Is.False);
    Assert.That(reason, Is.EqualTo("アバタールートが見つかりません"));
}
```

`AOMaterialEditorSetup.IsAvailable`（aoyon.material-editor 未導入なら false）が先に効くため、未導入環境では両テストとも「未導入」理由で落ちる。既存テストが `IsAvailable` をどう扱っているか確認し、同じ方針（未導入時は `Assert.Ignore` するなど）に合わせること。

- [ ] **Step 2: テストが失敗することを確認**

```bash
./.aibridge/cli/AIBridgeCLI.exe menu_item --menuPath "Assets/Refresh"
./.aibridge/cli/AIBridgeCLI.exe menu_item --menuPath "Tools/Costume Dashboard/Run Tests"
```

期待: `Availability` が存在せずコンパイルエラー。

- [ ] **Step 3: `Availability` と `HostSuffix` を実装**

`AOMEAvailability`（1199-1215）と `AOMEHostSuffix`（1236-1263）を `AOMaterialEditorSetup` へそのまま移す。`Availability` は `costume` 引数を削る。コメントも一緒に持ってくること（`onetrans`/`twotrans` と Main 枠に関する説明は残す価値がある）。

- [ ] **Step 4: `CreateForGroup` を実装**

`CreateAOMaterialEditor`（1264-1322）を移す。`EditorUtility.DisplayDialog` の箇所だけ差し替える。

```csharp
public static string CreateForGroup(GameObject costume, GameObject avatarRoot, SlotGroup group)
{
    var suffix = HostSuffix(group);
    var host = FindOrCreateChild(FindOrCreateChild(costume, "trans"), suffix);

    var slots = group.Slots
        .Where(s => s.Renderer != null)
        .Select(s => new SlotTarget
        {
            RendererPath = AvatarUtil.RelativePath(avatarRoot, s.Renderer.gameObject),
            MaterialIndex = s.SlotIndex,
        })
        .Where(s => !string.IsNullOrEmpty(s.RendererPath))
        .ToList();

    Shader shader = null;
    if (group.NeedsShaderOverride)
    {
        shader = AssetDatabase.LoadAssetAtPath<Shader>(AssetDatabase.GUIDToAssetPath(group.TransparentGuid));
        if (shader == null) return $"透過版シェーダーが見つかりません (GUID: {group.TransparentGuid})";
    }

    // 以降 1291-1321 と同一（effectivePreset / properties / AlphaMask 調整 / Apply 呼び出し）

    Apply(host, slots, shader, properties);
    return null;
}
```

`FindOrCreateChild`（1353-1361）も `AOMaterialEditorSetup` へ private static として移す。`Undo.RegisterCreatedObjectUndo` はそのまま残す（prefab contents 経路でも安全であることを 2026-07-31 に実測確認済み）。

- [ ] **Step 5: `CreateBatch` を実装**

`CreateAOMEBatch`（1325-1351）を移す。`Refresh()` と `ShowNotification` は呼び出し側（Window）に残し、件数を戻り値で返す。

```csharp
public static (int Created, int Skipped) CreateBatch(GameObject costume, GameObject avatarRoot, List<SlotGroup> groups)
{
    var created = 0;
    var skipped = 0;
    // グループキー/ホスト suffix の設計上、通常は同一バッチ内で suffix が重複することはないが、
    // 万一の回帰（キー正規化漏れ等）で衝突した場合に SlotTargets を後勝ちで上書きしてしまう事故を防ぐ防御線
    var usedSuffixes = new HashSet<string>();
    foreach (var group in groups)
    {
        var (enabled, _) = Availability(avatarRoot, group);
        if (!enabled) { skipped++; continue; }
        var suffix = HostSuffix(group);
        if (!usedSuffixes.Add(suffix)) { skipped++; continue; }
        if (CreateForGroup(costume, avatarRoot, group) != null) { skipped++; continue; }
        created++;
    }
    return (created, skipped);
}
```

- [ ] **Step 6: Window を委譲に変える**

1199-1215, 1236-1351, 1353-1361 を削除。呼び出し元を書き換える。

- 998 行: `AOMEAvailability(row.Costume, row.AvatarRoot, g).Item1` → `AOMaterialEditorSetup.Availability(row.AvatarRoot, g).Enabled`
- 1018 行: `var (enabled, reason) = AOMaterialEditorSetup.Availability(row.AvatarRoot, row.Group);`
- 999 行の `CreateAOMEBatch(...)` 呼び出しを次に置き換える。

```csharp
var button = new Button(() =>
{
    var (created, skipped) = AOMaterialEditorSetup.CreateBatch(row.Costume, row.AvatarRoot, row.CostumeGroups);
    Refresh();
    ShowNotification(new GUIContent($"AO ME: {created}グループ作成 / {skipped}スキップ"));
}) { text = "AO ME一括" };
```

- グループ行の単体作成ボタン（1017 行付近）は `CreateForGroup` の戻り値を見てエラーなら `EditorUtility.DisplayDialog` を出す

`AOMEHostSuffix` を tooltip 生成などで参照している箇所（1236 行の定義以外）があれば `AOMaterialEditorSetup.HostSuffix` に置き換える。

- [ ] **Step 7: コンパイルとテスト、コミット**

```bash
./.aibridge/cli/AIBridgeCLI.exe menu_item --menuPath "Assets/Refresh"
./.aibridge/cli/AIBridgeCLI.exe compile unity
./.aibridge/cli/AIBridgeCLI.exe menu_item --menuPath "Tools/Costume Dashboard/Run Tests"

git add Editor/Setup/AOMaterialEditorSetup.cs Editor/UI/CostumeDashboardWindow.cs Test/AOMaterialEditorSetupTest.cs
git commit -m "AO ME の可否判定と組み立てを AOMaterialEditorSetup へ抽出"
```

---

### Task 4: Toggle Menu 作成ロジックを Setup へ移す

`ToggleMenuCreatePopup.Create`（1683-1707）の中身を移す。特に「移設済み Reactive Component の自動包含」は GUI 以外から再現できない重要ロジック。

**Files:**
- Modify: `Editor/Setup/ToggleMenuSetup.cs`（メソッド追加）
- Modify: `Editor/UI/CostumeDashboardWindow.cs`（1683-1707 を委譲に）
- Modify: `Test/ToggleMenuSetupTest.cs`（テスト追加）

**Interfaces:**
- Consumes: `ToggleMenuSetup.BuildFadeTargets` / `.Create`、`ReactiveComponentSetup.Scan` / `.IsRelocated`、`AvatarUtil.RelativePath`（すべて既存）
- Produces:
  - `ToggleMenuSetup.CreateForSlots(GameObject costume, GameObject avatarRoot, List<SlotInfo> slots, IReadOnlyDictionary<int, FadeFrame> frameOverrides, string menuName, float transitionSeconds) -> AvatarToggleMenuCreator`

- [ ] **Step 1: 失敗するテストを書く**

`Test/ToggleMenuSetupTest.cs` に追加する。既存テストのメッシュ・スロット生成ヘルパを流用すること。

```csharp
[Test]
public void CreateForSlots_IncludesRelocatedReactiveComponentsAsWaitTargets()
{
    // アバタールート > 衣装 > メッシュ、メッシュ配下に移設済み Reactive Component を置く
    var avatarRoot = Track(new GameObject("Avatar"));
    var costume = Track(new GameObject("Costume"));
    costume.transform.SetParent(avatarRoot.transform);
    var mesh = Track(new GameObject("Mesh"));
    mesh.transform.SetParent(costume.transform);
    var renderer = mesh.AddComponent<SkinnedMeshRenderer>();

    // 移設済み状態（(ホスト名)_reactive 配下）を作る
    var reactiveHost = Track(new GameObject("Mesh_reactive"));
    reactiveHost.transform.SetParent(mesh.transform);
    Undo.AddComponent<ModularAvatarShapeChanger>(reactiveHost);

    var slots = new List<SlotInfo> { new SlotInfo { Renderer = renderer, SlotIndex = 0 } };

    var creator = ToggleMenuSetup.CreateForSlots(costume, avatarRoot, slots, null, "テストメニュー", 1f);

    Assert.That(creator, Is.Not.Null);
    Assert.That(creator.gameObject.name, Is.EqualTo("テストメニュー"));
    Assert.That(creator.gameObject.transform.parent, Is.EqualTo(costume.transform));
    var (_, targets) = ToggleMenuSetup.CollectMenuTargets(avatarRoot).First();
    Assert.That(targets, Does.Contain("Costume/Mesh"));
    Assert.That(targets, Does.Contain("Costume/Mesh/Mesh_reactive"), "移設済み Reactive Component が変化待機として含まれること");
}
```

`ReactiveComponentSetup.IsRelocated` が何を「移設済み」と判定するか（`(ホスト名)_reactive` という命名規則）は `Editor/Setup/ReactiveComponentSetup.cs:35` を読んで正確に合わせること。テストのセットアップがこの判定を満たしていないと意味のないテストになる。

- [ ] **Step 2: テストが失敗することを確認**

```bash
./.aibridge/cli/AIBridgeCLI.exe menu_item --menuPath "Assets/Refresh"
./.aibridge/cli/AIBridgeCLI.exe menu_item --menuPath "Tools/Costume Dashboard/Run Tests"
```

期待: `CreateForSlots` が存在せずコンパイルエラー。

- [ ] **Step 3: `CreateForSlots` を実装**

`Editor/Setup/ToggleMenuSetup.cs` に追加する。中身は 1683-1707 と同一で、`onCreated()` の呼び出しだけ落とす（UI の責務）。

```csharp
/// <summary>衣装配下に menuName のホストを作り、slots のメッシュを対象とした Toggle Menu を作成する。
/// 対象メッシュ配下の移設済み Reactive Component は ON=表示＋変化待機99% で自動包含する
/// （フェード完了直前まで適用を遅延させ、フェード中に素体の変化が見えるのを防ぐ）</summary>
public static AvatarToggleMenuCreator CreateForSlots(GameObject costume, GameObject avatarRoot,
    List<SlotInfo> slots, IReadOnlyDictionary<int, FadeFrame> frameOverrides, string menuName, float transitionSeconds)
{
    var togglePaths = slots
        .Select(s => AvatarUtil.RelativePath(avatarRoot, s.Renderer.gameObject))
        .Where(p => !string.IsNullOrEmpty(p))
        .Distinct()
        .ToList();
    var fades = BuildFadeTargets(avatarRoot, slots, frameOverrides);

    var reactiveWaitPaths = slots
        .Select(s => s.Renderer).Where(r => r != null).Distinct()
        .SelectMany(r => ReactiveComponentSetup.Scan(r.gameObject).Where(ReactiveComponentSetup.IsRelocated))
        .Select(c => AvatarUtil.RelativePath(avatarRoot, c.gameObject))
        .Where(p => !string.IsNullOrEmpty(p))
        .Distinct()
        .ToList();

    var host = new GameObject(menuName);
    host.transform.SetParent(costume.transform, false);
    Undo.RegisterCreatedObjectUndo(host, "Create Toggle Menu");
    return Create(host, togglePaths, fades, transitionSeconds, reactiveWaitPaths);
}
```

`BuildFadeTargets` は `frameOverrides` が null のときのオーバーロード（31 行の3引数版）と使い分けが要る。null を渡して落ちる場合は、`frameOverrides ?? new Dictionary<int, FadeFrame>()` で吸収するか2引数版へ分岐すること。

- [ ] **Step 4: テストが通ることを確認**

Step 2 と同じコマンド。期待: `"failed":0`。

- [ ] **Step 5: Popup を委譲に変える**

`ToggleMenuCreatePopup.Create`（1683-1707）を次に置き換える。

```csharp
void Create()
{
    ToggleMenuSetup.CreateForSlots(costume, avatarRoot, slots, dialogOverrides, menuName, transitionSeconds);
    onCreated();
}
```

- [ ] **Step 6: コンパイルとテスト、コミット**

```bash
./.aibridge/cli/AIBridgeCLI.exe menu_item --menuPath "Assets/Refresh"
./.aibridge/cli/AIBridgeCLI.exe compile unity
./.aibridge/cli/AIBridgeCLI.exe menu_item --menuPath "Tools/Costume Dashboard/Run Tests"

git add Editor/Setup/ToggleMenuSetup.cs Editor/UI/CostumeDashboardWindow.cs Test/ToggleMenuSetupTest.cs
git commit -m "Toggle Menu 作成フローを ToggleMenuSetup へ抽出"
```

---

### Task 5: Choose Menu 作成ロジックを Setup へ移す

`ChooseMenuCreateDialog.Create`（155-205）から、選択肢名の設定・カラバリ適用・件数集計を移す。`DisplayDialog` / `Selection.activeObject` / `onCreated` は UI に残す。

**Files:**
- Modify: `Editor/Setup/ChooseMenuVariantSetup.cs`（メソッド追加）
- Modify: `Editor/UI/ChooseMenuCreateDialog.cs`（155-205 を委譲に）
- Modify: `Test/ChooseMenuVariantSetupTest.cs`（テスト追加）

**Interfaces:**
- Consumes: `ChooseMenuSetup.Create`、`ChooseMenuVariantSetup.SetChooseName` / `.ApplyVariant` / `.MissingSlot`（すべて既存）
- Produces:
  - `ChooseMenuVariantSetup.ApplyRows(AvatarChooseMenu menu, GameObject avatarRoot, IReadOnlyList<GameObject> costumeRoots, IReadOnlyList<RowInput> rows, int baseChooseIndex) -> (int Applied, List<MissingSlot> Missing)`
  - `ChooseMenuVariantSetup.RowInput`（フィールド: `string Name`, `IReadOnlyList<GameObject> Variants` — `Variants[c]` が `costumeRoots[c]` に対応、null 可）

- [ ] **Step 1: 失敗するテストを書く**

`Test/ChooseMenuVariantSetupTest.cs` に追加する。既存テストの衣装・カラバリ生成ヘルパを流用すること。

```csharp
[Test]
public void ApplyRows_SetsChooseNamesAndAppliesVariants()
{
    // 既存テストのヘルパで「衣装ルート + メッシュ + マテリアル」と、色違いの variant を用意する
    var avatarRoot = Track(new GameObject("Avatar"));
    var costume = /* 既存ヘルパで生成 */;
    var variantRed = /* 既存ヘルパで生成（同じ構造・別マテリアル） */;

    var menu = /* ChooseMenuSetup.Create で作った creator の AvatarChooseMenu */;

    var rows = new List<ChooseMenuVariantSetup.RowInput>
    {
        new ChooseMenuVariantSetup.RowInput { Name = "既定", Variants = new GameObject[] { null } },
        new ChooseMenuVariantSetup.RowInput { Name = "赤", Variants = new[] { variantRed } },
    };

    var (applied, missing) = ChooseMenuVariantSetup.ApplyRows(menu, avatarRoot,
        new[] { costume }, rows, 0);

    Assert.That(applied, Is.GreaterThan(0), "赤の行でマテリアルが流し込まれること");
    Assert.That(missing, Is.Empty);
}
```

`AvatarChooseMenu` の選択肢名がどのプロパティに入るかは `ChooseMenuVariantSetup.SetChooseName`（117 行）を読んで確認し、名前が設定されたことも assert に加えること。

- [ ] **Step 2: テストが失敗することを確認**

```bash
./.aibridge/cli/AIBridgeCLI.exe menu_item --menuPath "Assets/Refresh"
./.aibridge/cli/AIBridgeCLI.exe menu_item --menuPath "Tools/Costume Dashboard/Run Tests"
```

期待: `ApplyRows` / `RowInput` が存在せずコンパイルエラー。

- [ ] **Step 3: `RowInput` と `ApplyRows` を実装**

`Editor/Setup/ChooseMenuVariantSetup.cs` に追加する。中身は `ChooseMenuCreateDialog.Create` の 178-194 と同一。

```csharp
public class RowInput
{
    public string Name;
    /// <summary>costumeRoots と同じ順序。null 要素はその衣装に割り当てなし</summary>
    public IReadOnlyList<GameObject> Variants;
}

/// <summary>各行の選択肢名を設定し、割り当てられたカラバリからマテリアルを流し込む。
/// 戻り値は適用スロット数と、対応するスロットが無く未設定のまま残した内訳</summary>
public static (int Applied, List<MissingSlot> Missing) ApplyRows(AvatarChooseMenu menu, GameObject avatarRoot,
    IReadOnlyList<GameObject> costumeRoots, IReadOnlyList<RowInput> rows, int baseChooseIndex)
{
    var applied = 0;
    var missing = new List<MissingSlot>();
    for (var i = 0; i < rows.Count; i++)
    {
        var row = rows[i];
        var chooseIndex = baseChooseIndex + i;
        SetChooseName(menu, chooseIndex, row.Name);
        if (row.Variants == null) continue;
        for (var c = 0; c < costumeRoots.Count; c++)
        {
            var variant = c < row.Variants.Count ? row.Variants[c] : null;
            if (variant == null) continue;
            var result = ApplyVariant(menu, avatarRoot, costumeRoots[c], variant, chooseIndex);
            applied += result.Applied;
            missing.AddRange(result.Missing);
        }
    }
    return (applied, missing);
}
```

現行の `RowHasVariants(i)` によるスキップは、`ApplyRows` 内で「`Variants` が全て null なら `ApplyVariant` を呼ばない」ことで等価になる（上のループは null をスキップするので追加の分岐は不要）。ただし `SetChooseName` は行に variant が無くても呼ばれる必要がある点に注意（現行も `RowHasVariants` チェックの前に呼んでいる）。

- [ ] **Step 4: テストが通ることを確認**

Step 2 と同じコマンド。期待: `"failed":0`。

- [ ] **Step 5: Dialog を委譲に変える**

`ChooseMenuCreateDialog.Create` の 178-194 を次に置き換える。`DisplayDialog` / `SetDirty` / `LogMissing` / `Selection` / `onCreated` はそのまま残す。

```csharp
var (applied, missing) = ChooseMenuVariantSetup.ApplyRows(menu, avatarRoot,
    costumes.Select(c => c.Costume).ToList(),
    rows.Select(r => new ChooseMenuVariantSetup.RowInput { Name = r.Name, Variants = r.Variants }).ToList(),
    baseIndex);
```

`rows[i].Variants` の型が `List<GameObject>` などであれば `RowInput.Variants` にそのまま渡せる。型が合わない場合は `.ToList()` で吸収する。

- [ ] **Step 6: コンパイルとテスト、コミット**

```bash
./.aibridge/cli/AIBridgeCLI.exe menu_item --menuPath "Assets/Refresh"
./.aibridge/cli/AIBridgeCLI.exe compile unity
./.aibridge/cli/AIBridgeCLI.exe menu_item --menuPath "Tools/Costume Dashboard/Run Tests"

git add Editor/Setup/ChooseMenuVariantSetup.cs Editor/UI/ChooseMenuCreateDialog.cs Test/ChooseMenuVariantSetupTest.cs
git commit -m "色変えメニューの選択肢適用を ChooseMenuVariantSetup へ抽出"
```

---

### Task 6: GUI 動作確認

移行によって GUI の挙動が変わっていないことを実機で確認する。自動テストでは UI 経路を検証できないため。

**Files:** なし（確認のみ）

- [ ] **Step 1: ユーザーへ確認を依頼**

Unity で `Tools/Costume Dashboard` を開き、次を実行して従来どおり動くことを確認してもらう。

1. 衣装を登録し、メッシュビューでスロット一覧が表示される
2. 衣装行の [AO ME一括] でグループが作成され、件数通知が出る
3. グループ行の [AO ME] 単体作成が動く（透過シェーダーが見つからない場合はダイアログが出る）
4. メッシュ行の [Toggle] でメニューが作成され、移設済み Reactive Component があれば変化待機に含まれる
5. 衣装行の [色変え] でダイアログが開き、カラバリを割り当てて作成できる
6. Reactive Component の [RC] ボタンで移設・削除ができ、既存 Toggle Menu への配線が更新される
7. Animator ビューで layer 一覧と分類が表示される

**シーンを変更するので、確認前後でユーザーに保存判断を仰ぐこと。**

- [ ] **Step 2: 問題があれば修正、なければ Plan 1 完了**

---

## 自己レビュー結果

**spec カバレッジ:** spec「Dashboard 側の変更」節の全項目に対応するタスクがある。

| spec の項目 | 対応タスク |
|---|---|
| `CreateAOMEBatch` / `CreateAOMaterialEditor` | Task 3 |
| `CreateToggleMenu` / `OpenToggleMenuForMesh` | Task 4 |
| `CreateChooseMenuForCostume` / `CreateChooseMenuBulk` | Task 5 |
| `FindOrCreateChild` | Task 3 |
| `RelocateReactive` / `RemoveReactive` | Task 2 |
| `AnimatorLayersView.BuildEntries` 系 | Task 1 |

**未着手として残すもの（意図的）:**

- `CollectCheckedSlots` / `CollectChooseSlots` / `CollectMeshes`（Window の対象収集）は `checkedMeshes`（UI のチェック状態）に依存する。エージェント経路では「衣装全体」または「指定メッシュ」を明示的に受け取るため、この収集ロジックは移行不要と判断した。Plan 2 でコマンド側の引数設計を確定する際に再確認する
- `ShowChooseMenuDialog` / `CreateToggleMenu` のバリデーション（同一アバター配下チェック）は、Plan 2 のコマンド側で同等の検証を実装する。UI 用の `DisplayDialog` とはエラー表現が異なるため移行せず、Plan 2 で書く

**型の一貫性:** Task 2 の `RelocateAndWire` / `RemoveAndUnwire`、Task 3 の `Availability` / `HostSuffix` / `CreateForGroup` / `CreateBatch`、Task 4 の `CreateForSlots`、Task 5 の `ApplyRows` / `RowInput` は、いずれも他タスクから参照されない独立した追加。Task 3 の `CreateBatch` のみ内部で `Availability` / `HostSuffix` / `CreateForGroup` を使うが、同一タスク内で定義している。

**既存 API との衝突確認:** `AOMaterialEditorSetup` には既に `Apply` / `HasComponent` / `IsAvailable` があり、追加する4メソッドと名前が衝突しない。`ToggleMenuSetup` の `Create`（97 行）と `CreateForSlots` も別名。`ChooseMenuVariantSetup` の既存4メソッドとも衝突しない。
