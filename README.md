# Costume Dashboard

アバター内の衣装prefabのマテリアル状況を一覧し、透過（着脱フェード）まわりの設定を一括で行う Unity エディタ拡張。

## Install

### VCC用インストーラーunitypackageによる方法（おすすめ）

https://github.com/Narazaka/net.narazaka.vrchat.costume-dashboard/releases/latest から `net.narazaka.vrchat.costume-dashboard-installer.zip` をダウンロードして解凍し、対象のプロジェクトにインポートする。

### VCCによる方法

1. https://vpm.narazaka.net/ から「Add to VCC」ボタンを押してリポジトリをVCCにインストールします。
2. VCCでSettings→Packages→Installed Repositoriesの一覧中で「Narazaka VPM Listing」にチェックが付いていることを確認します。
3. アバタープロジェクトの「Manage Project」から「Costume Dashboard」をインストールします。

## 機能

- 衣装prefab（複数登録可）配下のマテリアルスロットをメッシュ（レンダラー）行として一覧表示（shader family/variant 別）。EditorOnly タグ（自身または親）のメッシュはビルド時に除去されるため一覧・操作の対象外。シェーダー種別・AO ME グループは日本語表示名（例: 「不透明 Outline Tess」「半透明 2パス → Alpha (マスク乗算化)」、AO ME ホスト GameObject 名との対応は tooltip）で表示し、メッシュ行/グループ行は背景色 tint で区別。操作系の列（Select / ✓ / 操作 / 枠 / BS）はオブジェクト列の直後に配置
- lilToon フェード駆動枠の選択肢：main（メインカラー `_Color`、`#FFFFFFFF` のときのみ利用可）/ AlphaMask / 3rd / 2nd。推奨優先度は main > AlphaMask > 3rd > 2nd
- フェード枠はメッシュ（レンダラー）単位で1つに統一：マテリアルプロパティアニメーションはレンダラー単位でしかスロットを選べないため、推奨枠はメッシュ内の全マテリアルスロットが共通で利用可能（△含む）な最初の枠。メッシュ行に共通推奨、main/AM/3rd/2nd 各列にはスロット状態を集約した ○/△/× を表示（内訳は tooltip）
- フェード枠判定の緩和：ゲート無効・不透明シェーダーで実質未使用の残存値は △（警告付き利用可）と表示
- BlendShape 一覧表示と Modular Avatar BlendshapeSync の一括付与（素体 = アバター直下で最多シェイプのメッシュ、変更可。同名シェイプを一括バインド）
- 各フェード枠が利用不可の場合は理由を要約表示
- メッシュ単位の Select ボタン（SceneView ハイライト）
- チェックしたメッシュ群への Avatar Toggle Menu Creator 一括設定（プリセット透過フェード付き）
- 色変えメニュー雛形作成：衣装の全マテリアルスロット（配下にチェックがあればそのメッシュのみ）を対象に、アバタールート直下へ Avatar Choose Menu Creator を新規作成。各スロットの選択肢0＝現在マテリアルを入れた雛形（残りの色はユーザーが記入）。ツールバーの一括作成と衣装行ボタンの2導線
- メッシュ単位の Render Queue 一括設定（Change Render Queue コンポーネント）
- 同一シェーダーのスロット群に対する AO Material Editor 設定 GameObject の一括作成（要 aoyon.material-editor）
- 設定済み表示の強化：スロット行に所属グループの AO ME 対象（グループ表示名）を表示し、AO ME / BlendShapeSync / Toggle Menu / Render Queue が設定済みのボタンは緑色でハイライト。メッシュ単位でも既存 Toggle Menu の対象になっているかを Toggle✓ 表示（対象メニュー名は tooltip）で、Change Render Queue が付いているかを Q✓ 表示（メッシュ行 = コンポーネント有無、スロット行 = 実効設定の有無）で確認できる
- ビューモード：メッシュビュー（既定。メッシュ中心に Toggle/Queue/BlendShape 操作を集約、衣装行から AO ME 一括作成）と AO ME ビュー（グループ単位の個別設定）
- AlphaMask 干渉の自動調整：色フェード作成時、AlphaMask が置き換えモードなら乗算へ変換。不透明シェーダーの残存設定は無効化（override として AO ME に付与）
- 素体自動判定の除外：非アクティブ / EditorOnly / Avatar Descriptor の Face Mesh は素体候補から除外

## 使い方

`Tools/Costume Dashboard` を開き、衣装prefabルートを登録する。アバタールートは親方向に自動検出される。

## 依存

- Modular Avatar（BlendshapeSync 付与で直接利用）
- Avatar Menu Creator for Modular Avatar
- Change Render Queue
- AO Material Editor（任意。未導入時は AO ME 機能が無効化される）

## License

[Zlib License](LICENSE.txt)
