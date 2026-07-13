# multi-platform — 調査ログ / Gap 分析

design.md の判断根拠。一次情報の出典と、devcontainer 上で実施した検証(スパイク)の結果を記録する。

## 1. Gap 分析(既存コードベース)

### 1.1 現状調査

| 項目 | 現状 |
| --- | --- |
| 依存 | `Microsoft.ML.OnnxRuntime` 1.27.1 / `OpenCvSharp4` 4.13.0.20260627 / `OpenCvSharp4.official.runtime.linux-x64` 4.13.0.20260627(`src/Recognizer/Recognizer.csproj:11-13`) |
| RID 設定 | `RuntimeIdentifier(s)` 宣言なし。条件付き `ItemGroup` なし(AnyCPU ビルド) |
| CI | `.github/workflows/` は存在しない(`.github/CODEOWNERS` のみ) |
| ソリューション | `Recognizer.sln` に `src/Recognizer` と `tests/Recognizer.Tests` の 2 プロジェクト |
| テスト | 224 件 green(`dotnet test` 実行で確認)。fixture は ONNX のみ(実画像なし) |

### 1.2 OpenCvSharp の使用実態(要件の「6 操作」との対応)

| 操作 | 使用箇所 |
| --- | --- |
| デコード | `Cv2.ImRead`(`Internal/ImageDecoder.cs:38`)/ `Cv2.ImDecode`(同 `:68`) |
| リサイズ | `Cv2.Resize`(`Internal/Preprocessor.cs:50`, `Internal/EmbeddingPreprocessor.cs:33`) |
| 色変換 | `Cv2.CvtColor`(BGR2RGB。`Internal/Preprocessor.cs:58`, `Internal/EmbeddingPreprocessor.cs:36`) |
| パディング | `Cv2.CopyMakeBorder`(`Internal/Preprocessor.cs:53-55`。Scalar(114,114,114)) |
| ROI 切り出し | `new Mat(image, Rect)` + `Clone()`(`Internal/FaceCropper.cs:60-61`) |
| 画素バッファ連続アクセス | `rgb.GetArray(out Vec3b[] pixels)`(`Internal/Preprocessor.cs:61`, `Internal/EmbeddingPreprocessor.cs:39`)→ row-major で `DenseTensor<float>` に充填。検出は `/255`、埋め込みは `(x-127.5)/128` |

### 1.3 公開 API の Mat 露出(4 メソッド)

- `FaceDetector.DetectAsync(Mat, float, float, CancellationToken)`(`FaceDetector.cs:62`)
- `ObjectDetector.DetectAsync(Mat, float, float, CancellationToken)`(`ObjectDetector.cs:72`)
- `FaceRecognizer.CompareFacesAsync(Mat, Mat, float, float, CancellationToken)`(`FaceRecognizer.cs:129`)
- `FaceRecognizer.ExtractEmbeddingAsync(Mat, RectangleF?, float, float, CancellationToken)`(`FaceRecognizer.cs:283`)

path / bytes オーバーロード(計 8)は `ImageDecoder` で Mat 化してから Mat 版へ委譲する(所有権を移し `AwaitAndDisposeAsync` で破棄)。よって **Mat は公開契約の一部であり、内部実装の詳細ではない**。

### 1.4 テスト側の OpenCvSharp 依存

`FaceDetectorTests` / `ObjectDetectorTests` / `FaceRecognizerTests` / `EmbeddingPreprocessorTests` / `FaceCropperTests` が `new Mat(h, w, MatType.CV_8UC3, Scalar...)` で入力画像を生成。さらに `Cv2.ImWrite`(`FaceDetectorTests.cs:275`)・`Cv2.ImEncode`(同 `:296`, `FaceRecognizerTests.cs:562`)・`Cv2.Absdiff` / `Cv2.CountNonZero`(`FaceCropperTests.cs:35-38`)を使用。**バックエンドを置換すると、これら 5 クラス(30 件超のテスト)を書き換える必要がある。**

