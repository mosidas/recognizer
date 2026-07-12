# face-recognition — 実装タスク

> 仕様の詳細は同じディレクトリの requirements.md(要件 ID)・design.md(節)を参照する。
> このファイルには仕様を転記しない。

## タスク一覧

- [ ] 1. テスト fixture 増設(埋め込み・入力依存検出)
  - [x] 1.1 fixture ⑰〜㉓ の生成スクリプト追加・生成・コミットと README 追記
        _Requirements: 2.2, 2.3, 2.6, 3.5, 4.3, 4.4_
        _Boundary: TestFixtures_
    - 対象ファイル: `tools/generate_test_models.py`(変更・builder 追加), `tests/Recognizer.Tests/Fixtures/embed_nchw_meanrgb_d4.onnx` / `embed_nhwc_meanrgb_d4.onnx` / `embed_dynamic_input_d4.onnx` / `embed_unsupported_rank3.onnx` / `embed_unsupported_rank2_batch2.onnx` / `embed_unsupported_dynamic_dim.onnx` / `face_inputconf_f5.onnx`(生成物 7 種・新規。rank1 出力 `embed_nchw_rank1_d4.onnx`=㉔ は分岐網羅のためタスク 2.1 で追加), `tests/Recognizer.Tests/Fixtures/README.md`(変更・⑰〜㉔ 追記)
    - 設計参照: design.md §9 テスト戦略(fixture ⑰〜㉔ の入出力構成・入力依存/決定論の根拠)、design.md §6 ModelIntrospector (e-b)(e-d)(rank1 `[D]`/rank2 `[1,D]` の受理と非対応分岐の網羅。㉒=出力 `[2,4]`(rank2 先頭次元 ≠ 1)・㉓=出力 `[1,D]` の D 動的軸・㉔=rank1 `[4]` は §9 の集合を補うもの)、research.md §4(前処理の一次情報・生成方針)
    - 検証コマンド: `python3 -m venv /tmp/onnx-venv && /tmp/onnx-venv/bin/pip install onnx && /tmp/onnx-venv/bin/python tools/generate_test_models.py` の後、7 ファイルの存在と各サイズが 100 KB 未満であること・既存 16 fixture のバイト列が不変であること(`git diff --stat`)

- [ ] 2. 埋め込みモデルの形式自動判別(契約先行)
  - [x] 2.1 EmbeddingModelSpec と ModelIntrospector.IntrospectEmbedding のテストと実装(既定サイズ引数化を含む)
        _Requirements: 2.1, 2.2, 2.3, 2.6_
        _Boundary: ModelIntrospector_
        _Depends: 1.1_
    - 対象ファイル: `src/Recognizer/Internal/EmbeddingModelSpec.cs`(新規), `src/Recognizer/Internal/ModelIntrospector.cs`(変更・`IntrospectEmbedding` 追加/入力判別の既定サイズ引数化), `tests/Recognizer.Tests/ModelIntrospectorTests.cs`(変更・追加分岐テスト)
    - 設計参照: design.md §6 ModelIntrospector 判別規則 (e-a)〜(e-d)、§2 Boundary Map(既定サイズ 112・既存呼び出しは 640 で挙動不変)
    - 検証コマンド: `dotnet test --filter "FullyQualifiedName~ModelIntrospectorTests"`(rank1 `[D]`/rank2 `[1,D]` の次元確定 / 動的入力軸 → 112 既定(⑲) / 非対応 4 分岐を各 fixture で個別行使: 複数出力 → 既存 `face_multi_output.onnx`・rank3 以上 → ⑳ `embed_unsupported_rank3.onnx`・rank2 先頭次元 ≠ 1 → ㉒ `embed_unsupported_rank2_batch2.onnx`・D 動的 → ㉓ `embed_unsupported_dynamic_dim.onnx` の各々で NotSupportedException / 入力判別不能は既存入力側非対応 fixture を再利用 / 既存検出判別テストの非回帰)

- [ ] 3. 純粋計算部品(境界独立・並行可)
  - [x] 3.1 (P) FaceCropper(正方形化・境界クリップ・退化/非交差検査)のテストと実装
        _Requirements: 3.4, 3.7_
        _Boundary: FaceCropper_
    - 対象ファイル: `src/Recognizer/Internal/FaceCropper.cs`(新規), `tests/Recognizer.Tests/FaceCropperTests.cs`(新規)
    - 設計参照: design.md §6 FaceCropper(`Validate`・`CropSquare`。中心保持・辺長 = 長辺 × 1.4・`LetterboxParams.ClampToBounds`(`src/Recognizer/Internal/Letterbox.cs`)再利用・`Clone` 返却・退化 → ArgumentException)
    - 検証コマンド: `dotnet test --filter "FullyQualifiedName~FaceCropperTests"`(正方形サイズ・中心保持・境界クリップ・幅/高さ ≤ 0・画像非交差の各分岐)
  - [ ] 3.2 (P) EmbeddingPreprocessor(リサイズ・BGR→RGB・正規化・NCHW/NHWC テンソル化)のテストと実装
        _Requirements: 2.2_
        _Boundary: EmbeddingPreprocessor_
        _Depends: 2.1_
    - 対象ファイル: `src/Recognizer/Internal/EmbeddingPreprocessor.cs`(新規), `tests/Recognizer.Tests/EmbeddingPreprocessorTests.cs`(新規)
    - 設計参照: design.md §6 EmbeddingPreprocessor(`(x−127.5)/128` 正規化・Layout 別詰め順)、research.md §2(正規化式の一次情報)
    - 検証コマンド: `dotnet test --filter "FullyQualifiedName~EmbeddingPreprocessorTests"`(単色画像で正規化値・NCHW/NHWC の要素配置を照合)

