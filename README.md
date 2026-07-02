# Costume Dashboard

アバター内の衣装prefabのマテリアル状況を一覧し、透過（着脱フェード）まわりの設定を一括で行う Unity エディタ拡張。

## 機能

- 衣装prefab（複数登録可）配下の全マテリアルスロットを shader family/variant 別に一覧
- lilToon の 2nd / 3rd / AlphaMask 枠の使用状況とフェード駆動枠の推奨を表示
- 同一シェーダーのスロット群に対する AO Material Editor 設定 GameObject の一括作成（要 aoyon.material-editor）
- メッシュの Select ボタン（SceneView ハイライト）
- チェックしたメッシュ群への Avatar Toggle Menu Creator 一括設定（プリセット透過フェード付き）
- Render Queue 一覧と Change Render Queue コンポーネント設定

## 使い方

`Tools/Costume Dashboard` を開き、衣装prefabルートを登録する。アバタールートは親方向に自動検出される。

## 依存

- Avatar Menu Creator for Modular Avatar
- Change Render Queue
- AO Material Editor（任意。未導入時は AO ME 機能が無効化される）

## License

[Zlib License](LICENSE.txt)
