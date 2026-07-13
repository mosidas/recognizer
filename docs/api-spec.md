# Recognizer 公開 API 仕様

本書は recognizer ライブラリの公開 API を定義する。実装(要件定義・設計・タスク分解)は本書を正とし、flow-sdd で進める。

## 1. 概要

YOLO 形式の ONNX モデルファイルで動作する、顔検出・顔認証・物体検出のクラスライブラリ。端末上(ローカル)で CPU 推論する。

以下をスコープ外とする。

- 顔の角度計算(roll / pitch / yaw)
- 同一人物か否かの閾値判定(ライブラリはコサイン類似度を返し、判定は呼び出し側の責務とする)
- YOLOv3 形式(3 出力テンソル)のモデル対応
- モデルファイル名によるクラス名リストの自動選択(Open Images 等)
- 1:N 識別(登録済み顔集合との照合。埋め込み抽出 API の組み合わせで呼び出し側が実現できる)
- デバッグ用コンソール出力(ライブラリはコンソール出力しない)

## 2. 対象環境

| 項目 | 内容 |
| --- | --- |
| フレームワーク | .NET 10(`net10.0`)、C# クラスライブラリ |
| 推論 | Microsoft.ML.OnnxRuntime(CPU。実行プロバイダの追加はスコープ外) |
| 画像処理 | OpenCvSharp4(+ RID 別ネイティブランタイムパッケージ 3 件。下表を参照) |
| 動作環境 | `linux-x64` / `win-x64` / `osx-arm64` の 3 RID(devcontainer は linux/amd64 = `linux-x64`) |

ライブラリ(`src/Recognizer`)の依存パッケージは以下の 5 件に限る。この制約はライブラリに掛かるものであり、CLI(`src/Recognizer.Cli`)には掛からない(下記「CLI の依存」を参照)。

| パッケージ ID | 供給元 | 対象 RID |
| --- | --- | --- |
| `Microsoft.ML.OnnxRuntime` | Microsoft(公式) | 全 RID(1 パッケージに同梱) |
| `OpenCvSharp4` | shimat(公式) | マネージド(RID 非依存) |
| `OpenCvSharp4.official.runtime.linux-x64` | shimat(公式) | linux-x64 |
| `OpenCvSharp4.runtime.win` | shimat(公式) | win-x64 |
| `Sdcb.OpenCvSharp4.mini.runtime.osx-arm64` | sdcb(サードパーティ) | osx-arm64 |

- Windows のパッケージ ID は Linux 版と非対称で、`official` も `-x64` も付かない。上表の綴りが正であり、対称性を求めて改名すると存在しない ID となり復元に失敗する。
- osx-arm64 のランタイムのみサードパーティ(sdcb)である。OpenCvSharp 公式が osx-arm64 の現行版(4.13 系)ランタイムを提供しておらず(公式 macOS パッケージは 4.6.0 / x64 で更新停止)、Apple Silicon 対応の現行選択肢がこれのみのため。ライセンスは本体と同じ Apache-2.0。
- 3 つのランタイムパッケージは無条件に参照する。RID 未指定のビルドではホストの RID で解決され、`dotnet publish -r <RID>` では**これら 3 パッケージのネイティブ資産**は対象 RID のもののみが配置される(ONNX Runtime のネイティブはこの限りでない。クラスライブラリを単体で publish した場合、ONNX Runtime の MSBuild targets が Windows 用 DLL を RID 非依存で複製する。ライブラリを参照するアプリケーションの publish では複製されず、実害はない)。

### CLI の依存

CLI(`src/Recognizer.Cli`)はライブラリを `ProjectReference` し、加えて CLI 固有の依存を持つ。上表の 5 件はライブラリに掛かる制約であり、以下は仕様違反ではない。

| パッケージ ID | 用途 |
| --- | --- |
| `System.CommandLine` | コマンドライン解析 |
| `OpenCvSharp4` | 画像処理には使わない。OpenCV ネイティブの警告が stderr の JSON 出力を汚すのを止めるためだけに参照する |

`OpenCvSharp4` は既にライブラリの 5 件の 1 つであり、依存グラフに新しいパッケージは増えていない。CLI の導入で実質的に増えた依存は `System.CommandLine` のみである。

## 3. 公開 API

名前空間は `Recognizer` とする。公開型は以下に列挙したものに限る。

### 3.1 共通仕様

- 画像入力は 3 形式のオーバーロードで受け付ける。
  - `OpenCvSharp.Mat`(BGR)
  - `string imagePath`(ファイルパス。画像フォーマットは OpenCV が自動判別)
  - `ReadOnlyMemory<byte>`(エンコード済み画像バイト列。フォーマットは OpenCV が自動判別)
