# Recognizer 公開 API 仕様

本書は recognizer ライブラリの公開 API を定義する。実装(要件定義・設計・タスク分解)は本書を正とし、flow-sdd で進める。

## 1. 概要

YOLO 形式の ONNX モデルファイルで動作する、顔検出・顔認証・物体検出のクラスライブラリ。端末上(ローカル)で CPU 推論する。

`workspaces/face-recognition` の `src/Recognizer` の作り直しであり、旧実装の機能のうち以下をスコープ外とする。

- 顔の角度計算(roll / pitch / yaw)
- YOLOv3 形式(3 出力テンソル)のモデル対応
- モデルファイル名によるクラス名リストの自動選択(Open Images 等)
- `FaceDatabase` による 1:N 識別(埋め込み抽出 API の組み合わせで呼び出し側が実現できる)
- デバッグ用コンソール出力(ライブラリはコンソール出力しない)

## 2. 対象環境

| 項目 | 内容 |
| --- | --- |
| フレームワーク | .NET 10(`net10.0`)、C# クラスライブラリ |
| 推論 | Microsoft.ML.OnnxRuntime(CPU。実行プロバイダの追加はスコープ外) |
| 画像処理 | OpenCvSharp4 |
| 動作環境 | Windows / macOS / Linux(devcontainer は linux/amd64) |

## 3. 公開 API

名前空間は `Recognizer` とする。公開型は以下に列挙したものに限る。

### 3.1 共通仕様

- 画像入力は 3 形式のオーバーロードで受け付ける。
  - `OpenCvSharp.Mat`(BGR)
  - `string imagePath`(ファイルパス。画像フォーマットは OpenCV が自動判別)
  - `ReadOnlyMemory<byte>`(エンコード済み画像バイト列。フォーマットは OpenCV が自動判別)
- 座標系は入力画像のピクセル座標(左上原点)。`System.Drawing.RectangleF` / `System.Drawing.PointF` を使用する。
- すべての非同期メソッドは `CancellationToken`(省略可、既定 `default`)を受け取る。
- 推論セッションを保持する公開クラスは `IDisposable` を実装する。
- 同一インスタンスに対する `DetectAsync` 等の並行呼び出しを許可する(スレッドセーフ)。

### 3.2 モデル形式の自動判別

利用者はモデルの形式(バージョン・テンソルレイアウト)を指定しない。ライブラリが ONNX メタデータと出力形状から自動判別する。

- 入力テンソル: NCHW / NHWC、入力サイズをモデルメタデータから判別する。動的軸の場合は 640x640 を既定とする。
- 検出モデルの出力テンソル: 転置形式 `[1, F, N]` と標準形式 `[1, N, F]` を自動判別する。
  - 顔検出: `F = 5`(bbox + conf)および `F = 20`(bbox + conf + ランドマーク 5 点 x [x, y, conf])に対応する。ランドマークはモデルが出力する場合のみ結果に含める。
  - 物体検出: `F = 4 + クラス数`(YOLOv8/v11)および `F = 5 + クラス数`(YOLOv5)に対応する。
- 顔埋め込みモデル: 入力レイアウト・入力サイズをメタデータから判別し、出力の 1 次元ベクトルを埋め込みとする。
- 判別できない形式は `NotSupportedException` を送出する。判別ルールの詳細は設計フェーズで確定する。

### 3.3 FaceDetector(顔検出)

```csharp
public sealed class FaceDetector : IDisposable
{
    public FaceDetector(string modelPath, FaceDetectorOptions? options = null);

    public Task<IReadOnlyList<FaceDetection>> DetectAsync(Mat image, CancellationToken cancellationToken = default);
    public Task<IReadOnlyList<FaceDetection>> DetectAsync(string imagePath, CancellationToken cancellationToken = default);
    public Task<IReadOnlyList<FaceDetection>> DetectAsync(ReadOnlyMemory<byte> encodedImage, CancellationToken cancellationToken = default);
}

public sealed record FaceDetectorOptions
{
    public float ConfidenceThreshold { get; init; } = 0.7f;
    public float NmsThreshold { get; init; } = 0.5f;
}

public sealed record FaceDetection(RectangleF BBox, float Confidence, FaceLandmarks? Landmarks);

public sealed record FaceLandmarks(PointF LeftEye, PointF RightEye, PointF Nose, PointF LeftMouth, PointF RightMouth);
```

- 検出結果は信頼度降順で返す。検出なしは空リスト(例外にしない)。
- NMS 適用後の結果を返す。

### 3.4 FaceRecognizer(顔認証)

