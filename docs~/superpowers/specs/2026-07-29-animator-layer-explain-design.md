# Animator Layer 説明ビュー 設計

- 日付: 2026-07-29
- 対象パッケージ: `net.narazaka.vrchat.costume-dashboard`

## 目的

衣装導入時に、衣装prefabやアバターが持つ Animator layer が「何で動き（トリガー）・何を動かし（対象）・どう振る舞うか（構造パターン）」を、改変ユーザーが Costume Dashboard 上で読んで理解できるようにする。干渉調査・重複トグル防止が主用途。

- 単純な構造（boolトグル各形 / int排他選択 / float連続制御）は機械的に分類して日本語要約を生成
- 機械分類に収まらない複雑な layer は、Animator を構造化JSONに変換して LLM API に渡し、説明を生成する

## 決定事項

- 利用者: 改変ユーザー自身（agent向け情報提供は主目的でない）
- 解析対象ソース: 登録衣装ルート配下の MA Merge Animator（既定）+ アバターの Descriptor playable layers 等（オプショナル）
- 複雑layerの説明: LLM API を直接呼ぶ（OpenAI互換 + Anthropic の2形式対応）
- LLM実行: 一括生成ボタン（確認ダイアログ付き）+ 行ごとの手動生成ボタン。**未生成の複雑layer全部を1リクエストにまとめて渡す**（複数layer連携のコンテキスト保持のため）
- UI: 既存 CostumeDashboardWindow に「Animator」ビューを追加。ビュー切替は既存Popupを廃止して**ボタングループ**（メッシュ / AO ME / Animator）に変更
- 衣装連携: 各layerの作用先パスと登録衣装ルートを突合して表示・フィルタ
- アーキテクチャ: C#構造モデル（**任意のグラフを扱える平坦なノード+エッジ表現**）を中心に、機械分類はモデル直接、LLMへは構造化JSONシリアライズを渡す。「これだけ読めば Animator の直接読解が不要」な自己完結表現を目指す

## 全体構成

```
Editor/
  Core/AnimatorAnalysis/   … 純ロジック（UI非依存・書き込みなし）
    AnimatorSourceCollector  アバタールート → 解析対象ソース列挙
    LayerModelBuilder        AnimatorController → layer構造モデル（グラフ）
    LayerPatternClassifier   モデル → 分類 + 機械的日本語要約
    LayerModelSerializer     モデル → 構造化JSON（LLM入力・クリップボードコピー共用）
  Core/Llm/
    LlmClient                OpenAI互換 / Anthropic 対応の最小HTTPクライアント
    LayerExplainer           プロンプト構築・キャッシュ・一括実行
  UI/
    AnimatorLayersView       Animatorビュー本体（ウィンドウ本体とは別ファイル）
```

- 読み取り専用機能（Undo不要）。既存Setup系・メッシュ/AO MEビューの中身とは独立
- データフロー: Refresh時に登録衣装からアバタールート解決（既存 `AvatarUtil` 流用）→ ソース列挙 → layerモデル化 → 分類・要約 → バインディングパスと登録衣装ルートを突合して「作用先衣装」判定

### 解析対象ソース

- **既定: 登録衣装ルート配下の MA Merge Animator のみ**（参照Controller + pathMode / 相対ルート / layerType 込み）。衣装が持ち込むAnimatorの把握が主用途
- **[アバターも解析] toggle（ウィンドウ状態）**: ONで以下を追加
  - `VRCAvatarDescriptor` の playable layers（Controller設定済みの全枠。FX中心）
  - 登録衣装外（アバター本体・ギミック等）の MA Merge Animator
  - アバター新規設定時の確認用途を想定

## 構造モデル仕様

layer単位の自己完結モデル。**状態遷移は樹状ではなくノード（状態）+ エッジ（遷移）の平坦なグラフ表現**とし、循環（ON⇔OFF相互遷移）・自己遷移・Entry/AnyState/Exit・sub-state machine 境界を越える遷移をすべて表現できる。

- **ソース情報**: playable種別（FX等）、または Merge Animator のコンポーネントパス・layerType
- **layer情報**: 名前・index・weight・AvatarMask・additiveか
- **パラメーター**: layerが参照する全パラメーター（名前・型）。Expression Parameters 登録有無（synced/saved/既定値）を突合して付記
- **状態（ノード）**: 一意ID・名前・所属スコープ（sub-state machine を `sm/子sm` のパス的表記で持ち、グラフ自体は平坦に保つ）・初期状態か・Motion内容・Motion Timeパラメーター・Write Defaults・VRC StateMachineBehaviour（Parameter Driver の set/add/random 内容、Tracking Control 等は種別と要点）
  - Motion内容: クリップ名、または BlendTree 構造（種別・駆動パラメーター・子としきい値）。BlendTree 内部のみ木として入れ子表現（実際に木のため）
- **遷移（エッジ）**: fromID/toID（Entry・AnyState・Exit は擬似ノード）・条件（パラメーター×比較×しきい値）・Has Exit Time・duration
- **バインディング**: クリップ/BlendTree配下の全 `EditorCurveBinding` を「アバタールート絶対パス × 型 × プロパティ × 値要約（定数 / 開始→終了 / min-max）」に展開。Merge Animator 相対パスはルート解決済み。Humanoidマッスルカーブはパス無し「Humanoid: プロパティ名」
- **作用先衣装**: 解決済みパスと登録衣装ルートの突合結果

