# cli — 実装タスク

> 仕様の詳細は同じディレクトリの requirements.md(要件 ID)・design.md(節)を参照する。
> このファイルには仕様を転記しない。

## タスク一覧

- [x] 1. プロジェクト骨格とテスト基盤(以降の全タスクの土台)
  - [x] 1.1 CLI プロジェクトとテストプロジェクトを作成し、`Recognizer.sln` に登録する
        _Requirements: 1.1, 1.2, 1.4, 1.5, 1.7_
        _Boundary: Recognizer.Cli_
    - 対象ファイル: `src/Recognizer.Cli/Recognizer.Cli.csproj`(新規)、`tests/Recognizer.Cli.Tests/Recognizer.Cli.Tests.csproj`(新規)、`Recognizer.sln`(変更)
    - 設計参照: design.md §3 File Structure Plan、§10.1(publish 設定は 7.1 で仕上げる。ここでは net10.0 / Exe / nullable / ProjectReference / `System.CommandLine` 2.0.9 / `InternalsVisibleTo` まで)
    - 検証コマンド: `dotnet build`
  - [x] 1.2 既存 Fixtures のリンク参照とテストヘルパーを用意する
        _Requirements: 8.4, 8.5_
        _Boundary: Recognizer.Cli.Tests_
        _Depends: 1.1_
    - 対象ファイル: `tests/Recognizer.Cli.Tests/Recognizer.Cli.Tests.csproj`(変更)、`tests/Recognizer.Cli.Tests/CliTestHost.cs`(新規)
    - 設計参照: design.md §9.1(インプロセス実行)、§9.2(リンク参照・パス解決・一時画像の生成)
    - 検証コマンド: `dotnet test --filter FullyQualifiedName~Recognizer.Cli.Tests`(Fixtures が出力ディレクトリに配置されることを検証するテストを 1 件書く)
  - [x] 1.3 既存テストの非回帰を確認する
        _Requirements: 1.3, 1.6_
        _Boundary: Recognizer.Cli_
        _Depends: 1.1_
    - 対象ファイル: なし(確認のみ。`src/Recognizer` を一切変更していないことを含めて確認する)
    - 設計参照: design.md §2 既存システムの分析(3)、§5 の 1.3 / 1.6 行
    - 検証コマンド: `dotnet test`(既存 224 件が成功すること。`git diff --stat src/Recognizer/` が空であること)

- [x] 2. JSON 出力基盤(出力 DTO とシリアライズ)
  - [x] 2.1 出力 DTO とソース生成コンテキスト、シリアライズ設定を実装する
        _Requirements: 6.2, 6.3, 6.4_
        _Boundary: Output_
        _Depends: 1.2_
    - 対象ファイル: `src/Recognizer.Cli/Output/OutputDtos.cs`(新規)、`src/Recognizer.Cli/Output/CliJsonContext.cs`(新規)、`src/Recognizer.Cli/Output/CliJson.cs`(新規)、`tests/Recognizer.Cli.Tests/JsonOutputTests.cs`(新規)
    - 設計参照: design.md §7 データモデル(**プロパティ名は `Bbox`。`BBox` は `"bBox"` になる**。ソース生成は `JsonSerializerOptions` 経由で結線する)
    - 検証コマンド: `dotnet test --filter FullyQualifiedName~JsonOutputTests`(キーが `bbox` であること・1 行であること・`status` が列挙子名であること・カルチャ非依存であることを RED→GREEN で固定する)

