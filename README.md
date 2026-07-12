# recognizer

YOLO 形式の ONNX モデルで動作する顔検出・顔認証・物体検出のコアライブラリ(C# / .NET 10)。

## 機能

- 顔検出: YOLO-face 系モデル(YOLOv8n-face / YOLOv11-face 等)による顔のバウンディングボックスとランドマークの検出
- 顔認証: 顔埋め込みモデル(ArcFace 等)による 1:1 照合と埋め込み抽出
- 物体検出: YOLOv5/v8/v11 系モデルによる物体検出

モデルの入出力形式(テンソルレイアウト・出力形状)は ONNX メタデータと出力形状から自動判別する。

## API 仕様

`docs/api-spec.md` を参照。

## リポジトリ構成

- `src/Recognizer` — 公開 API を提供する net10.0 クラスライブラリ(内部実装は `Internal/` 配下で `internal`)
- `tests/Recognizer.Tests` — xUnit テストプロジェクト
- `tests/Recognizer.Tests/Fixtures` — テスト用のダミー ONNX モデル(生成物をコミット。詳細は同ディレクトリの `README.md`)
- `tools/generate_test_models.py` — fixture 再生成スクリプト(再生成時のみ使用)
- `models/` — 実 ONNX モデルの置き場(`.gitignore` 済みで未追跡)

## 開発

devcontainer で開発する。ビルドとテストはいずれも終了コード 0 で完了する。

```bash
# ビルド
dotnet build

# テスト
dotnet test
```

テストは `Fixtures/` にコミット済みのダミー ONNX モデルで自己完結するため、実行に Python は不要である。

### テスト用 fixture の再生成

`Fixtures/*.onnx` を作り直す場合のみ、Python(onnx パッケージ)で再生成する。生成物はコミットされているため、通常の開発では不要である。

```bash
python3 -m venv /tmp/onnx-venv   # venv の場所は任意
/tmp/onnx-venv/bin/pip install onnx
/tmp/onnx-venv/bin/python tools/generate_test_models.py
```

生成される fixture の一覧・期待値・生成ロジックは `tests/Recognizer.Tests/Fixtures/README.md` と `tools/generate_test_models.py` を参照。

## モデルファイル

ONNX モデルはリポジトリに含めない。`models/` に別途配置する。

## ライセンス

MIT