### 1.5 プラットフォーム移植性(Windows / macOS で落ちうる箇所)

問題なし。fixture 読み込みは `Path.Combine(AppContext.BaseDirectory, "Fixtures", ...)`、一時ファイルは `Path.GetTempPath()`、`PublicApiTests` のリポジトリルート探索は `Recognizer.sln` を上方向に探索し `Path.DirectorySeparatorChar` で正規化(`PublicApiTests.cs:133-153`)。ハードコードされたパス区切りは検出されなかった。

### 1.6 要件 → 資産マップ

| 要件 | 必要な技術要素 | 既存資産 | タグ |
| --- | --- | --- | --- |
| 1(3 RID でのビルド・実行) | win-x64 / osx-arm64 のネイティブランタイムパッケージ | linux-x64 のみ | `Missing` |
| 2(ライブラリ選定) | 候補の一次情報 | なし | `Missing` |
| 3(公開 API 契約の維持) | Mat オーバーロード 4 件 | 実装済み | `Constraint` |
| 4(非回帰・依存検査) | `PublicApiTests` の許可リスト(3 パッケージ、厳密一致) | 実装済み(`PublicApiTests.cs:31-32`) | `Constraint` |
| 5(CI) | GitHub Actions ワークフロー | なし | `Missing` |
| 6(恒久情報) | api-spec §2 / §5、README、CLAUDE.md | 既存(linux 前提の記述) | `Missing`(更新要) |
| 7(依存の健全性) | ライセンス・供給元の記録 | なし | `Missing` |

## 2. 一次情報(NuGet レジストリ / 公式ドキュメント)

取得方法: NuGet flat container API(`https://api.nuget.org/v3-flatcontainer/<id>/index.json`)で現行版を、`<id>.nuspec` でライセンス・作者を確認(2026-07-13 時点)。

| パッケージ | 現行安定版 | ライセンス | 作者 | 出典 |
| --- | --- | --- | --- | --- |
| `OpenCvSharp4` | 4.13.0.20260627 | Apache-2.0 | shimat(公式) | https://www.nuget.org/packages/OpenCvSharp4 |
| `OpenCvSharp4.official.runtime.linux-x64` | 4.13.0.20260627 | Apache-2.0 | shimat(公式) | https://www.nuget.org/packages/OpenCvSharp4.official.runtime.linux-x64 |
| `OpenCvSharp4.runtime.win` | 4.13.0.20260627 | Apache-2.0 | shimat(公式) | https://www.nuget.org/packages/OpenCvSharp4.runtime.win |
| `Sdcb.OpenCvSharp4.mini.runtime.osx-arm64` | 4.13.0.45 | Apache-2.0 | sdcb(**サードパーティ**) | https://github.com/sdcb/opencvsharp-mini-runtime |
| `SkiaSharp` | 4.150.0 | MIT | Microsoft | https://www.nuget.org/packages/SkiaSharp |
| `SixLabors.ImageSharp` | 4.0.0 | **Six Labors Split License** | Six Labors | https://github.com/SixLabors/ImageSharp/blob/main/LICENSE |

### 2.1 OpenCvSharp の osx-arm64 公式ランタイムは存在しない(確認済み)

flat container API で以下を照会し、**いずれも 404(NOT FOUND)**:

- `opencvsharp4.official.runtime.osx-arm64` → 存在しない
- `opencvsharp4.runtime.osx` → 存在しない
- `opencvsharp4.official.runtime.win-x64` → 存在しない(Windows の公式パッケージ ID は `OpenCvSharp4.runtime.win`)

唯一存在する macOS 系公式パッケージ `opencvsharp4.runtime.osx.10.15-x64` は **最終版 4.6.0.20230105(2023 年)で更新停止**、かつ x64 で Apple Silicon 非対応。したがって **osx-arm64 の現行版はサードパーティの `Sdcb.OpenCvSharp4.mini.runtime.osx-arm64` が唯一の選択肢**(要件の前提が裏付けられた)。

