# cli — 調査ログ / Gap 分析

設計(design.md)の根拠となる既存コード調査と、外部ライブラリ(System.CommandLine)の API 実測ログ。中間生成物であり、実装完了で凍結される。

## 1. 既存プロジェクトの規約

| 項目 | 実測値 | 根拠 |
| --- | --- | --- |
| TargetFramework | `net10.0` | `src/Recognizer/Recognizer.csproj` |
| Nullable | `enable` | 同上 |
| ライブラリの依存 | 5 件(OnnxRuntime 1.27.1 / OpenCvSharp4 4.13.0 + RID 別ランタイム 3 件) | 同上 |
| RID 設定 | ライブラリ側になし(publish 時に `-r` で指定) | 同上 |
| テスト依存 | xUnit 2.9.3 / Microsoft.NET.Test.Sdk 18.7.0 / xunit.runner.visualstudio 3.1.4 | `tests/Recognizer.Tests/Recognizer.Tests.csproj` |
| Fixtures 供給 | `<None Include="Fixtures\*.onnx" CopyToOutputDirectory="PreserveNewest" LinkBase="Fixtures" />` | 同上 :26-28 |
| Fixtures パス解決 | `Path.Combine(AppContext.BaseDirectory, "Fixtures", fileName)` | `tests/Recognizer.Tests/FaceDetectorTests.cs:13-14` |
| テスト用画像 | `Cv2.ImWrite` で一時 PNG を書き出す(`Path.GetTempPath()` + GUID) | 同 :274-275, 393-394 |
| `Directory.Build.props` | 存在しない(csproj 単体で完結) | リポジトリ走査 |

`Recognizer.sln` は `src` / `tests` のソリューションフォルダを持ち、それぞれ `Recognizer` / `Recognizer.Tests` を含む。

## 2. CLI が呼ぶ公開 API(実シグネチャ)

すべて `string` パスのオーバーロードが存在することを確認した。

```csharp
FaceDetector(string modelPath)
Task<IReadOnlyList<FaceDetection>> DetectAsync(string imagePath, float confidenceThreshold = 0.7f, float nmsThreshold = 0.5f, CancellationToken ct = default)

ObjectDetector(string modelPath, IReadOnlyList<string>? classNames = null)
Task<IReadOnlyList<ObjectDetection>> DetectAsync(string imagePath, float confidenceThreshold = 0.5f, float nmsThreshold = 0.5f, CancellationToken ct = default)

FaceRecognizer(string detectorModelPath, string embeddingModelPath)
Task<FaceComparisonResult> CompareFacesAsync(string imagePath1, string imagePath2, float detectionThreshold = 0.7f, float nmsThreshold = 0.5f, CancellationToken ct = default)
```

結果型: `FaceDetection(RectangleF BBox, float Confidence, FaceLandmarks? Landmarks)` / `FaceLandmarks(PointF LeftEye, RightEye, Nose, LeftMouth, RightMouth)` / `ObjectDetection(int ClassId, string ClassName, float Confidence, RectangleF BBox)` / `FaceComparisonResult(FaceComparisonStatus Status, float Similarity, FaceDetection? Face1, FaceDetection? Face2)`。

## 3. 例外の実送出箇所(code マッピングの根拠)

| 例外型 | 送出箇所(実コード) | 発生条件 |
| --- | --- | --- |
| `FileNotFoundException` | `FaceDetector.cs:33` / `ObjectDetector.cs:42` / `FaceRecognizer.cs:46` | コンストラクタでモデルファイル不在 |
| `Microsoft.ML.OnnxRuntime.OnnxRuntimeException` | ライブラリは包まず透過(`FaceDetector.cs:36` のコメント) | 壊れた ONNX のロード失敗。既存テストが `Assert.Throws<OnnxRuntimeException>`(`FaceDetectorTests.cs:44-51`)で確認済み |
| `NotSupportedException` | `Internal/ModelIntrospector.cs`(27 箇所) | 非対応のモデル形式。既存テスト `FaceDetectorTests.cs:61-65` |
| `ArgumentException` | `Internal/ImageDecoder.cs:23,45,63,74` | 画像のロード/デコード失敗のみ |
| `ArgumentException` | `FaceDetector.cs:242` / `ObjectDetector.cs:280` / `FaceRecognizer.cs:464` | 閾値が 0.0〜1.0 の範囲外 |

**確認結果(前提 P1 の裏取り)**: `ImageDecoder` の `ArgumentException` は画像起因のみで、閾値検証(`EnsureThresholdInRange`)とは送出箇所が分離している。CLI が閾値をパース時に検証すれば、検出メソッド呼び出し中に到達する `ArgumentException` は画像起因に限定できる。P1 は成立する。