### JSON

- 1layer = 1オブジェクト。Newtonsoft.Json（VRC SDK同梱）でシリアライズ
- LLMプロンプト・クリップボードコピー・キャッシュキーに共用

## 機械分類仕様

分類とは独立に、トリガーパラメーター一覧・作用先一覧は複雑layerでもモデルから機械的に抽出して表示する。分類器はグラフ上のパターン判定として実装する。

| 分類 | 判定条件 | 要約例 |
|---|---|---|
| boolトグル | bool 1個の条件で結ばれた2ノード循環（相互遷移 / Exit経由Entry再評価 / AnyState経由の各形） | 「`X` で `Sailor_Top` ほか2件をON/OFF」 |
| int排他選択 | int 1個のEquals（+NotEqual/Exit再評価形）で状態が排他切替 | 「`X`=0: ◯◯ / =1: △△」 |
| float連続 | 単一状態のMotion Time、または float 1個の1D BlendTree | 「`X` の値で ◯◯ を連続変化」 |
| 複雑 | 上記以外（Direct BlendTree・複数パラメーター混在・sub-state machine・Parameter Driver絡みの多段等） | LLM生成対象 |
| 空 | バインディング無し / weight 0 | グレー表示 |

- 要約の作用先内訳は GameObject ON/OFF・BlendShape・マテリアルプロパティ・Transform・その他 に分けて表現

## LLM連携仕様

- **設定**: プロバイダー（OpenAI互換 / Anthropic）・エンドポイントURL・モデル名・APIキー。EditorPrefs保存（リポジトリに入らない・マシン内共有）。設定UIはAnimatorビューの歯車ボタンから
- **実行**: 未生成の複雑layer全部のJSONを**1リクエストにまとめて**渡す（Parameter Driver 連鎖・同一パラメーター共有など layer 間の関係を説明に活かす）。出力は「layer ID → 説明」のJSONを返すようプロンプトで指示し、応答をパースして各行に割り付ける
  - ツールバー [未生成を一括生成]: 対象件数・リクエスト数・概算入力サイズの確認ダイアログ後に実行（進捗表示・中断可）
  - 行ごとの [生成] ボタン併設（強制再生成兼用）
  - 入力サイズが設定上限を超える場合のみ layer 境界で分割して複数リクエスト
- **プロンプト**: system で「VRChatアバターのAnimator layerのJSON表現を読み、改変ユーザー向けに日本語で ①何で動くか ②何を動かすか ③振る舞い を数行で説明」と指示、user にJSON群
- **キャッシュ**: layer単位。key = SHA1(自layerのJSON + プロンプトバージョン + モデル名)。`UserSettings/CostumeDashboardLlmCache.json` に保存（プロジェクトごと・通常VCS外）。内容不変なら再呼出ししない
- **エラー**: キー未設定・HTTPエラー・応答パース失敗は該当行とHelpBoxに表示（例外を出さない）

## UI仕様

- ビュー切替: 既存の表示Popupを廃止し、ツールバーの**ボタングループ（メッシュ / AO ME / Animator）**に変更（1クリック切替。既存2ビューの中身は無変更）。選択状態はウィンドウ状態として保持
- Animatorビュー構成: ツールバー行（[アバターも解析] toggle / 「登録衣装に作用するもののみ」フィルタtoggle / [未生成を一括生成] / 歯車）+ `MultiColumnTreeView` + 下部詳細ペイン
- ツリー: ソース行（`FX` / `Merge Animator (オブジェクトパス)`）→ layer行
- 列: layer名 / 分類 / トリガー（パラメーター名。Expression Menu の該当コントロール名は tooltip にベストエフォート併記）/ 作用先（先頭+件数、内訳tooltip）/ 衣装（作用する登録衣装名）/ 説明（機械要約またはLLM生成の1行目）/ 操作（[生成] [JSONコピー]）
- 詳細ペイン: 選択layerの説明全文・作用先全一覧・遷移条件の要点
- 「登録衣装に作用するもののみ」フィルタはアバター解析ON時に特に有効（衣装のみ解析時もパス突合表示自体は行う）

## エラー処理

- アバタールート未解決: HelpBoxで理由表示
- Descriptor の Controller 未設定枠: スキップ
- Missing参照（Controller/クリップ）: 行に警告表示して解析対象外

## テスト

既存Test構成に倣うEditModeテスト:

- 各パターンのAnimatorControllerをコード生成 → 分類・機械要約・トリガー/作用先抽出を検証（循環・AnyState・Exit再評価形・sub-state machine を含む）
- Merge Animator 相対パス解決（pathMode両方）の検証
- シリアライザのJSON構造検証
- LLMはリクエストボディ/ヘッダー構築（OpenAI互換・Anthropic両形式）のユニットテストのみ。実API呼出しテストはしない

## 非スコープ（将来）

- Expression Menu 完全解決（MA Menu Installer の合成結果を辿る本格的なメニュー項目逆引き）
- NDMFビルド後の合成FX解析
- 説明結果のエクスポート（Markdown等）
