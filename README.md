# recognizer

YOLO 形式の ONNX モデルで動作する顔検出・顔認証・物体検出のコアライブラリ(C# / .NET 10)。

## 機能

- 顔検出: YOLO-face 系モデル(YOLOv8n-face / YOLOv11-face 等)による顔のバウンディングボックスとランドマークの検出
- 顔認証: 顔埋め込みモデル(ArcFace 等)による 1:1 照合と埋め込み抽出
- 物体検出: YOLOv5/v8/v11 系モデルによる物体検出

モデルの入出力形式(テンソルレイアウト・出力形状)は ONNX メタデータと出力形状から自動判別する。

## API 仕様

`docs/api-spec.md` を参照。

## 開発

devcontainer で開発する。

```bash
# ビルド
dotnet build

# テスト
dotnet test
```

## モデルファイル

ONNX モデルはリポジトリに含めない。`models/` に別途配置する。

## ライセンス

MIT
