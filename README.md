# Costume Dashboard

アバター内の衣装prefabのマテリアル状況を一覧し、透過（着脱フェード）まわりの設定を一括で行う Unity エディタ拡張。

## 機能

- 衣装prefab（複数登録可）配下の全マテリアルスロットをメッシュ（レンダラー）行として一覧表示（shader family/variant 別）
- lilToon フェード駆動枠の選択肢：main（メインカラー `_Color`、`#FFFFFFFF` のときのみ利用可）/ AlphaMask / 3rd / 2nd。推奨優先度は main > AlphaMask > 3rd > 2nd
- フェード枠判定の緩和：ゲート無効・不透明シェーダーで実質未使用の残存値は △（警告付き利用可）と表示
- BlendShape 一覧表示と Modular Avatar BlendshapeSync の一括付与（素体 = アバター直下で最多シェイプのメッシュ、変更可。同名シェイプを一括バインド）
- 各フェード枠が利用不可の場合は理由を要約表示
- メッシュ単位の Select ボタン（SceneView ハイライト）
- チェックしたメッシュ群への Avatar Toggle Menu Creator 一括設定（プリセット透過フェード付き）
- メッシュ単位の Render Queue 一括設定（Change Render Queue コンポーネント）
- 同一シェーダーのスロット群に対する AO Material Editor 設定 GameObject の一括作成（要 aoyon.material-editor）
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
