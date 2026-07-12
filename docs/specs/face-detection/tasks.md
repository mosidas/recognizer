# face-detection — 実装タスク

> 仕様の詳細は同じディレクトリの requirements.md(要件 ID)・design.md(節)を参照する。
> このファイルには仕様を転記しない。

## タスク一覧

- [ ] 1. プロジェクト骨格とテスト fixture 基盤
  - [x] 1.1 ソリューションと 2 プロジェクト(src/Recognizer、tests/Recognizer.Tests)の作成
        _Requirements: 5.4, 5.5_
        _Boundary: Solution_
    - 対象ファイル: `Recognizer.sln`(新規), `src/Recognizer/Recognizer.csproj`(新規), `tests/Recognizer.Tests/Recognizer.Tests.csproj`(新規)
    - 設計参照: design.md §2 技術スタック、§3 File Structure Plan(nullable 有効・`InternalsVisibleTo("Recognizer.Tests")`・依存 4 パッケージ限定)
    - 検証コマンド: `dotnet build` / `dotnet test`(空テストで終了コード 0)
  - [x] 1.2 fixture 生成スクリプトと 6 種のダミー ONNX fixture の生成・コミット
        _Requirements: 2.1, 2.2, 2.3, 2.6_
        _Boundary: TestFixtures_
        _Depends: 1.1_
    - 対象ファイル: `tools/generate_test_models.py`(新規), `tests/Recognizer.Tests/Fixtures/*.onnx`(生成物 6 種・新規), `tests/Recognizer.Tests/Fixtures/README.md`(新規)
    - 設計参照: design.md §9(fixture 一覧①〜⑥・定数出力の構成)、research.md §3(venv での生成手順)
    - 検証コマンド: `python3 -m venv /tmp/onnx-venv && /tmp/onnx-venv/bin/pip install onnx && /tmp/onnx-venv/bin/python tools/generate_test_models.py` の後、6 ファイルの存在と各サイズが 100 KB 未満であること

- [ ] 2. 純粋計算部品(テストファースト)
  - [x] 2.1 (P) NonMaxSuppression(IoU 計算・貪欲 NMS)のテストと実装
        _Requirements: 3.2, 3.3_
        _Boundary: NonMaxSuppression_
        _Depends: 1.1_
    - 対象ファイル: `src/Recognizer/Internal/NonMaxSuppression.cs`(新規), `tests/Recognizer.Tests/NonMaxSuppressionTests.cs`(新規)
    - 設計参照: design.md §6 NonMaxSuppression(信頼度降順維持・単一クラス適用)
    - 検証コマンド: `dotnet test --filter "FullyQualifiedName~NonMaxSuppressionTests"`
  - [x] 2.2 (P) Letterbox(パラメータ計算・座標逆変換・画像境界クリップ)のテストと実装
        _Requirements: 3.5_
        _Boundary: Letterbox_
        _Depends: 1.1_
    - 対象ファイル: `src/Recognizer/Internal/Letterbox.cs`(新規), `tests/Recognizer.Tests/LetterboxTests.cs`(新規)
    - 設計参照: design.md §6 Preprocessor / Letterbox(逆変換式・クリップ範囲)
    - 検証コマンド: `dotnet test --filter "FullyQualifiedName~LetterboxTests"`

- [ ] 3. 画像入力
  - [x]* 3.1 (P) ImageDecoder(パス/バイト列 → Mat 解決と入力ガード)の実装
        _Requirements: 1.2, 1.3, 1.4, 1.5, 1.6_
        _Boundary: ImageDecoder_
        _Depends: 1.1_
    - 対象ファイル: `src/Recognizer/Internal/ImageDecoder.cs`(新規)
    - 設計参照: design.md §6 FaceDetector(事前条件)、§8 エラーハンドリング。契約テストは公開 API 経由で 6.2 / 6.3 が担う(design.md §9 のテスト戦略。専用テストファイルは File Structure Plan に置かない)
    - 検証コマンド: `dotnet build`
- [ ] 4. モデル形式の自動判別
  - [x] 4.1 TensorLayout / DetectionModelSpec / ModelIntrospector のテストと実装
        _Requirements: 2.1, 2.2, 2.3, 2.6_
        _Boundary: ModelIntrospector_
        _Depends: 1.2_
    - 対象ファイル: `src/Recognizer/Internal/TensorLayout.cs`(新規), `src/Recognizer/Internal/DetectionModelSpec.cs`(新規), `src/Recognizer/Internal/ModelIntrospector.cs`(新規), `tests/Recognizer.Tests/ModelIntrospectorTests.cs`(新規)
    - 設計参照: design.md §6 ModelIntrospector 判別規則 (a)〜(f) と責務分担(fixture ①〜⑥で NCHW/NHWC・動的軸 640 既定・転置/標準・F=5/20・非対応 F=7 の各分岐を個別テスト)
    - 検証コマンド: `dotnet test --filter "FullyQualifiedName~ModelIntrospectorTests"`

