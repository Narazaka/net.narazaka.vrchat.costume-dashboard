# Costume Dashboard

アバター内の衣装prefabのマテリアル状況を一覧し、透過（着脱フェード）まわりの設定を一括で行う Unity エディタ拡張。

## 機能

- 衣装prefab（複数登録可）配下の全マテリアルスロットをメッシュ（レンダラー）行として一覧表示（shader family/variant 別）
- lilToon フェード駆動枠の選択肢：main（メインカラー `_Color`、`#FFFFFFFF` のときのみ利用可）/ AlphaMask / 3rd / 2nd。推奨優先度は main > AlphaMask > 3rd > 2nd
- 各フェード枠が利用不可の場合は理由を要約表示
- メッシュ単位の Select ボタン（SceneView ハイライト）
- チェックしたメッシュ群への Avatar Toggle Menu Creator 一括設定（プリセット透過フェード付き）
- メッシュ単位の Render Queue 一括設定（Change Render Queue コンポーネント）
- 同一シェーダーのスロット群に対する AO Material Editor 設定 GameObject の一括作成（要 aoyon.material-editor）

## 使い方

`Tools/Costume Dashboard` を開き、衣装prefabルートを登録する。アバタールートは親方向に自動検出される。

## 依存

- Avatar Menu Creator for Modular Avatar
- Change Render Queue
- AO Material Editor（任意。未導入時は AO ME 機能が無効化される）

## License

[Zlib License](LICENSE.txt)
