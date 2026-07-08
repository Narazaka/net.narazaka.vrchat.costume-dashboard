# 色変えメニュー雛形作成 設計

- 日付: 2026-07-09
- 対象パッケージ: `net.narazaka.vrchat.costume-dashboard`
- 関連: 既存の Toggle Menu 作成（`ToggleMenuSetup` / `CostumeDashboardWindow`）、スキル `vrchat-avatar-color-variation-menu`

## 目的

選択した衣装のマテリアルスロットを対象にした `AvatarChooseMenuCreator`（色変え／カラーバリエーションメニュー）を **雛形（scaffold）** として1クリックで作成する。各スロットの選択肢0に現在のマテリアルを入れておき、残りの色マテリアルはユーザーがコンポーネントのインスペクタで埋める。

本機能は「切替対象スロットを列挙して器を作る」ところまでを担う。実際の色マテリアルをカラバリ prefab 差分から流し込む処理（スキルの `BuildChooseMaterialsFromPrefabs` 相当）は非スコープ。

## 決定事項

- ホスト GameObject は **`<アバタールート>/色`**（衣装ルート単位ではない）。対象メッシュを**アバタールート単位**でグループ化し、アバタールートごとに1つの Choose Menu に全スロットを集約する。衣装ごとに分けない。
- **常に新規 GameObject を作成**する。既存 `AvatarChooseMenuCreator` の再利用・マージ・Toggle との同居衝突チェックはしない（雛形なので毎回まっさらな器を作る前提）。
- GameObject 名は `GameObjectUtility.GetUniqueNameForSibling(avatarRoot, "色")` で一意化（初回「色」、以降「色 (1)」…）。これは重複マージ回避のためではなく、パラメータ名（= GameObject 名フォールバック）の重複による MA パラメータ衝突を避けるための標準的な新規作成命名。
- 対象スロット = 対象メッシュ配下で **Material≠null の全スロット**（全シェーダーファミリー。色替えはマテリアル差し替えなので lilToon 以外も対象）。null マテリアルのスロットは除外。EditorOnly メッシュは `MaterialSlotScanner` が既に除外済み。

## コンポーネント情報（AvatarMenuCreaterForMA）

- typeName: `net.narazaka.avatarmenucreator.components.AvatarChooseMenuCreator`（`AvatarChooseMenu AvatarChooseMenu` を保持）
- 色替えの本体: `AvatarChooseMenu.ChooseMaterials`（キー `(string meshPath, int slotIndex)` → `IntMaterialDictionary`（選択肢index → Material））
- 既定・共通フィールド（`AvatarMenuBase` / `AvatarChooseMenu`）: `TransitionSeconds`(float)、`Saved`(bool)、`Synced`(bool)、`ChooseCount`(int, 既定2)、`ChooseDefaultValue`(int)、`UseParentMenu`(bool, 既定true)
- `ParameterName` は未設定時 GameObject 名にフォールバックする（`AvatarMenuCreatorBase.ParameterName`）。よって GameObject 名「色」がメニュー名・パラメータ名になる
- `AvatarToggleMenuCreator` と同一 GameObject には付与不可（専用 `色` を新規に作るため衝突しない）
- 依存は Toggle 同様に既存の asmdef 参照（`net.narazaka.avatarmenucreator` / `.components`）で満たされる

## 全体構成

```
Editor/
  Setup/
    ChooseMenuSetup.cs   … AvatarChooseMenuCreator の新規作成・スロット列挙（書き込み、Undo対応）
  UI/
    CostumeDashboardWindow.cs … ツールバー／衣装行ボタンの追加、対象決定と呼び出し
```

### ChooseMenuSetup（Setup/ChooseMenuSetup.cs）

```csharp
public static AvatarChooseMenuCreator Create(GameObject avatarRoot, IEnumerable<SlotInfo> slots)
```

- `avatarRoot` 配下に `GameObjectUtility.GetUniqueNameForSibling(avatarRoot.transform, "色")` 名の子 GameObject を新規作成（`Undo.RegisterCreatedObjectUndo`）。
- その GameObject に `Undo.AddComponent<AvatarChooseMenuCreator>` で**常に新規**付与。
- `menu.TransitionSeconds = 0` / `menu.Saved = true` / `menu.Synced = true` / `menu.ChooseCount = 2` / `menu.ChooseDefaultValue = 0` / `menu.UseParentMenu = true`。
- `slots` のうち `Material != null` の各スロットについて、`meshPath = AvatarUtil.RelativePath(avatarRoot, slot.Renderer.gameObject)`（root プレフィクスなし相対、null/空はスキップ）で `menu.ChooseMaterials[(meshPath, slot.SlotIndex)] = new IntMaterialDictionary { [0] = slot.Material }` を登録。
- `EditorUtility.SetDirty(creator)`、作成した creator を返す。
- 対象スロットが0件（列挙後に1件も登録されない）なら GameObject を作らず null を返す（呼び出し側で通知）。