- [ ] 3. エラー処理基盤と CLI 制御フロー(最もリスクが高い。コマンドより先に固める)
  - [ ] 3.1 終了コード・code 定数・内部例外・実行時エラーのマッピングを実装する
        _Requirements: 7.1, 7.3, 7.7_
        _Boundary: Errors_
        _Depends: 2.1_
    - 対象ファイル: `src/Recognizer.Cli/ExitCodes.cs`(新規)、`src/Recognizer.Cli/Errors/ErrorCodes.cs`(新規)、`src/Recognizer.Cli/Errors/CliRuntimeException.cs`(新規)、`src/Recognizer.Cli/Errors/RuntimeErrorMapper.cs`(新規)、`tests/Recognizer.Cli.Tests/ErrorHandlingTests.cs`(新規)
    - 設計参照: design.md §8.1(例外 → code の対応表と評価順序)、§8.3(終了コード)
    - 検証コマンド: `dotnet test --filter FullyQualifiedName~ErrorHandlingTests`
  - [ ] 3.2 閾値オプション(数値変換 + 値域検証)を実装する
        _Requirements: 2.6_
        _Boundary: Commands_
        _Depends: 3.1_
    - 対象ファイル: `src/Recognizer.Cli/Commands/ThresholdOption.cs`(新規)、`src/Recognizer.Cli/Errors/UsageErrorCollector.cs`(新規)、`tests/Recognizer.Cli.Tests/ErrorHandlingTests.cs`(変更)
    - 設計参照: design.md §6 ThresholdOption(**`Validators` は使わない。`CustomParser` を使う**。値域判定は `!(v >= 0f && v <= 1f)` と書く = NaN を弾く)
    - 検証コマンド: `dotnet test --filter FullyQualifiedName~ErrorHandlingTests`(`abc` / `1.5` / `NaN` / 値なし の 4 分岐をそれぞれテストする)
  - [ ] 3.3 使用法エラーの構造的分類を実装する
        _Requirements: 2.4, 2.5, 7.8_
        _Boundary: Errors_
        _Depends: 3.2_
    - 対象ファイル: `src/Recognizer.Cli/Errors/UsageErrorClassifier.cs`(新規)、`tests/Recognizer.Cli.Tests/ErrorHandlingTests.cs`(変更)
    - 設計参照: design.md §8.2(判定順序 1〜8。英語メッセージの文字列一致に依存しない)
    - 検証コマンド: `dotnet test --filter FullyQualifiedName~ErrorHandlingTests`(§8.2 の 8 行それぞれを 1 ケース以上で覆う)
  - [ ] 3.4 CliApplication と Program(制御フロー・エラー JSON の書き出し)を実装する
        _Requirements: 2.7, 6.1, 7.2, 7.3_
        _Boundary: CliApplication_
        _Depends: 3.3_
    - 対象ファイル: `src/Recognizer.Cli/CliApplication.cs`(新規)、`src/Recognizer.Cli/Program.cs`(新規)、`tests/Recognizer.Cli.Tests/ErrorHandlingTests.cs`(変更)
    - 設計参照: design.md §6 CliApplication(契約)、§4 システムフロー、§8(`EnableDefaultExceptionHandler = false`。パースエラー時は `InvokeAsync` を呼ばない)
    - 検証コマンド: `dotnet test --filter FullyQualifiedName~Recognizer.Cli.Tests`(`--help` が終了コード 0・stderr 空であること、エラー時に stdout が空であることを含む。**この時点ではコマンドが 1 つも登録されていないため help の一覧は空でよい**。3 コマンドが列挙されることの検証は 6.2 で行う)

