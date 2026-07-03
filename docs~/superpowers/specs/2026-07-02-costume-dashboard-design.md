# Costume Dashboard 設計

- 日付: 2026-07-02
- パッケージ名: `net.narazaka.vrchat.costume-dashboard`
- 開発場所: `vrchat-AVATAR-SANDBOX/Packages/net.narazaka.vrchat.costume-dashboard`（他の net.narazaka.* パッケージと同様、Unityプロジェクト内で個別gitリポジトリとして開発）
- Unity: 2022.3

## 目的

アバター改変（衣装導入）時のマテリアル関連作業を1つのEditorWindowに集約する。アバター内の複数衣装prefabを横断して:

1. 使用マテリアル・シェーダーの状況を一覧把握できる
2. 同一シェーダーのスロット群に対して AO Material Editor 設定GameObjectを一括作成できる
3. lilToonの 2nd / 3rd / AlphaMask の使用状況を一覧し、透過（着脱フェード）をどの駆動枠で実現すべきかがわかる
4. 各メッシュの Select ボタンで輪郭ハイライトを出し、Avatar Toggle Menu Creator でまとめるべきオブジェクトの目視確認ができる
5. 複数メッシュに対して Avatar Toggle Menu Creator をプリセット透過方法で一括設定できる
6. Render Queue を一覧し、Change Render Queue コンポーネントを設定できる

## 背景

同等のドメインロジックは agent 向けに実装済み（`claude-vrchat-avatar-skills` リポジトリ）:

- `GroupRendererSlotsByShader.exe`: shader family/variant 別スロットグルーピング
- `CheckMaterialFadeCompat.exe`: 2nd/3rd/AlphaMask のフェード駆動枠空き判定
- `SetupAOMaterialEditorCommand`: AO Material Editor（internal型）のリフレクション経由セットアップ
- `SetupAvatarToggleMenuCommand`: Avatar Toggle Menu Creator セットアップ
- 判定ルール・シェーダーGUIDマッピング表: `vrchat-avatar-ao-material-editor-transparency` スキル

本ツールはこれらの人間向けGUI版。ロジックは本パッケージのEditorアセンブリ内にC#で移植する（aibridge非依存・単体公開可能）。当面スキル側exeとロジックが二重になるが、将来的に agent-tools 側が本パッケージを参照する形へ逆転させて解消する構想（本スペックのスコープ外）。

## 決定事項

- 配置: 新規独立VPMパッケージ（案A）。名前 `net.narazaka.vrchat.costume-dashboard`
- UI: UIElements の `MultiColumnTreeView` ベースの EditorWindow
- 輪郭ハイライト: `Selection` 選択によるUnity標準のSceneView輪郭＋Hierarchy ping（カスタム描画なし）
- UX: まず動く画面を作り、その後実物を触りながら詰める（初版のレイアウトは暫定）

## 全体構成

Editor専用パッケージ。Runtimeコンポーネントは持たない（既存の ChangeRenderQueue / AvatarToggleMenuCreator / AO Material Editor コンポーネントを配置する側）。

```
Editor/
  Core/    … 純ロジック（UI非依存・書き込みなし）
    MaterialSlotScanner    アバター走査 → Renderer×スロット×Material×shader family/variant の一覧化
    FadeCompatChecker      Material直読みで 2nd/3rd/AlphaMask 枠の空き判定
    ShaderCatalog          lilToon std/tess/lite/multi/もっちり の family・variant 判定と不透明→透過版マッピング表
    TransparencyPresets    liltoon_transparent_3rd / _2nd / _alpha_mask のプロパティセット定義
  Setup/   … 書き込み操作（すべてUndo対応）
    AOMaterialEditorSetup  リフレクションで AO ME GameObject 作成・SlotTargets 割り当て
    ToggleMenuSetup        AvatarToggleMenuCreator 作成（公開APIを直接利用）
    RenderQueueSetup       ChangeRenderQueue コンポーネント付与・値編集
  UI/
    CostumeDashboardWindow EditorWindow 本体（MultiColumnTreeView）
```

依存:

- `nadena.dev.modular-avatar`（BlendshapeSync 付与で直接利用（asmdef 参照）。AvatarObjectReference / ModularAvatarBlendshapeSync）
- `net.narazaka.vrchat.avatar-menu-creater-for-ma`（ToggleMenuSetup が公開APIを利用）
- `net.narazaka.vrchat.change-render-queue`（RenderQueueSetup がコンポーネントを付与）
- `aoyon.material-editor` は**ソフト依存**: 型がinternalのためリフレクションでアクセスし、vpmDependencies に含めない。未導入時はAO ME関連ボタンを無効化して理由を表示