- 検出の信頼度閾値(`confidenceThreshold` / `detectionThreshold`)と NMS 閾値(`nmsThreshold`)は、コンストラクタではなく各メソッドの引数で指定する。いずれも省略可とし、既定値を持つ。
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
    public FaceDetector(string modelPath);

    public Task<IReadOnlyList<FaceDetection>> DetectAsync(
        Mat image,
        float confidenceThreshold = 0.7f,
        float nmsThreshold = 0.5f,
        CancellationToken cancellationToken = default);

    // string imagePath / ReadOnlyMemory<byte> encodedImage の同形オーバーロードを持つ
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
    public FaceRecognizer(string detectorModelPath, string embeddingModelPath);

    // 2 画像それぞれで最高信頼度の顔の埋め込みを抽出し、コサイン類似度を返す。
    // 同一人物か否かの判定はしない
    public Task<FaceComparisonResult> CompareFacesAsync(
        Mat image1,
        Mat image2,
        float detectionThreshold = 0.7f,
        float nmsThreshold = 0.5f,
        CancellationToken cancellationToken = default);

    // 埋め込み抽出(faceRegion 省略時は顔検出して最高信頼度の顔を使用)
    public Task<FaceEmbeddingResult> ExtractEmbeddingAsync(
        Mat image,
        RectangleF? faceRegion = null,
        float detectionThreshold = 0.7f,
        float nmsThreshold = 0.5f,
        CancellationToken cancellationToken = default);

    // string imagePath / ReadOnlyMemory<byte> encodedImage の同形オーバーロードを持つ

    // コサイン類似度 [-1, 1]
    public static float CompareEmbeddings(ReadOnlySpan<float> embedding1, ReadOnlySpan<float> embedding2);
}

public enum FaceComparisonStatus
{
    Success,        // 両画像で顔を検出し、類似度を算出した
    NoFaceInImage1, // 画像 1 で顔未検出
    NoFaceInImage2  // 画像 2 で顔未検出
}

public sealed record FaceComparisonResult(
    FaceComparisonStatus Status,
    float Similarity,          // コサイン類似度 [-1, 1]。顔未検出時は 0
    FaceDetection? Face1,      // 画像 1 で使用した顔(未検出時は null)
    FaceDetection? Face2);     // 画像 2 で使用した顔(未検出時は null)

public sealed record FaceEmbeddingResult(
    float[]? Embedding,        // 顔未検出時は null
    FaceDetection? Face);      // 使用した顔(faceRegion 指定時・未検出時は null)
```

- 顔認証は判定結果(同一人物か否か)を返さず、コサイン類似度を返す。閾値判定は呼び出し側の責務とする。
- 顔未検出は予期されるエラーであり、例外ではなく結果型(`Status` / `null`)で表現する。
- 埋め込み抽出時の顔領域切り出しは、周辺情報を含む正方形(パディング比率 0.2)で行う。
- `CompareEmbeddings` は次元不一致のとき `ArgumentException` を送出する。

### 3.5 ObjectDetector(物体検出)

```csharp
public sealed class ObjectDetector : IDisposable
{
    // classNames 省略時: クラス数がモデル出力から 80 と判別できれば COCO 80 クラス名、
    // それ以外は "class_{id}" を返す
    public ObjectDetector(string modelPath, IReadOnlyList<string>? classNames = null);

    public Task<IReadOnlyList<ObjectDetection>> DetectAsync(
        Mat image,
        float confidenceThreshold = 0.5f,
        float nmsThreshold = 0.5f,
        CancellationToken cancellationToken = default);

    // string imagePath / ReadOnlyMemory<byte> encodedImage の同形オーバーロードを持つ
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
| 引数不正(空の `Mat`、次元不一致、閾値の範囲外等) | `ArgumentException` |

## 4. リポジトリ構成

```
src/Recognizer/              クラスライブラリ本体
src/Recognizer.Cli/          CLI(ライブラリを参照するコンソールアプリケーション)
tests/Recognizer.Tests/      xUnit テスト(ライブラリ)
tests/Recognizer.Cli.Tests/  xUnit テスト(CLI)
models/                      ONNX モデル置き場(gitignore 済み、実モデルはコミットしない)
docs/                        本仕様書・SDD 成果物
```

- 本書はライブラリの公開 API 仕様を定める。CLI の使い方(コマンド・オプション・出力形式)は `README.md` を参照する。

- テストで実モデルが必要な場合の扱い(小型ダミー ONNX の生成・実モデルのダウンロード手順等)は設計フェーズで確定する。

## 5. 非機能要件

- ライブラリはコンソール出力・ログ出力をしない(ロギング機構の導入はスコープ外)。
- 内部実装(前処理・テンソル変換・NMS・出力パース)は公開しない(`internal`)。
- ライブラリ(`src/Recognizer`)の依存パッケージは ONNX Runtime(`Microsoft.ML.OnnxRuntime`)と画像処理バックエンド(`OpenCvSharp4`)、およびその RID 別ネイティブランタイムパッケージ 3 件(`OpenCvSharp4.official.runtime.linux-x64` / `OpenCvSharp4.runtime.win` / `Sdcb.OpenCvSharp4.mini.runtime.osx-arm64`)の計 5 件に限る(§2 の表)。これ以外の依存追加は本仕様の変更を伴う。
  - この制約はライブラリに掛かるものであり、CLI(`src/Recognizer.Cli`)には掛からない。CLI は `System.CommandLine` を追加で参照する(§2「CLI の依存」)。テストの依存検査(`tests/Recognizer.Tests/PublicApiTests.cs`)も `src/Recognizer/Recognizer.csproj` のみを対象としている。