### 2.1.1 ただし linux-arm64 の公式ランタイムは存在する(CLAUDE.md の記述は現在は誤り)

`OpenCvSharp4.runtime.linux-arm64` **4.13.0.20260627**(shimat / Apache-2.0)が現行版として存在する(flat container API で確認。全 5 版)。

現在の `CLAUDE.md` は「OpenCvSharp の公式ネイティブランタイムが linux-arm64 非対応のため devcontainer は linux/amd64(Apple Silicon では Rosetta エミュレーション)」と書いているが、**この理由は 4.13 時点では成り立たない**。要件 6.4 は「linux-arm64 非対応の理由が有効か否かを含む」実態への更新を求めており、この記述を訂正する必要がある(linux-arm64 への**対応**自体は要件のスコープ外)。

### 2.2 Six Labors Split License の条件(確認済み)

Version 1.0(June 2022)。Apache-2.0 で利用できるのは、OSS 利用 / 推移的依存 / **年間総売上 100 万 USD 未満の営利企業・個人** / 非営利団体の 4 類型のみ。それ以外は https://sixlabors.com/pricing/ で商用ライセンスの購入が必要。

### 2.3 GitHub Actions ランナー(確認済み)

`actions/runner-images` の README より:

| ラベル | アーキテクチャ |
| --- | --- |
| `macos-latest` / `macos-15` | **arm64(Apple Silicon)** |
| `macos-14` | arm64(ただし **deprecated**) |
| `macos-15-intel` / `macos-latest-large` | x64 |
| `ubuntu-latest` | x64(Ubuntu 24.04) |
| `windows-latest` | x64(Windows Server 2025) |

出典: https://github.com/actions/runner-images(README の Available Images 表)

→ 要件 5.3 の「`macos-14` 以降の Apple Silicon ランナー」は **`macos-15` を明示指定**して満たす(`macos-14` は deprecated のため採用しない)。

`actions/setup-dotnet` は `dotnet-version: 10.0.x` の指定形式(A.B.x)で .NET 10 SDK の最新パッチを導入できる。出典: https://github.com/actions/setup-dotnet

## 3. スパイク(devcontainer 上での実証)

`/tmp` に使い捨てプロジェクトを作り、**リポジトリを変更せずに** RID 別ネイティブ資産の解決を検証した(consumer となる exe から classlib を参照する構成。classlib 単体のビルドではネイティブ資産は出力されないため)。

参照した 5 パッケージ: `Microsoft.ML.OnnxRuntime` 1.27.1 / `OpenCvSharp4` 4.13.0.20260627 / `OpenCvSharp4.official.runtime.linux-x64` / `OpenCvSharp4.runtime.win` / `Sdcb.OpenCvSharp4.mini.runtime.osx-arm64` 4.13.0.45(すべて**無条件参照**)。

### 3.1 RID 未指定 `dotnet build`(ホスト = linux-x64)

出力の `runtimes/` に全 RID の資産が配置され、実行時に RID で解決される:

```
runtimes/linux-x64/native/libOpenCvSharpExtern.so
runtimes/osx-arm64/native/libOpenCvSharpExtern.dylib
runtimes/win-x64/native/OpenCvSharpExtern.dll
runtimes/{linux-x64,linux-arm64,osx-arm64,win-x64,win-arm64}/native/(onnxruntime 各種)
```

→ **要件 1.3 を満たす**(RID 未指定でも従来どおり devcontainer で動く)。ONNX Runtime が全 OS のネイティブを 1 パッケージに同梱している前提も、これで**検証済み**になった。

### 3.2 `dotnet publish -r <RID>`(3 RID すべて成功、error 0)

**publish 対象が何かで結果が変わる**。両方を実測した(この差を最初は見落としており、文書ゲートの指摘で判明した)。

**(a) consumer app(exe)を publish した場合** — ライブラリ利用者の実際の使い方:

| RID | 出力に配置されたネイティブ資産 |
| --- | --- |
| `linux-x64` | `libOpenCvSharpExtern.so`, `libonnxruntime.so`, `libonnxruntime_providers_shared.so` |
| `win-x64` | `OpenCvSharpExtern.dll`, `opencv_videoio_ffmpeg4130_64.dll`, `onnxruntime.dll`, `onnxruntime_providers_shared.dll` |
| `osx-arm64` | `libOpenCvSharpExtern.dylib`, `libonnxruntime.dylib` |

**対象 RID のネイティブのみ**がフラット配置され、他プラットフォームの資産は含まれない。

**(b) クラスライブラリ(`Recognizer.csproj` 相当)を単体で publish した場合**:

| RID | 出力に配置されたネイティブ資産 |
| --- | --- |
| `linux-x64` | `libOpenCvSharpExtern.so`, `libonnxruntime.so`, `libonnxruntime_providers_shared.so`, **`onnxruntime.dll`**, **`onnxruntime_providers_shared.dll`** |
| `osx-arm64` | `libOpenCvSharpExtern.dylib`, `libonnxruntime.dylib`, **`onnxruntime.dll`**, **`onnxruntime_providers_shared.dll`** |
| `win-x64` | `OpenCvSharpExtern.dll`, `opencv_videoio_ffmpeg4130_64.dll`, `onnxruntime.dll`, `onnxruntime_providers_shared.dll`, `.lib` 各種 |

linux / osx の出力に **Windows 用の `onnxruntime.dll` が混入する**(太字)。原因は `Microsoft.ML.OnnxRuntime` の `build/netstandard2.0/Microsoft.ML.OnnxRuntime.targets` が、**直接参照するプロジェクト**に対して Windows ネイティブを RID 非依存でコピーすること。consumer app は `buildTransitive/` 経由で参照するためこの targets が効かず、(a) では混入しない。

**画像処理バックエンド(OpenCvSharp)のネイティブは、(a)(b) いずれでも RID 別に正しく解決され、他 RID の資産は混入しない。**

→ 要件 1.2 は (a)(b) 双方で満たされる。要件 1.4(「特定の 1 つでのみ有効な**ネイティブランタイムパッケージ**」を他 RID の出力に含めない)は、RID 別ランタイムパッケージ(OpenCvSharp の 3 件)について満たされる。ORT は「単一パッケージが全 RID のネイティブを同梱する」構成であり RID 別ランタイムパッケージではないため要件 1.4 の対象外だが、この混入は design.md §10.3 に既知事項として記録し、設計ゲートで人間に確認する。

→ 3 つのランタイムパッケージを無条件参照するだけでよく、**条件付き `ItemGroup` や `RuntimeIdentifiers` の宣言は不要**(YAGNI)。

### 3.3 Sdcb mini ビルドが必要な操作を含むか(検証済み)

`Sdcb.OpenCvSharp4.mini.runtime.osx-arm64` 4.13.0.45 の nupkg を展開し、`runtimes/osx-arm64/native/libOpenCvSharpExtern.dylib`(10.8 MB)を検査:

- 必要 6 操作に対応するエクスポート: `imgcodecs_imread` / `imgcodecs_imdecode` / `imgproc_resize` / `imgproc_cvtColor` / **`core_copyMakeBorder`** / `core_Mat_new3` **すべて存在**(`copyMakeBorder` は OpenCV では core モジュール所属のため、ネイティブ名も `core_*` である。当初 `imgproc_copyMakeBorder` と記載していたが、これは実在しないシンボル名だった)
- テストのみが使う API: `imgcodecs_imencode` / `imgcodecs_imwrite` / `core_absdiff` / `core_countNonZero` **すべて存在**
- 同梱コーデック: libjpeg-turbo / libpng / libwebp / libtiff / openjpeg → **imgcodecs のデコーダを内蔵**(`ImRead` / `ImDecode` が機能する)

「mini」は videoio・dnn 等を削った縮小ビルドであり、本ライブラリが使う core / imgproc / imgcodecs は含まれる。