## 4. テスト用ダミー ONNX(Fixtures)

`tests/Recognizer.Tests/Fixtures/README.md` より、CLI テストで使うものを選定した。

| 用途 | Fixture | 振る舞い |
| --- | --- | --- |
| 顔検出・ランドマーク無し | `face_nchw_transposed_f5.onnx` | 定数出力。入力画像によらず conf 0.95 / 0.85 / 0.75 の 3 件を返す(`Landmarks=null`) |
| 顔検出・ランドマーク付き | `face_nchw_transposed_f20.onnx` | F=20。`FaceLandmarks` を含む |
| 顔検出・入力依存(未検出を作れる) | `face_inputconf_f5.onnx` | 入力画像の平均画素値 = confidence。白画像 → 検出、黒画像 → 未検出 |
| 物体検出 | `object_nchw_transposed_4c3.onnx` | 3 クラス。cls0 0.90 / cls1 0.85 / cls2 0.70 の 3 件 |
| 物体検出・COCO 名解決 | `object_transposed_coco80.onnx` | 80 クラス。`classNames` 省略時に COCO 名(person/car/cat)へ解決 |
| 顔埋め込み | `embed_nchw_meanrgb_d4.onnx` | 入力依存(mean 各 ch + 1.0)。D=4 |
| 非対応モデル形式 | `face_unsupported_f7.onnx` / `object_unsupported_f4.onnx` | `NotSupportedException` |

- 定数出力の fixture により、CLI の正常系テストは入力画像の中身に依存せず決定論的に検出件数を検証できる。
- `face_inputconf_f5.onnx` + 白/黒画像の組み合わせで、`compare-face` の `Success` / `NoFaceInImage1` / `NoFaceInImage2`、`detect-face` の検出 0 件を作り分けられる。

## 5. CI(`.github/workflows/ci.yml`)

- マトリクス: `ubuntu-latest`/`linux-x64`、`windows-latest`/`win-x64`、`macos-15`/`osx-arm64`。
- ステップ: `dotnet build -c Release` → `dotnet test -c Release --no-build` → `dotnet publish src/Recognizer/Recognizer.csproj -c Release -r <rid> -o publish-<rid>`(:39)。
- `dotnet test` はソリューション全体を対象とするため、CLI テストは sln 登録だけで CI に乗る(変更不要)。
- publish はライブラリのパスがハードコードされているため、**CLI の publish は自動では CI に乗らない**(前提 P6 の根拠)。

## 6. PublicApiTests の検査範囲(前提 P5 の裏取り)

| テスト | 走査対象 | CLI への影響 |
| --- | --- | --- |
| 公開型の列挙(:44-61) | `typeof(FaceDetector).Assembly`(= `Recognizer` アセンブリのみ) | 影響なし(CLI は別アセンブリ) |
| コンソール出力の禁止(:107-118) | `<repo>/src/Recognizer` ディレクトリのみ | 影響なし(`src/Recognizer.Cli` は走査されない) |
| 依存パッケージの検査(:125-138) | `src/Recognizer/Recognizer.csproj` のみ | 影響なし(`System.CommandLine` は検査対象外) |

P5 は成立する。

## 7. System.CommandLine の API 実測(source-driven)

`dotnet package search` で入手可能な最新安定版は **2.0.9**(2.0 は GA 済み。beta 系とは API が大きく異なる)。devcontainer 上に検証用プロジェクトを作り、リフレクションと実行で API と挙動を実測した(記憶や beta 系の記事に依拠しない)。

### 7.1 確認した API 形状(2.0.9)

```csharp
new Argument<string>("image") { Description = "..." }
new Option<string>("--model", "-m") { Description = "...", Required = true }   // Required は Option 基底の set 可能プロパティ
new Option<float>("--confidence") { DefaultValueFactory = _ => 0.7f, CustomParser = ar => ... }
command.Add(argument | option | subcommand)
command.SetAction(Func<ParseResult, CancellationToken, Task<int>>)             // 非同期 + CancellationToken + 終了コード
ParseResult pr = rootCommand.Parse(string[] args)
pr.Errors            // IReadOnlyList<ParseError>(Message / SymbolResult)
pr.UnmatchedTokens
pr.GetValue(option) / pr.GetValue(argument)
await pr.InvokeAsync(new InvocationConfiguration { EnableDefaultExceptionHandler = false, Output = ..., Error = ... }, ct)
```

`InvocationConfiguration` の `Output` / `Error` が `TextWriter` で差し替え可能 → **テストから stdout/stderr をインプロセスで捕捉できる**。

### 7.2 実測した挙動(重要な発見)