### 対象決定ヘルパー（テスト可能に分離）

UI から切り出し、`SlotInfo` 群 + チェック集合 + アバタールート解決を受けて「アバタールート → そのアバターの対象スロット群」を返す純ロジックにする。

- 入力: 対象 `SlotInfo` 群（対象メッシュのスロット）
- グルーピング: 各スロットの `Renderer` から `AvatarUtil.FindAvatarRoot` でアバタールートを解決し、アバタールート単位で束ねる。アバタールート未解決のスロットは対象外
- 出力: `List<(GameObject AvatarRoot, List<SlotInfo> Slots)>`

「対象メッシュ」の決定（チェック考慮）は UI 側で行う:
- チェックが1つ以上あれば → チェック済みメッシュのスロットのみ
- チェックが無ければ → 対象範囲（ツールバー = 全登録衣装／衣装行 = その衣装）配下の全メッシュのスロット

### UI 配線（CostumeDashboardWindow）

- **ツールバーボタン**「色変えメニュー一括作成」: 対象メッシュ = チェックがあれば全チェック済みメッシュ、無ければ全登録衣装の全メッシュ。ヘルパーでアバタールート単位にグループ化し、各アバタールートに `ChooseMenuSetup.Create` を1回ずつ実行。作成数を通知
- **衣装行ボタン**「色変え雛形」（[AO ME一括] の隣）: 対象メッシュ = その衣装配下でチェックがあればそれのみ、無ければその衣装の全メッシュ。その衣装のアバタールートに `ChooseMenuSetup.Create` を実行

いずれも作成後、生成した `色` GameObject を `Selection` に入れてユーザーがすぐ編集に入れるようにする（任意・初版で実施）。

## エラー処理

- 対象範囲にアバタールート不明の衣装しかない／対象スロット0件 → `EditorUtility.DisplayDialog` で通知し何もしない
- アバタールート未解決の衣装（スロット）はグループ化時に除外（他操作と同じ扱い）
- ツールバーでチェックが複数アバターにまたがる場合もアバタールート単位で分割するため衝突しない
- 書き込みは GameObject 作成・AddComponent とも Undo 対応

## テスト（EditMode）

`ChooseMenuSetup`:
- `Create` が Material≠null の全スロットを `ChooseMaterials` に列挙し、各キーの選択肢0に現在マテリアルを入れる
- 既定値 `ChooseCount=2` / `ChooseDefaultValue=0` / `TransitionSeconds=0` / `UseParentMenu=true` / `Saved=true` / `Synced=true`
- `meshPath` が root プレフィクスなしの avatar-root 相対
- null マテリアルスロットを `ChooseMaterials` に入れない
- 対象スロット0件で null を返し GameObject を作らない
- 2回呼ぶと別々の新規 GameObject（「色」「色 (1)」）が作られる（マージしない）

対象決定ヘルパー:
- スロット群をアバタールート単位でグループ化する（複数アバターが分かれる）
- アバタールート未解決のスロットを除外する

（UI ボタンのチェック有無分岐は薄いラッパーとしてヘルパー呼び出しに集約し、ロジック本体はヘルパー／Setup 側テストでカバーする）

## スキル知見の反映（`vrchat-avatar-color-variation-menu`）

- `transitionSeconds = 0`（色替えは即時切替）
- `useParentMenu = true`、`saved = true`、`synced = true`、`chooseDefaultValue = 0`
- 選択肢0 = 現在のマテリアルをベースラインにする
- `meshPath` は avatar-root 相対・root プレフィクスなし（既存 `AvatarUtil.RelativePath` と一致）
- メニュー名は「色」推奨（GameObject 名で表現）、衣装メニュー末尾に置く運用
- `AvatarChooseMenuCreator` は `AvatarToggleMenuCreator` と同一 GameObject 不可

## 非スコープ（将来構想）

- カラバリ prefab 群から実マテリアルを流し込んで選択肢を埋める（スキルの `BuildChooseMaterialsFromPrefabs` 相当）。本機能は雛形のみ
- 選択肢名・アイコンの自動命名、`UseCompressed` の自動設定
- 既存 Choose Menu の設定済み表示（メッシュ行バッジ）
- `siblingIndex` によるメニュー表示位置の制御（初版は末尾自動）