**未検証(UNVERIFIED)**: dylib は Mach-O / arm64 のため devcontainer(linux/amd64)では**実行できない**。実行時の動作確認は CI(macos-15)に委ねる。

## 4. 画像処理ライブラリの比較(5 評価軸)

| 評価軸 | A. OpenCvSharp 継続 | B. SkiaSharp | C. SixLabors.ImageSharp |
| --- | --- | --- | --- |
| **公開 API 契約への影響** | **なし**。`Mat` オーバーロード 4 件をそのまま維持(要件 3.1-3.3 を無改修で満たす) | **破壊的**。`Mat` を `SKBitmap` 等へ置換 → 公開型・シグネチャが変わる。要件 2.4 発火 | **破壊的**。`Image<Bgr24>` 等へ置換 → 同上。要件 2.4 発火 |
| **ライセンス** | Apache-2.0(本体・全ランタイム。Sdcb 含む) | MIT | **Six Labors Split License**。年商 100 万 USD 以上の営利利用は有償 → 要件 7.2 で人間承認が必要 |
| **供給元の信頼** | 本体・win・linux は公式(shimat)。**osx-arm64 のみサードパーティ(sdcb)** | Microsoft(公式)。最も安定 | Six Labors(公式) |
| **性能** | ネイティブ OpenCV。現行実装のベースライン | ネイティブ Skia。画像処理は高速だが、`copyMakeBorder` 相当は canvas 描画で代替する必要 | **pure-managed のため一般にネイティブより遅い**。リサイズ・デコードで顕著 |
| **ネイティブ依存 / RID 構成** | 3 RID すべてでネイティブ資産あり(§2・§3 で実証) | 公式 NativeAssets が win/linux/macOS に存在。ネイティブ依存は残る | **ネイティブ依存ゼロ**。全 RID で無設定動作 |
| **6 操作の充足** | 全操作を直接提供(実証済み) | デコード・リサイズ・色変換・ROI・画素アクセスは可。パディングは canvas 描画で代替 | 全操作を提供(`Mutate` / `ProcessPixelRows`) |
| **移行コスト** | **ゼロ**(csproj に 2 行追加) | 内部 5 ファイル + 公開 3 クラス + テスト 5 クラス(30 件超)を書き換え。1 unit を超える | 同左 |

### 重要な事実: 「完全な pure-managed 化」は達成できない

ImageSharp を採用しても、推論側の `Microsoft.ML.OnnxRuntime` がネイティブライブラリを P/Invoke する(§3.1 の `runtimes/*/native/libonnxruntime.*` が実証)。つまり **ネイティブ依存はどの選択肢でも不可避**であり、「ネイティブ依存を無くす」ことは ImageSharp を選ぶ理由にならない。ImageSharp が持つ利点は「RID 別ランタイムパッケージの管理が不要」という**構成の単純さ**に限られるが、その単純さは §3 の実証により **OpenCvSharp 継続でも(無条件参照 3 つで)十分に単純**であることが分かった。

## 5. 結論

**A. OpenCvSharp 継続を採用する。** 根拠は design.md §2「技術スタック」に記す。

主要な根拠の要約:

1. 公開 API が `Mat` を型として露出しており(要件 3、`api-spec.md` §3.1)、B・C はいずれも破壊的変更になる。要件 2.4 により、その場合は unit の再分割と再承認が必要で、**プラットフォーム対応という本 unit の目的達成が遅れる**。
2. B・C を選ぶ動機として最有力だった「ネイティブ依存の排除」は、ONNX Runtime のネイティブ依存により**そもそも達成不能**(§4)。
3. C はライセンス上、下流利用者に売上規模依存の課金義務を波及させうる(要件 7.2 の承認事項)。MIT のコアライブラリの依存として不適。
4. A の唯一の弱点である「osx-arm64 がサードパーティ依存」は、リスクとして受容し撤退条件を design.md §10 に明記する(要件 2.3)。Apache-2.0 かつ必要な全関数の存在を実証済み(§3.3)。
