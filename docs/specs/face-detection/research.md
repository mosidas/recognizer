# face-detection — 調査ログ

design.md の判断根拠となる調査結果。新規プロジェクトのため既存コードの Gap 分析は対象外(既存資産なし)。

## 1. 依存パッケージのバージョン(2026-07-12 時点、NuGet API で確認)

| パッケージ | 最新安定版 | 用途 |
| --- | --- | --- |
| Microsoft.ML.OnnxRuntime | 1.27.1 | CPU 推論 |
| OpenCvSharp4 | 4.13.0.20260627 | 画像処理 |
| OpenCvSharp4.official.runtime.linux-x64 | 4.13.0.20260627 | devcontainer(linux/amd64)用ネイティブランタイム |
| xunit | 2.9.3 | テスト |
| Microsoft.NET.Test.Sdk | 18.7.0 | テストホスト |

- dotnet SDK: 10.0.301(コンテナにインストール済みを確認)。
- OpenCvSharp の Windows / macOS 向けランタイムは利用側が追加する(本リポジトリの開発・CI は linux-x64 のみ)。

## 2. OnnxRuntime のスレッドセーフ性(要件 4.3 の根拠)

- 同一 `InferenceSession` に対する複数スレッドからの `Run` 同時呼び出しは安全(CPU EP)。根拠:
  - 公式リポジトリ Issue(メンテナ回答): https://github.com/microsoft/onnxruntime/issues/114
  - 公式ディスカッション(Java/C API の説明。セッションは構築後スレッドセーフ): https://github.com/microsoft/onnxruntime/discussions/10107
  - 例外は DirectML EP(同時 Run 不可): https://github.com/microsoft/onnxruntime/discussions/9441 — 本ライブラリは CPU EP のみのため非該当。
- onnxruntime.ai のドキュメントサイトでは明文の記載ページを確認できなかった(threading ページはスレッド数設定のみ)。上記は公式リポジトリの一次情報で代替する。

## 3. テスト用 ONNX モデルの供給方式(api-spec 4. で設計確定とされた項目)

環境検証の結果:

- コンテナの Python 3.14.4 に `onnx` パッケージは無く、pip 直インストールは PEP 668(externally-managed)で不可。
- `python3 -m venv` + `pip install onnx` は成功(onnx 1.22.0)。ネットワーク到達性あり。

| 案 | 内容 | トレードオフ |
| --- | --- | --- |
| A(採用) | Python スクリプトで小型ダミー ONNX を生成し、生成物(数 KB)を `tests/Recognizer.Tests/Fixtures/` にコミットする | `dotnet test` が Python 非依存で自己完結・決定論的。リポジトリにバイナリが入るが数 KB × 6 個程度 |
| B | テスト実行時に毎回生成 | リポジトリにバイナリを入れないが、全テスト環境に Python + venv + onnx が必要になり、`dotnet test` が自己完結しない |
| C | 実モデル(YOLO 顔検出)をダウンロードして使用 | 実挙動に忠実だが、ネットワーク依存・ライセンス管理・数十 MB のダウンロードが必要で単体テストに不向き |

- 案 A を採用。`.gitignore` の除外対象は `models/*.onnx`(実モデル置き場)であり、テスト fixture は対象外。この解釈は design 承認ゲートで人間が確認する。
- ダミーモデルは「入力に依存しない定数出力」を返す最小グラフとして生成する(出力値をテストコードから予測可能にするため)。

## 4. モデル形式自動判別の背景(要件 2.1〜2.3)

- YOLOv8/v11 系の ONNX エクスポートの出力は転置形式 `[1, F, N]`(例: 640x640 入力で N = 8400)が主流。YOLOv5 系は標準形式 `[1, N, F]`。顔検出モデル(YOLOv8-face 系)のランドマーク付き出力は `F = 20`(bbox 4 + conf 1 + ランドマーク 5 点 × [x, y, conf])。
- bbox はモデル出力座標系で中心形式(cx, cy, w, h)が YOLO 系の慣習。
- 判別が曖昧になるのは N と F の両方が {5, 20} に一致する病的な形状のみ(実モデルでは N が数千)。この場合は転置形式を優先する(主流形式のため)。