```csharp
public sealed class FaceRecognizer : IDisposable
{
    public FaceRecognizer(string detectorModelPath, string embeddingModelPath, FaceRecognizerOptions? options = null);

    // 1:1 照合(各画像で最高信頼度の顔を使用)
    public Task<FaceVerificationResult> VerifyAsync(Mat image1, Mat image2, CancellationToken cancellationToken = default);
    public Task<FaceVerificationResult> VerifyAsync(string imagePath1, string imagePath2, CancellationToken cancellationToken = default);
    public Task<FaceVerificationResult> VerifyAsync(ReadOnlyMemory<byte> encodedImage1, ReadOnlyMemory<byte> encodedImage2, CancellationToken cancellationToken = default);

    // 埋め込み抽出(faceRegion 省略時は顔検出して最高信頼度の顔を使用)
    public Task<FaceEmbeddingResult> ExtractEmbeddingAsync(Mat image, RectangleF? faceRegion = null, CancellationToken cancellationToken = default);
    public Task<FaceEmbeddingResult> ExtractEmbeddingAsync(string imagePath, RectangleF? faceRegion = null, CancellationToken cancellationToken = default);
    public Task<FaceEmbeddingResult> ExtractEmbeddingAsync(ReadOnlyMemory<byte> encodedImage, RectangleF? faceRegion = null, CancellationToken cancellationToken = default);

    // コサイン類似度 [-1, 1]
    public static float CompareEmbeddings(ReadOnlySpan<float> embedding1, ReadOnlySpan<float> embedding2);
}

public sealed record FaceRecognizerOptions
{
    public float DetectionThreshold { get; init; } = 0.7f;
    public float RecognitionThreshold { get; init; } = 0.6f;
    public float NmsThreshold { get; init; } = 0.5f;
}

public enum FaceVerificationStatus
{
    Match,          // 同一人物と判定
    NoMatch,        // 別人と判定
    NoFaceInImage1, // 画像 1 で顔未検出
    NoFaceInImage2  // 画像 2 で顔未検出
}

public sealed record FaceVerificationResult(
    FaceVerificationStatus Status,
    float Similarity,          // 顔未検出時は 0
    FaceDetection? Face1,      // 画像 1 で使用した顔(未検出時は null)
    FaceDetection? Face2)      // 画像 2 で使用した顔(未検出時は null)
{
    public bool IsMatch => Status == FaceVerificationStatus.Match;
}

public sealed record FaceEmbeddingResult(
    float[]? Embedding,        // 顔未検出時は null
    FaceDetection? Face);      // 使用した顔(faceRegion 指定時・未検出時は null)
```

- 顔未検出は予期されるエラーであり、例外ではなく結果型(`Status` / `null`)で表現する。
- 埋め込み抽出時の顔領域切り出しは、周辺情報を含む正方形(パディング比率 0.2)で行う(旧実装と同等)。
- `CompareEmbeddings` は次元不一致のとき `ArgumentException` を送出する。

### 3.5 ObjectDetector(物体検出)

```csharp
public sealed class ObjectDetector : IDisposable
{
    public ObjectDetector(string modelPath, ObjectDetectorOptions? options = null);

    public Task<IReadOnlyList<ObjectDetection>> DetectAsync(Mat image, CancellationToken cancellationToken = default);
    public Task<IReadOnlyList<ObjectDetection>> DetectAsync(string imagePath, CancellationToken cancellationToken = default);
    public Task<IReadOnlyList<ObjectDetection>> DetectAsync(ReadOnlyMemory<byte> encodedImage, CancellationToken cancellationToken = default);
}

public sealed record ObjectDetectorOptions
{
    public float ConfidenceThreshold { get; init; } = 0.5f;
    public float NmsThreshold { get; init; } = 0.5f;

    // 省略時: クラス数がモデル出力から 80 と判別できれば COCO 80 クラス名、
    // それ以外は "class_{id}" を返す
    public IReadOnlyList<string>? ClassNames { get; init; }
}

public sealed record ObjectDetection(int ClassId, string ClassName, float Confidence, RectangleF BBox);
```

- 検出結果は信頼度降順で返す。検出なしは空リスト。
- クラス単位で NMS を適用する。

### 3.6 エラー処理の方針

| 状況 | 表現 |
| --- | --- |
| 顔未検出・物体検出 0 件 | 結果型(`Status` / `null` / 空リスト) |
| モデルファイルが存在しない・ロード失敗 | 例外(OnnxRuntime の例外をそのまま、またはファイル存在チェックの `FileNotFoundException`) |
| 画像のロード失敗(パス不正・デコード不可) | `ArgumentException` |
| 非対応のモデル形式 | `NotSupportedException` |
| 引数不正(空の `Mat`、次元不一致等) | `ArgumentException` |

## 4. リポジトリ構成

```
src/Recognizer/          クラスライブラリ本体
tests/Recognizer.Tests/  xUnit テスト
models/                  ONNX モデル置き場(gitignore 済み、実モデルはコミットしない)
docs/                    本仕様書・SDD 成果物
```

- テストで実モデルが必要な場合の扱い(小型ダミー ONNX の生成・実モデルのダウンロード手順等)は設計フェーズで確定する。

## 5. 非機能要件

- ライブラリはコンソール出力・ログ出力をしない(ロギング機構の導入はスコープ外)。
- 内部実装(前処理・テンソル変換・NMS・出力パース)は公開しない(`internal`)。
- 依存パッケージ: Microsoft.ML.OnnxRuntime、OpenCvSharp4(+ 各 OS 向け runtime パッケージ)。旧実装にあった Microsoft.ML、SixLabors.ImageSharp、System.Numerics.Tensors への依存は持たない。
