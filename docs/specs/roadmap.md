# Recognizer 実装 roadmap

`docs/api-spec.md` に定義された公開 API を 3 つの作業単位(unit)に分解して実装する。
本ファイルは中間生成物であり、全 unit 完了で役目を終える。

## 経路判定

- **経路 D**(複数 unit へ分解)
- 根拠: 仕様は公開クラス 3 つ + 共通基盤(モデル形式自動判別・前処理・NMS)にまたがり、1 unit の規模目安(要件 10 以下・メインタスク 8 以下)を超えるため。

## unit 一覧

| # | unit | 内容 | 依存 | 状態 |
| --- | --- | --- | --- | --- |
| 1 | `face-detection` | プロジェクト骨格(src/Recognizer、tests/Recognizer.Tests)、共通基盤(3 形式の画像入力、ONNX モデル形式自動判別、前処理、NMS、テスト用ダミー ONNX 方針)、`FaceDetector` / `FaceDetection` / `FaceLandmarks`(api-spec 3.2, 3.3) | なし | 完了(2026-07-12、最終検証 GO) |
| 2 | `object-detection` | `ObjectDetector` / `ObjectDetection`、クラス単位 NMS、COCO 80 クラス名の既定解決、YOLOv5/v8/v11 出力パース(api-spec 3.5) | 1(共通基盤を再利用) | 完了(2026-07-12、最終検証 GO) |
| 3 | `face-recognition` | `FaceRecognizer` / `FaceComparisonResult` / `FaceEmbeddingResult` / `FaceComparisonStatus`、顔埋め込みモデル対応、顔領域切り出し(パディング比率 0.2)、コサイン類似度(api-spec 3.4) | 1(`FaceDetector` と共通基盤を利用) | 未着手 |

## 順序

1 → 2 → 3 の順で進める。unit 2 と 3 は互いに独立(どちらも unit 1 のみに依存)だが、レビュー負荷を一定に保つため直列で進める。

## 備考

- 共通基盤は unit 1 で `internal` として実装し、unit 2・3 はそれを拡張・再利用する(過度な事前抽象化はしない。YAGNI)。
- 各 unit は `docs/specs/<unit>/` を workdir とし、要件 → 設計 → タスク分解 → 実装を承認ゲート付きで進める。
- 各 unit の開始時に作業ブランチ `<unit>` を作成する。main への統合(PR・マージ)は人間に委ねる。