## 画面（初版・暫定）

対象の**衣装prefabルート（複数可）を明示指定**する（ObjectFieldリストへのドラッグ&ドロップ / Hierarchy選択からの追加ボタン）。アバター直下には様々なものが存在するため自動検出はせず、注目したい衣装だけを登録する方式。アバタールートは各衣装から**親方向に `VRCAvatarDescriptor` を走査して自動検出**する（シーン・Prefab Stage共通のロジック）。登録した衣装ごとに配下を走査して1つのツリー表を表示:

```
▼ シンシア_セーラー服 (衣装prefabルート)                        [Select]
  ▼ lilToon_std / cutout_o (5 slots)   [AO ME作成] [Toggle Menu作成] [Queue一括設定]
      Sailor_Top    slot0  Top.mat    3rd:空 2nd:空 AM:空 → 3rd推奨   Q:2450  [Select]
      Sailor_Skirt  slot0  Skirt.mat  3rd:使用済 2nd:空 AM:空 → 2nd推奨 Q:2450  [Select]
  ▼ lilToon_std / trans_o (2 slots)   …
```

- 行構成（UX改訂）: 衣装ルート > shader family/variant グループ > **メッシュ（レンダラー）** > スロット
- メッシュ行: Select ボタン / チェックボックス（Toggle Menu対象選択用）/ **[Toggle] ボタン（そのメッシュ単体で Toggle Menu 作成ダイアログを直接開く1クリック導線）** / **[Q] ボタン（全スロット一括の Render Queue 設定）** / **フェード枠セレクタ（推奨=自動 / main / alpha / 3rd / 2nd）**
- スロット行: マテリアル / shader variant / main・AM・3rd・2nd 使用状況 / 推奨枠 / Render Queue（スロット単位 [Q] も維持）。Select・チェックはメッシュ行に集約（スロットごとは冗長のため置かない）
- Select: メッシュ行の Renderer GameObject を `Selection` に設定（Ctrl+クリックで追加選択）
- 既存の AO ME / ChangeRenderQueue / AvatarToggleMenuCreator の設定済み状況も行に表示（重複作成の防止）
- 「衣装ルート」の単位: ユーザーが登録したGameObjectを1単位とする（素体を見たければ素体ルートを登録すればよい）
- 衣装ごとに親アバターを独立に解決する。アバタールートが見つからない衣装は一覧表示のみ可とし、アバタールート相対パスを要する操作（AO ME作成 / Toggle Menu作成）は無効化して理由を表示。複数衣装にまたがる一括操作（Toggle Menu作成）は同一アバター配下の衣装同士に限る
- 登録した衣装リストはウィンドウ状態として保持（ドメインリロードを跨いで維持）

## 機能仕様

### 1. マテリアル状況の一覧（MaterialSlotScanner + ShaderCatalog）

- 対象: アバター配下の全 `Renderer`（SkinnedMeshRenderer / MeshRenderer）。EditorOnly タグの扱いは含める（表示上マークする）
- 各マテリアルスロットについて: Material参照、shader、shader family（`lilToon_std` / `lilToon_tess` / `lilToon_lite` / `lilToon_multi` / `motchiri_std` / `motchiri_tess` / `unknown`）、variant（`opaque[_o]` / `cutout[_o]` / `trans[_o]` / `onetrans[_o]` / `twotrans[_o]`。multiは `_TransparentMode` 値で判定）を判定
- family/variant 判定はシェーダーGUIDベース（ShaderCatalog にマッピング表を保持。`vrchat-avatar-ao-material-editor-transparency` スキルの表を移植）
- 同一レンダラー内でスロットごとにfamilyが異なるケースを正しく分離する（グルーピングはスロット単位）

### 2. フェード駆動枠判定（FadeCompatChecker）

`CheckMaterialFadeCompat.exe` のロジックを移植・拡張。判定対象は Main / AlphaMask / 3rd Tex / 2nd Tex の4枠:

- **Main 枠（UX改訂で追加・今後の標準）**: `_Color` が `#FFFFFFFF`（RGBA=(1,1,1,1)）のときのみ「空」。それ以外（色変え済み・α使用済み）は「使用済」。駆動は `_Color` を `(1,1,1,0)`（OFF）↔`(1,1,1,1)`（ON）のベクトルフェード（駆動プロパティの有効化は不要）
- 3rd / 2nd / AlphaMask 枠: 各枠に属するプロパティ群がすべてシェーダーデフォルト値と一致すれば「空」
- **実質未使用の緩和判定（UX改訂3で追加）**: 差分があっても実際のレンダリングに使われていない場合は「利用可（警告付き）」とする:
  - 2nd/3rd: ゲート `_UseMainNndTex/_UseMainNrdTex` が 0（無効）なら、配下のプロパティに差分が残っていても利用可＋警告（プリセット適用で残存値が有効化される旨）
  - AlphaMask: `_AlphaMaskMode` が 0（無効）なら、`_AlphaMask`/`_AlphaMaskScale`/`_AlphaMaskValue` に差分があっても利用可＋警告
  - シェーダーが不透明（variant が opaque/cutout、multi の `_TransparentMode` 0/1）の場合、AlphaMask 系はモード有効でも出力に効かないため利用可＋警告
  - 警告付き利用可は表示上「△」（○=完全空き / △=警告付き利用可 / ×=使用済）とし、ツールチップに残存値の要約を出す。推奨枠決定では △ も利用可として扱う（優先度順は不変）
- ゲート有効（2nd/3rd の Use=1、AlphaMask の Mode≠0 かつ透過シェーダー）で差分あり → 「使用済」
- **利用不可理由の簡潔表示（UX改訂で追加）**: 使用済の枠には「なぜ不可か」を1行で要約した理由を表示する（例: main「_Color が #FF8899 (白のみ可)」、3rd「3rd Tex 使用中 (_UseMain3rdTex=1 他2件)」）。要約はテクスチャ割当・有効化フラグ・色変更など代表的な差分を優先して生成し、全差分プロパティ一覧はツールチップで補足

推奨プリセットは **Main > AlphaMask > 3rd > 2nd** の優先順位で最初に空いた枠（UX改訂で 3rd>2nd>AlphaMask から変更。母乳染み等のギミックが 2nd/3rd を選択的に使うため、それらを温存する並び）。全枠使用済みは警告表示。

**カスタム枠選択（UX改訂で追加）**: メッシュ（レンダラー）単位でフェード枠を推奨から手動で変更できる（既定は推奨=自動）。選択した実効枠は AO ME 作成のグループ分割・プリセット選択と Toggle Menu のフェード駆動の両方に反映される。

- 3rd/2nd 枠のプロパティ群: `_UseMainNrdTex`, `_ColorNrd`, `_MainNrdTex*`（Decal/Dissolve/DistanceFade 系含む約29項目）
- AlphaMask 枠: `_AlphaMaskMode`, `_AlphaMask`, `_AlphaMaskScale`, `_AlphaMaskValue`
- Materialアセットは書き換えない（判定のみ）

### 3. AO Material Editor 一括作成（AOMaterialEditorSetup）

グループ行（同一 family/variant）単位で実行。衣装ルート配下に `trans/<variant>` GameObject を作成し、AO ME コンポーネントを SlotTargets モード（対象スロット全列挙）で付与。設定内容は variant で分岐:

| variant | シェーダー変更 | propertyPreset |
|---|---|---|
| `opaque[_o]` / `cutout[_o]` | あり（マッピング表の透過版へ。アウトライン有無は維持） | 実効枠のプリセット（透過共通＋枠別駆動。Main は駆動プロパティ不要のため共通のみ） |
| `trans[_o]` | なし | 実効枠のプリセット |
| `onetrans[_o]` / `twotrans[_o]` | なし | ブレンド設定に触らず実効枠の**駆動プロパティのみ**（Main は空 = プロパティ変更なし、3rd なら `_UseMain3rdTex=1` 等） |
| multi（`_TransparentMode` 0/1/2） | なし | 実効枠のプリセット + `_TransparentMode=2` override |
| multi（`_TransparentMode` 3-6）/ `unknown` | — | 対象外（行に理由表示、ボタン無効） |

実効枠 = メッシュ行のカスタム選択（未選択なら推奨枠）。グループ分割キーの preset もこの実効枠を使う。

- `overrideRenderQueue` は常に false（Render Queue は ChangeRenderQueue に一元化）
- AO ME の型は internal のためリフレクションで生成・設定（`SetupAOMaterialEditorCommand` の実装を移植）
- 1グループ内でマテリアルごとに推奨枠が異なる場合（例: 一部だけ3rd使用済み）はグループを推奨枠別にさらに分割して別インスタンスにする

### 4. Toggle Menu 一括作成（ToggleMenuSetup）

チェック選択した複数メッシュに対して `AvatarToggleMenuCreator` GameObject を1つ作成:

- オブジェクトON/OFF（`ToggleObjects`）+ フェード用シェーダーパラメータ駆動を設定
- フェード駆動はプリセット4種（メッシュごとの実効枠 = カスタム選択またはスロット推奨に応じて使い分け）:
  - Main: `ToggleShaderVectorParameters` で `_Color` を `[1,1,1,0]`（OFF）↔ `[1,1,1,1]`（ON）（今後の標準）
  - 3rd: 同上 `_Color3rd`
  - 2nd: 同上 `_Color2nd`
  - AlphaMask: `ToggleShaderParameters` で `_AlphaMaskValue` を `-1`（OFF）↔ `0`（ON）