- [ ] 4. 公開結果型(データのみ・境界独立)
  - [x] 4.1 (P) FaceComparisonStatus / FaceComparisonResult / FaceEmbeddingResult の定義
        _Requirements: 7.1, 7.2_
        _Boundary: ResultTypes_
    - 対象ファイル: `src/Recognizer/FaceComparisonStatus.cs`(新規), `src/Recognizer/FaceComparisonResult.cs`(新規), `src/Recognizer/FaceEmbeddingResult.cs`(新規)
    - 設計参照: design.md §7 データモデル(api-spec 3.4 と文字単位一致・public sealed record / public enum)
    - 検証コマンド: `dotnet build`

- [ ] 5. FaceRecognizer 公開 API(パイプライン統合)
  - [ ] 5.1 (P) CompareEmbeddings(static コサイン類似度)のテストと実装
        _Requirements: 5.1, 5.2, 5.3, 5.4, 5.5_
        _Boundary: FaceRecognizer_
    - 対象ファイル: `src/Recognizer/FaceRecognizer.cs`(新規・static メソッドのみ), `tests/Recognizer.Tests/FaceRecognizerTests.cs`(新規)
    - 設計参照: design.md §6 FaceRecognizer(`CompareEmbeddings` 契約)、§7(ゼロベクトル → 0・Math.Clamp)
    - 検証コマンド: `dotnet test --filter "FullyQualifiedName~FaceRecognizerTests.CompareEmbeddings"`(同一=1・逆向き=-1・直交=0・次元不一致 → ArgumentException・ゼロベクトル → 0・クランプ、epsilon=1e-5)
  - [ ] 5.2 コンストラクタ(2 モデル判別・ガード・部分構築防止・Dispose)のテストと実装
        _Requirements: 2.4, 2.5, 2.7, 6.4, 6.5_
        _Boundary: FaceRecognizer_
        _Depends: 5.1, 2.1_
    - 対象ファイル: `src/Recognizer/FaceRecognizer.cs`(変更), `tests/Recognizer.Tests/FaceRecognizerTests.cs`(変更)
    - 設計参照: design.md §6 FaceRecognizer(コンストラクタ事前/事後条件・部分構築時の内包 detector 破棄)、§8(モデル不存在/ロード失敗/判別不能/null パス/Dispose 後)
    - 検証コマンド: `dotnet test --filter "FullyQualifiedName~FaceRecognizerTests"`(null パス → ArgumentNullException・不存在 → FileNotFoundException・判別不能 → NotSupportedException・Dispose 後の各メソッド → ObjectDisposedException)。要件 2.5(ロード失敗 → ORT 例外透過)は決定論的な破損 onnx fixture を要し再現が困難なため、実装は「包まず透過」に留め自動テスト対象外(防御的実装)とする — この扱いを完了条件とする
  - [ ] 5.3 ExtractEmbeddingAsync(Mat)パイプラインと分岐のテストと実装
        _Requirements: 3.1, 3.2, 3.3, 3.5, 3.6, 3.8, 3.9_
        _Boundary: FaceRecognizer_
        _Depends: 5.2, 3.1, 3.2, 4.1, 1.1_
    - 対象ファイル: `src/Recognizer/FaceRecognizer.cs`(変更), `tests/Recognizer.Tests/FaceRecognizerTests.cs`(変更)
    - 設計参照: design.md §4 システムフロー(単画像版)、§6 FaceRecognizer(同期ガード → 検出 → 切り出し → 前処理 → Run)、§8(未検出 → `(null, null)`)
    - 検証コマンド: `dotnet test --filter "FullyQualifiedName~FaceRecognizerTests"`(faceRegion 省略で検出・最高信頼度使用・Face 設定 / faceRegion 指定で検出スキップ・Face=null / 未検出 → Embedding・Face とも null(㉑ 黒画像) / Embedding 長 = 次元数(⑰) / 既定 0.7・0.5 / 閾値範囲外 → ArgumentException(faceRegion 指定時も) / faceRegion 空・非交差 → ArgumentException)
  - [ ] 5.4 CompareFacesAsync(Mat)パイプラインと Status 3 分岐のテストと実装
        _Requirements: 4.1, 4.2, 4.3, 4.4, 4.5, 4.6, 4.7_
        _Boundary: FaceRecognizer_
        _Depends: 5.3, 1.1_
    - 対象ファイル: `src/Recognizer/FaceRecognizer.cs`(変更), `tests/Recognizer.Tests/FaceRecognizerTests.cs`(変更)
    - 設計参照: design.md §4 システムフロー(逐次検出・画像 1 → 2)、§8(NoFaceInImage1/2)、§10(逐次検出の理由)
    - 検証コマンド: `dotnet test --filter "FullyQualifiedName~FaceRecognizerTests"`(単色 2 画像で解析的類似度=Success(⑰⑱) / 画像 1 未検出 → NoFaceInImage1・Similarity=0・Face1=null(両未検出も同) / 画像 2 未検出 → NoFaceInImage2(㉑ 白黒組合せ) / Face1/Face2 設定 / 既定 0.7・0.5 / 閾値範囲外 → ArgumentException)
  - [ ] 5.5 画像入力 3 形式のオーバーロード(path/bytes 委譲)と入力ガードのテストと実装
        _Requirements: 1.1, 1.2, 1.3, 1.4, 1.5, 1.6_
        _Boundary: FaceRecognizer_
        _Depends: 5.3, 5.4_
    - 対象ファイル: `src/Recognizer/FaceRecognizer.cs`(変更), `tests/Recognizer.Tests/FaceRecognizerTests.cs`(変更)
    - 設計参照: design.md §6 FaceRecognizer(オーバーロードは `ImageDecoder` で Mat 化して Mat 版へ委譲・所有 Mat 破棄)、§8(null/空/デコード不可)
    - 検証コマンド: `dotnet test --filter "FullyQualifiedName~FaceRecognizerTests"`(path/bytes と Mat の結果一致 / 不正パス・デコード不可・空 Mat → ArgumentException / null Mat(両画像分)・null imagePath → ArgumentNullException)
  - [ ] 5.6 キャンセル・並行呼び出しの公開契約テスト
        _Requirements: 6.1, 6.2, 6.3_
        _Boundary: FaceRecognizer_
        _Depends: 5.5_
    - 対象ファイル: `tests/Recognizer.Tests/FaceRecognizerTests.cs`(変更)
    - 設計参照: design.md §10(チェックポイント・セッション並行 Run 安全・ロックなし)、§6 FaceRecognizer(実装方針)
    - 検証コマンド: `dotnet test --filter "FullyQualifiedName~FaceRecognizerTests"`(キャンセル済みトークン → OperationCanceledException・同一インスタンスへの並行呼び出しが単独時と同一結果)