- [ ] 4. detect-face コマンド(最初の end-to-end スライス)
  - [ ] 4.1 detect-face の正常系(JSON 出力・ランドマーク有無・検出 0 件)を実装する
        _Requirements: 2.1, 2.2, 2.3, 3.1, 3.2, 3.3, 3.4, 3.5, 3.6, 3.7, 6.5, 7.9, 8.1, 8.3_
        _Boundary: Commands_
        _Depends: 3.4_
    - 対象ファイル: `src/Recognizer.Cli/Commands/DetectFaceCommand.cs`(新規)、`src/Recognizer.Cli/CliApplication.cs`(変更: コマンド登録)、`src/Recognizer.Cli/Output/OutputDtos.cs`(変更: 変換の追加)、`tests/Recognizer.Cli.Tests/DetectFaceCommandTests.cs`(新規)
    - 設計参照: design.md §5 の 3.1〜3.7 行、§9.3(Fixture と期待値: `face_nchw_transposed_f5`(3 件・landmarks は null)/ `face_nchw_transposed_f20`(landmarks あり)/ `face_inputconf_f5` + 黒画像(0 件))
    - 検証コマンド: `dotnet test --filter FullyQualifiedName~DetectFaceCommandTests`(`--confidence` の既定値 0.7 が適用されることを含む)
  - [ ] 4.2 実行時エラー(画像・モデル)の分岐を実コマンド経路で検証する
        _Requirements: 7.4, 7.5, 8.2_
        _Boundary: Errors_
        _Depends: 4.1_
    - 対象ファイル: `tests/Recognizer.Cli.Tests/ErrorHandlingTests.cs`(変更)
    - 設計参照: design.md §8.1(`imageLoadFailed` / `modelNotFound` / `modelLoadFailed` / `unsupportedModelFormat`)
    - 検証コマンド: `dotnet test --filter FullyQualifiedName~ErrorHandlingTests`(画像不在・非画像ファイル・モデル不在・壊れた ONNX・`face_unsupported_f7.onnx` の各分岐を個別にテストする)

- [ ] 5. detect-object コマンド(クラス名解決を含む)
  - [ ] 5.1 detect-object の正常系(既定のクラス名解決・検出 0 件)を実装する
        _Requirements: 2.1, 2.2, 2.3, 4.1, 4.2, 4.3, 4.5, 4.7, 4.8, 8.1_
        _Boundary: Commands_
        _Depends: 4.1_
    - 対象ファイル: `src/Recognizer.Cli/Commands/DetectObjectCommand.cs`(新規)、`src/Recognizer.Cli/CliApplication.cs`(変更: コマンド登録)、`src/Recognizer.Cli/Output/OutputDtos.cs`(変更)、`tests/Recognizer.Cli.Tests/DetectObjectCommandTests.cs`(新規)
    - 設計参照: design.md §5 の 4.1〜4.8 行、§9.3(`object_nchw_transposed_4c3`(3 件・信頼度降順)/ `object_transposed_coco80`(COCO 名))
    - 検証コマンド: `dotnet test --filter FullyQualifiedName~DetectObjectCommandTests`(**`--confidence` の既定値が 0.5**(detect-face の 0.7 ではない)であることを検証する)
  - [ ] 5.2 `--classes` によるクラス名解決と読み込み失敗の分岐を実装する
        _Requirements: 4.4, 4.6, 7.6_
        _Boundary: Commands_
        _Depends: 5.1_
    - 対象ファイル: `src/Recognizer.Cli/Commands/ClassNamesFile.cs`(新規)、`src/Recognizer.Cli/Commands/DetectObjectCommand.cs`(変更: `--classes` オプションの追加と `classNames` の受け渡し)、`tests/Recognizer.Cli.Tests/DetectObjectCommandTests.cs`(変更)、`tests/Recognizer.Cli.Tests/ErrorHandlingTests.cs`(変更)
    - 設計参照: design.md §6 ClassNamesFile(契約と異常系。**素の `FileNotFoundException` を漏らさない** = `modelNotFound` と誤判定される)、§8.1 の順 1
    - 検証コマンド: `dotnet test --filter FullyQualifiedName~Recognizer.Cli.Tests`(クラス名の解決・行数不一致でエラーにしないこと・ファイル不在が `classesFileNotFound` になりモデル不在と区別されることを個別にテストする)