- [ ] 5. 前処理と出力パース
  - [x]* 5.1 (P) Preprocessor(letterbox・BGR→RGB・正規化・NCHW/NHWC テンソル化)の実装
        _Requirements: 2.1, 2.2_
        _Boundary: Preprocessor_
        _Depends: 2.2, 4.1_
    - 対象ファイル: `src/Recognizer/Internal/Preprocessor.cs`(新規)
    - 設計参照: design.md §6 Preprocessor / Letterbox。契約検証は fixture パイプライン経由で 6.2 が担う(design.md §9)
    - 検証コマンド: `dotnet build`
  - [x]* 5.2 (P) FaceOutputParser(形式吸収・閾値フィルタ・ランドマーク読み出し)と FaceLandmarks の実装
        _Requirements: 2.3, 3.1, 3.6, 3.7_
        _Boundary: FaceOutputParser_
        _Depends: 4.1_
    - 対象ファイル: `src/Recognizer/Internal/FaceOutputParser.cs`(新規。internal 候補 struct を同居), `src/Recognizer/FaceLandmarks.cs`(新規)
    - 設計参照: design.md §6 FaceOutputParser、§7 データモデル。契約検証は 6.2 が担う(design.md §9)
    - 検証コマンド: `dotnet build`

- [ ] 6. FaceDetector 統合(垂直スライス完成)
  - [ ] 6.1 コンストラクタ(ガード・モデルロード・形式判別)と Dispose のテストと実装
        _Requirements: 2.1, 2.4, 2.5, 2.6, 2.7, 4.4_
        _Boundary: FaceDetector_
        _Depends: 1.2, 4.1_
    - 対象ファイル: `src/Recognizer/FaceDetector.cs`(新規), `tests/Recognizer.Tests/FaceDetectorTests.cs`(新規)
    - 設計参照: design.md §6 FaceDetector(コンストラクタ契約)、§8(null / FileNotFound / ORT 透過 / NotSupported の各分岐を個別テスト)
    - 検証コマンド: `dotnet test --filter "FullyQualifiedName~FaceDetectorTests"`
  - [ ] 6.2 DetectAsync(Mat)のパイプライン統合と FaceDetection のテストと実装
        _Requirements: 1.1, 1.5, 3.1, 3.2, 3.3, 3.4, 3.5, 3.6, 3.7, 3.8, 3.9_
        _Boundary: FaceDetector_
        _Depends: 2.1, 2.2, 5.1, 5.2, 6.1_
    - 対象ファイル: `src/Recognizer/FaceDetector.cs`(変更), `src/Recognizer/FaceDetection.cs`(新規), `tests/Recognizer.Tests/FaceDetectorTests.cs`(変更)
    - 設計参照: design.md §4 システムフロー、§6 FaceDetector(DetectAsync 契約)。fixture ①②③④で閾値フィルタ・NMS・降順・空リスト・座標復元・ランドマーク有無・既定値、空 Mat / 閾値範囲外の異常系を個別テスト
    - 検証コマンド: `dotnet test --filter "FullyQualifiedName~FaceDetectorTests"`
  - [ ] 6.3 パス / バイト列オーバーロードのテストと実装
        _Requirements: 1.2, 1.3, 1.4, 1.6_
        _Boundary: FaceDetector_
        _Depends: 3.1, 6.2_
    - 対象ファイル: `src/Recognizer/FaceDetector.cs`(変更), `tests/Recognizer.Tests/FaceDetectorTests.cs`(変更)
    - 設計参照: design.md §5(1.2〜1.4, 1.6)、§8(不正パス・デコード不可・null の各分岐を個別テスト。Mat 版との結果同一性を含む)
    - 検証コマンド: `dotnet test --filter "FullyQualifiedName~FaceDetectorTests"`
  - [ ] 6.4 非同期契約(CancellationToken・キャンセル・並行呼び出し・Dispose 後)のテストと実装
        _Requirements: 4.1, 4.2, 4.3, 4.5_
        _Boundary: FaceDetector_
        _Depends: 6.2_
    - 対象ファイル: `src/Recognizer/FaceDetector.cs`(変更), `tests/Recognizer.Tests/FaceDetectorTests.cs`(変更)
    - 設計参照: design.md §6 FaceDetector(実装方針: Task.Run・ConfigureAwait(false)・チェックポイント)、§10 キャンセルの制約(キャンセル済みトークン / 並行 Task.WhenAll 同一結果 / Dispose 後の各分岐を個別テスト)
    - 検証コマンド: `dotnet test --filter "FullyQualifiedName~FaceDetectorTests"`