- [ ] 6. 公開 API サーフェスと非回帰の検証
  - [ ] 6.1 PublicApiTests の 9 型更新・追加 internal 型検査・全体 green 確認
        _Requirements: 7.1, 7.3, 7.4, 7.5_
        _Boundary: PublicApiSurface_
        _Depends: 5.5_
    - 対象ファイル: `tests/Recognizer.Tests/PublicApiTests.cs`(変更・公開型期待集合を 9 型へ・内部型検査に FaceCropper/EmbeddingPreprocessor/EmbeddingModelSpec 追加)
    - 設計参照: design.md §5 Traceability(7.1〜7.5)、§3 File Structure Plan(公開 4 型・internal 3 型)
    - 検証コマンド: `dotnet build` / `dotnet test`(既存 138 + 新規テストが終了コード 0・追加公開型は 4 型のみ・追加実装は internal・Console/ログなし・依存パッケージ不変)

## Implementation Notes

- 知識 port: `docs/dev/ports/` は存在せず注入なし。
- (1.1)埋め込み fixture の出力チャネル順は `[mean(R),mean(G),mean(B),1.0]`。fixture は入力チャネルをそのまま ReduceMean するため、**EmbeddingPreprocessor が BGR→RGB 変換して RGB 順で詰めること**を後続の C# テストで担保する必要がある(単色 (r,g,b) 入力 → 正規化 `(x−127.5)/128` の解析値と照合)。
- (1.1)㉓(D 動的)は入力を動的軸 `[1,3,'h','w']` にしないと ORT が Shape→Slice を定数畳み込みして D が静的化してしまう。判別上は 112 既定に落ちるため後続テストは無害。
- (1.1)㉑ `face_inputconf_f5` は conf=入力全体の ReduceMean(前処理 /255 後)。letterbox パディング(114)が混じらないよう、C# テストは **640×640 の正方単色画像**を使う(白=conf 1.0 検出 / 黒=0.0 未検出)。出力 `[1,5,6]`(N=6>F=5 で転置 `[1,5,N]` 判別)。
- (2.1)埋め込みモデルの入出力名は検出系と同じく `images` / `output`(`input` ではない)。後続の FaceRecognizer で入出力名を参照する際に留意。
- (2.1)分岐網羅のため rank1 出力 fixture ㉔ `embed_nchw_rank1_d4.onnx`(出力 rank1 `[4]`)を追加。`IntrospectEmbedding` は rank1 `[D]`/rank2 `[1,D]` 双方を受理(design (e-b))。既定サイズは `IntrospectInput` の引数化で検出=640・埋め込み=112 に分岐(既存検出挙動は不変)。