- [ ] 6. compare-face コマンドと、3 コマンド出揃い後の使用法エラーの実経路検証
  - [ ] 6.1 compare-face の 3 つの status(Success / NoFaceInImage1 / NoFaceInImage2)を実装する
        _Requirements: 2.1, 2.2, 2.3, 5.1, 5.2, 5.3, 5.4, 5.5, 5.6, 5.7, 5.8, 8.1, 8.3_
        _Boundary: Commands_
        _Depends: 5.1_
    - 対象ファイル: `src/Recognizer.Cli/Commands/CompareFaceCommand.cs`(新規)、`src/Recognizer.Cli/CliApplication.cs`(変更: コマンド登録)、`src/Recognizer.Cli/Output/OutputDtos.cs`(変更)、`tests/Recognizer.Cli.Tests/CompareFaceCommandTests.cs`(新規)
    - 設計参照: design.md §5 の 5.1〜5.8 行(**NoFaceInImage1 では face1 / face2 とも null**)、§9.3(`face_inputconf_f5` + `embed_nchw_meanrgb_d4`、白/黒画像で status を作り分ける)
    - 検証コマンド: `dotnet test --filter FullyQualifiedName~CompareFaceCommandTests`(3 つの status を個別にテストする。`--detection-threshold` の既定値 0.7 を含む)
  - [ ] 6.2 使用法エラーと `--help` を実コマンド経路で検証する(3 コマンドが出揃った後にしか成立しない検証)
        _Requirements: 2.4, 2.5, 2.6, 2.7, 8.2_
        _Boundary: Errors_
        _Depends: 6.1_
    - 対象ファイル: `tests/Recognizer.Cli.Tests/ErrorHandlingTests.cs`(変更)
    - 設計参照: design.md §8.2(判定順序 8 行)、§9.3 の「閾値がライブラリに渡らないこと」「`--help`」の各行
    - 検証コマンド: `dotnet test --filter FullyQualifiedName~ErrorHandlingTests`。次を実コマンドで検証する: (a) 3 コマンドそれぞれの必須オプション欠落(`--model` / `--detector-model` / `--embedding-model`)、(b) **`--confidence 1.5` を存在しないモデルパスと併用しても終了コード 2(使用法エラー)であり 1(モデル不在)にならないこと = ライブラリを呼んでいない**(要件 2.6)、(c) 位置引数の過不足・未知のコマンド/オプション、(d) `--help` が終了コード 0 で 3 コマンドを列挙すること

- [ ] 7. 配布(publish)と CI
  - [ ] 7.1 publish 設定を仕上げ、linux-x64 の実バイナリでスモーク検証する
        _Requirements: 9.1, 9.2, 9.3_
        _Boundary: Recognizer.Cli_
        _Depends: 6.1_
    - 対象ファイル: `src/Recognizer.Cli/Recognizer.Cli.csproj`(変更)
    - 設計参照: design.md §10.1(publish 設定の表。トリミングはしない)、§9.4(スモーク検証の手順。結果を Implementation Notes に記録する)
    - 検証コマンド: `dotnet publish src/Recognizer.Cli/Recognizer.Cli.csproj -c Release -r linux-x64 -o /tmp/cli-publish` の後、生成された実行ファイルで 3 コマンドとエラー 2 系統(実行時・使用法)を実行し、JSON と終了コード(0 / 1 / 2)を確認する
  - [ ] 7.2 CI に CLI の publish ステップを追加する
        _Requirements: 9.2_
        _Boundary: CI_
        _Depends: 7.1_
    - 対象ファイル: `.github/workflows/ci.yml`(変更)
    - 設計参照: design.md §10.2(`dotnet test` は変更不要。publish のみ 1 行追加)
    - 検証コマンド: YAML の妥当性を `python3 -c "import yaml; yaml.safe_load(open('.github/workflows/ci.yml'))"` で確認し、追加したステップと同じコマンド(`dotnet publish src/Recognizer.Cli/Recognizer.Cli.csproj -c Release -r linux-x64 -o /tmp/ci-check`)がローカルで成功することを確認する。3 プラットフォームの実機検証は CI に委ねる

- [ ] 8. 恒久情報への反映
  - [ ] 8.1 README に CLI の使い方を追記する
        _Requirements: 10.1, 10.2, 10.3_
        _Boundary: Docs_
        _Depends: 7.1_
    - 対象ファイル: `README.md`(変更)
    - 設計参照: design.md §8(code の対応表と終了コードを転記する)、§10.1(publish 手順)
    - 検証コマンド: 記載した各コマンド例を実際に CLI で実行し、README の出力例・終了コードが実物と一致することを確認する
  - [ ] 8.2 (P) api-spec と CLAUDE.md を更新する
        _Requirements: 10.4, 10.5_
        _Boundary: Docs_
        _Depends: 7.1_
    - 対象ファイル: `docs/api-spec.md`(変更)、`CLAUDE.md`(変更)
    - 設計参照: design.md §3 File Structure Plan の該当行(8.1 と対象ファイルが重ならないため並行可)
    - 検証コマンド: `dotnet test`(ドキュメント更新が既存テストに影響しないことの最終確認)