| 入力 | `pr.Errors` | `pr.Action` | 備考 |
| --- | --- | --- | --- |
| 正常 | 0 件 | 設定した Action | `InvokeAsync` が Action の戻り値(int)をそのまま返す |
| `--help` | 0 件 | `HelpAction` | ヘルプを Output に書き、終了コード 0 |
| 必須オプション欠落 | 1 件 / `SymbolResult` = `OptionResult(--model)` | `ParseErrorAction` | メッセージは英語 |
| 位置引数不足 | 1 件 / `SymbolResult` = `ArgumentResult(image)` | 同上 | |
| 位置引数の過剰・未知オプション | 1 件 / `UnmatchedTokens` に該当トークン | 同上 | |
| 未知のコマンド | 2 件以上(未認識トークン数に依存。`nosuch` で 2 件、`nosuch a.jpg` で 3 件)/ `UnmatchedTokens` に該当、`CommandResult.Command` は root | 同上 | 件数は入力依存のため分類には使わない(構造で判定する) |
| コマンド未指定 | 1 件 / `UnmatchedTokens` は空、`CommandResult.Command` は root | 同上 | |
| オプションに値が無い(`--confidence` が末尾) | 1 件 / `SymbolResult` = `OptionResult(--confidence)`、トークン 0 件 | 同上 | **`CustomParser` は呼ばれない**。必須オプション欠落と構造が似るため、`Option.Required` で区別する必要がある(design §8.2 の順序 4・5) |

- **落とし穴(実測で判明)**: `Option.Validators` の中で `OptionResult.GetValueOrDefault<float>()` を呼ぶと、`--confidence abc` のような**変換不能な値で `Parse()` 自体が `InvalidOperationException` を投げる**(ParseError にならず、CLI が JSON を出す前にクラッシュする)。
- **対策(実測で確認)**: `CustomParser`(`Func<ArgumentResult, T>`)で「数値変換」と「値域検証」を一体で行い、失敗時は `ar.AddError(...)` して `default` を返す。これにより **変換不能・値域外の双方が例外ではなく `ParseError` として現れる**。日本語メッセージも自前で出せる。
- フレームワークが生成するパースエラーのメッセージは英語("Option '--model' is required." 等)。CLAUDE.md の日本語規約を満たすため、CLI は `ParseError.Message` をそのまま使わず、`SymbolResult` の構造から種別を判定して**日本語メッセージと code を自前で生成する**(design.md §8.2)。
- 終了コードは `ParseErrorAction` に委ねず、`pr.Errors.Count > 0` の時点で `InvokeAsync` を呼ばずに CLI 自身が JSON を stderr に書いて終了する(既定の英語エラー + ヘルプが stdout/stderr を汚さないようにするため)。

出典: NuGet `System.CommandLine` 2.0.9(`dotnet package search` で確認)の実アセンブリをリフレクションで検査し、上記の各入力を実行して観測した(2026-07-13、devcontainer / .NET SDK 10.0.301)。

## 8. System.Text.Json の実測(要件 6.2〜6.4)

同じく devcontainer 上で実際にシリアライズして観測した。

| 観測 | 結果 |
| --- | --- |
| `record FaceDto(BboxDto BBox, ...)` + `PropertyNamingPolicy.CamelCase` | `{"bBox":{...}}` ← **要件違反**。camelCase 変換は先頭の連続大文字列をまとめて小文字化する |
| `record FaceDto(BboxDto Bbox, ...)` + 同上 | `{"bbox":{"x":1.5,"y":2,"width":3,"height":4},"confidence":0.95}` ← 要件どおり |
| `JsonStringEnumConverter()`(命名ポリシーを渡さない) | `"status":"NoFaceInImage1"` ← 列挙子名そのまま(要件 5.3・6.2) |
| `CurrentCulture = de-DE` で float を出力 | `0.7`(小数点はカルチャに依存しない。要件 6.4) |
| ソース生成の context を**直接**使う(`Serialize(v, CliJsonContext.Default.DetectFaceOutput)`) | `{"Image":...,"Faces":[{"BBox":...}]}` ← PascalCase・enum が数値。**命名ポリシーが効かない** |
| `JsonSerializerOptions { TypeInfoResolver = CliJsonContext.Default, PropertyNamingPolicy = CamelCase, Converters = { new JsonStringEnumConverter() } }` 経由 | camelCase + 列挙子名。これが正しい結線 |
| `float.TryParse("NaN")` | **成功する**(`NaN`)。`v < 0f \|\| v > 1f` は NaN に対して false になるため値域検証を素通りする。`!(v >= 0f && v <= 1f)` なら弾ける |