- [ ] 7. 公開面の検査と仕上げ
  - [ ] 7.1 公開 API 面と非機能のテスト
        _Requirements: 5.1, 5.2, 5.3, 5.4_
        _Boundary: FaceDetector_
        _Depends: 6.3, 6.4_
    - 対象ファイル: `tests/Recognizer.Tests/FaceDetectorTests.cs`(変更。リフレクションで公開型が 3 型のみであることを検査), `src/Recognizer/Recognizer.csproj`(検査対象)
    - 設計参照: design.md §5(5.1〜5.4)
    - 検証コマンド: `dotnet test` に加え、`grep -rn "Console\." src/Recognizer/` がヒット 0 件(5.3)、`grep -c "<PackageReference" src/Recognizer/Recognizer.csproj` が design.md §2 の 4 パッケージ以下(5.4)
  - [ ] 7.2 最終検証とドキュメント反映
        _Requirements: 5.5_
        _Boundary: Solution_
        _Depends: 7.1_
    - 対象ファイル: `README.md`(変更。ビルド・テスト・fixture 再生成手順の追記。File Structure Plan 外のドキュメント反映)
    - 設計参照: design.md §9(fixture 再生成手順)、§5(5.5)
    - 検証コマンド: `dotnet build && dotnet test`(いずれも終了コード 0)

## Implementation Notes

- 知識 port: 注入なし(`docs/dev/ports/` が存在しないため。`ports.py --skill dev-implement` で確認)。準拠規約は CLAUDE.md の .NET コーディング規約と design.md の契約。
- fixture(タスク 1.2): 入力名 `images`・出力名 `output`・opset 17・N=6。既定閾値(0.7/0.5)での期待結果は A(0.95)→B(0.85)→D(0.75)。期待値の正本は `tests/Recognizer.Tests/Fixtures/README.md`。座標は 640x640 レターボックス空間・中心形式。F=20 のランドマーク点 conf は 0.99(C# 側は破棄)。
- fixture の再生成はバイト一致で再現する(/tmp/onnx-venv の python で tools/generate_test_models.py 実行)。
- NMS API(タスク 2.1): `NonMaxSuppression.Apply(IReadOnlyList<(RectangleF Box, float Confidence)>, float nmsThreshold)` → 採用候補の元配列インデックスを信頼度降順で返す。抑制は IoU > 閾値(等値は残す)。`IntersectionOverUnion(RectangleF, RectangleF)` も公開(internal)。
- Letterbox API(タスク 2.2): `LetterboxParams(float Scale, float PadX, float PadY)` record。`Create(srcW, srcH, inW, inH)` / `InverseTransform(PointF|RectangleF)` / `ClampToBounds(PointF|RectangleF, width, height)`(逆変換とクリップは独立。合成順は Clamp(Inverse(x)))。中心形式→左上形式変換は FaceOutputParser の責務。
- ImageDecoder API(タスク 3.1): `EnsureValid(Mat)`(所有権非移動)/ `DecodeFile(string)` / `DecodeBytes(ReadOnlyMemory<byte>)` — 返却 Mat は呼び出し側が破棄(using)。paramName は CallerArgumentExpression で自動捕捉。ImRead/ImDecode は失敗時に空 Mat を返す(空 Span のみ例外のため先行ガード済み)。
- 環境: libOpenCvSharpExtern.so は GTK に動的リンクする。devcontainer の Dockerfile に `libgtk-3-0` を追記済み(コンテナ再ビルドで恒久化。現コンテナには導入済み)。
- ModelIntrospector API(タスク 4.1): `Introspect(InferenceSession)` → `DetectionModelSpec { Layout, InputWidth, InputHeight, InputName, OutputName }`(出力形式は保持しない)。`ClassifyOutput(ReadOnlySpan<int>)` → `OutputSpec(Format: Transposed|Standard, FeatureCount, CandidateCount)` — Run 後の実形状での再判定に使う純粋関数(DenseTensor<float>.Dimensions を渡せる)。非対応は NotSupportedException。
- ORT 実挙動: NodeMetadata.Dimensions は int[]、動的軸は -1。fixture は計 11 種(⑦〜⑪は非対応分岐の検証用)。テストの fixture パスは AppContext.BaseDirectory + "Fixtures"(csproj で出力へコピー)。
- レビュー教訓(4.1): 異常系ガードは 1 ガード 1 テストで独立に検証する(ガード単位の退行を捕捉するため)。後続タスクも同方針で。
- Preprocessor API(5.1): `Preprocess(Mat, DetectionModelSpec)` → `(DenseTensor<float> Tensor, LetterboxParams Params)`。RGB 順・[0,1] 正規化。null/空の再検査はしない(上流 ImageDecoder が担保)。
- FaceOutputParser API(5.2): `Parse(Tensor<float> output, float confidenceThreshold)` → `IReadOnlyList<FaceCandidate(Box, Confidence, Landmarks?)>`。出力はレターボックス空間・左上形式・閾値フィルタ済み。NMS・逆変換・クリップ・降順整列・FaceDetection 生成は 6.2 の責務。