- 作成ダイアログでもメッシュごとの枠を変更可能（既定はメッシュ行セレクタの実効枠）
- 1クリック導線: メッシュ行の [Toggle] ボタンでそのメッシュ単体を対象にダイアログを直接開く（チェック→ツールバーの2段階を省略。ダイアログ自体は誤発防止のため維持）
- トランジション（フェード時間・オフセット）は `SetupAvatarToggleMenuCommand` が採る標準パターンに合わせる
- メニュー名・配置先GameObjectはダイアログで指定（既定: 衣装ルート配下）

### 4b. BlendShape 一覧と MA BlendshapeSync 付与（UX改訂3で追加）

- メッシュ行に BlendShape 数を表示し、ツールチップでシェイプ名一覧を確認できる
- **素体メッシュ**: アバターごとに1つ。既定はアバター直下（直接の子）の SkinnedMeshRenderer のうち BlendShape 数が最大のもの。ウィンドウ上の ObjectField で変更可能（セッション状態）
- メッシュ行の [BS Sync] ボタン: そのメッシュに `ModularAvatarBlendshapeSync` を付与/更新し、**素体と同名の BlendShape** を全てバインドする（`ReferenceMesh`=素体、`Blendshape`=名前、`LocalBlendshape`=""。既存バインドは同名エントリを更新、それ以外は追加 — `BulkSetupBlendShapeSync` と同じ update-or-add 意味論）
- ✓ チェック済みメッシュへの一括付与もツールバーから可能
- 無効条件（ボタン無効＋理由）: BlendShape なし / アバタールート不明 / 対象が素体自身 / 素体と同名シェイプが1つもない
- シェイプ単位の付け替え・別メッシュ参照などの細かい調整は既存の `BulkSetupBlendShapeSync`（GameObject メニュー）に任せ、本ツールは同名一括のみ扱う

### 5. Render Queue 一覧・設定（RenderQueueSetup）

- 一覧列に実効 Render Queue を表示: `ChangeRenderQueue` コンポーネントがあればその値、なければ Material の renderQueue 値（どちら由来かを区別表示）
- スロット単位に加え、**メッシュ（レンダラー）単位の一括設定**（UX改訂で追加）: メッシュ行の [Q] で全スロットに同一値を設定
- `ChangeRenderQueue` のスロット指定と `MaterialIndex=-1`（wildcard）の併存はビルド時に例外となるため、ツールは併存状態を作らない（wildcard がある状態でスロット指定するときは wildcard を各スロットへ展開してから設定）

## データフロー・エラー処理

- 走査対象: 登録された衣装ルート群（シーン内・Prefab Stage内いずれも可）。アバタールートは衣装から親方向に検出。走査は明示的な Refresh ボタン＋ウィンドウフォーカス時の自動更新（Undo/変更検知による完全リアクティブ化は初版では追わない）
- 登録衣装がシーンから消えた（削除・Stage閉鎖）場合はリストから薄表示にして操作対象外にする
- 書き込み操作はすべて `Undo.RegisterCreatedObjectUndo` / `Undo.RecordObject` 対応
- `aoyon.material-editor` 未導入・型名変更検出時: AO ME 関連UIを無効化し理由をHelpBoxで表示（例外を出さない）
- `unknown` family のスロット: 一覧には出すがフェード系操作は無効化
- Materialアセット・shaderアセットの欠損（null slot / missing shader）: 行に警告表示、操作対象から除外

## テスト

- Core（MaterialSlotScanner / FadeCompatChecker / ShaderCatalog / TransparencyPresets）は UI・書き込み非依存の純ロジックとして切り、Unity Test Runner（EditMode）でテスト用Material・テスト階層を生成して検証
- Setup 系は EditMode テストで実際にコンポーネントを生成し、シリアライズ結果（SlotTargets の中身、preset プロパティ値、ChangeRenderQueue 値）を検証
- AO ME リフレクション部は aoyon.material-editor 導入環境でのみ実行されるテストとしてマーク

## 非スコープ（将来構想）

- agent-tools / スキル側ロジックの本パッケージ参照への統合（二重管理解消）
- もっちりシェーダー干渉対策（`vrchat-avatar-ao-material-editor-motchiri` 相当）の自動化
- Quest対応・lilToon以外のシェーダーファミリーのフェード対応
- UXの本格調整（初版を触ってから詰める）