## Implementation Notes

- 知識 port: `docs/dev/ports` は存在せず、注入なしで進行(`ports.py --skill dev-implement` の走査結果)。プロジェクト規約は `CLAUDE.md` に従う。
- タスク 1.3(非回帰確認)はメイン文脈で実測: 既存 224 件グリーン、`git diff --stat HEAD -- src/Recognizer/` および `git status --porcelain src/Recognizer/` が空(要件 1.3 / 1.6 の成立)。
- `InternalsVisibleTo("Recognizer.Cli.Tests")` は実機能している。top-level statements が生成する `Program` は internal で、これを外すと `CS0122` でコンパイルが落ちることをレビュアーが falsification で確認済み。
- テストで `GetReferencedAssemblies` による ProjectReference 検査を使わないこと(Roslyn は未使用の参照をマニフェストに残さず偽陰性になる)。コンパイル時の型解決で担保する。
- `CliTestHost`(tests/Recognizer.Cli.Tests)が提供するヘルパー: `FixturePath` / `CreateWhiteImage` / `CreateBlackImage` / `CreateNonImageFile(ext)` / `CreateClassNamesFile(...)` / `NonExistentPath(ext)`。インスタンスごとに GUID 隔離した一時ディレクトリを持ち `Dispose` で削除する(並行実行で衝突しない)。
- **タスク 3.4 で `CliTestHost` に `RunCliAsync` を追加すること**(`CliApplication.RunAsync` を `StringWriter` 2 本で呼び `(exitCode, stdout, stderr)` を返す薄いラッパー。design §9.1)。あわせて 1.1 が置いた暫定 `ProjectSetupTests.cs` を削除/吸収する。
- `face_inputconf_f5.onnx` + 白画像 → 検出あり / 黒画像 → 未検出 を実測で確認済み(research §4 のとおり)。
- 要件 8.5 のため、パス組み立ては必ず `Path.Combine` を使い区切り文字をハードコードしない。
- **JSON の結線**(タスク 2.1): `CliJson.Options` に `Encoder = JavaScriptEncoder.Create(UnicodeRanges.All)` を追加した(design §7 に反映済み)。既定エンコーダだと日本語のエラーメッセージが `\uXXXX` にエスケープされ、要件 7.1 の「人間可読なメッセージ」が端末で読めなくなるため。`<` `>` `&` と U+2028/U+2029 はエスケープされ続けるので安全性は後退しない(レビュアーが実測)。
- 後続コマンドが使う API: `CliJson.Write(TextWriter, T)` が 1 行 JSON + 末尾改行 1 個を書く。DTO 生成は `DetectFaceOutput.From(image, faces)` / `DetectObjectOutput.From(image, objects)` / `CompareFaceOutput.From(image1, image2, result)` を使い、**コマンド側に変換ロジックを書かない**(design §7「ロジックの所在」)。ライブラリ側は `BBox`、CLI DTO 側は `Bbox`。変換は `From(...)` 内に閉じている。
- **`code` 文字列は camelCase**(`imageLoadFailed` / `modelNotFound` 等。design §8.1・§8.2 が正本)。SCREAMING_SNAKE にしない。
- **既知の制約(タスク 3.4 / 7.1 で対応を検討)**: 出力に生の非 ASCII が乗るため、Windows コンソール(コードページが UTF-8 でない場合)で日本語メッセージが文字化けし得る。`Program.cs` で `Console.OutputEncoding = Encoding.UTF8` を設定するのが対策。テストはインプロセスの `StringWriter` を使うためこの経路を捕捉しない。
