# multi-platform — 実装タスク

> 仕様の詳細は同じディレクトリの requirements.md(要件 ID)・design.md(節)を参照する。
> このファイルには仕様を転記しない。

## タスク一覧

- [ ] 1. 依存パッケージの追加と依存検査テストの更新 (P)
  - [x] 1.1 `PublicApiTests` の依存パッケージ許可リストを 5 パッケージへ更新する(TDD の RED: この時点では csproj が 3 パッケージのため失敗する)。テスト名の「3つ」も実数へ改名する
        _Requirements: 4.2, 4.3, 7.1_
        _Boundary: Recognizer.Tests_
    - 対象ファイル: `tests/Recognizer.Tests/PublicApiTests.cs`(変更)
    - 設計参照: design.md §2.4(5 パッケージの一覧), §3(File Structure Plan), §9(テスト戦略)
    - 検証コマンド: `dotnet test --filter "FullyQualifiedName~PublicApiTests"`(このタスク単体では RED になることを確認する)
  - [x] 1.2 `Recognizer.csproj` に `OpenCvSharp4.runtime.win` と `Sdcb.OpenCvSharp4.mini.runtime.osx-arm64` を無条件 `PackageReference` として追加する(GREEN)。why コメントで「なぜ osx-arm64 だけサードパーティか」を残す
        _Requirements: 1.1, 1.3, 4.1, 4.2, 7.1_
        _Boundary: Recognizer.csproj_
        _Depends: 1.1_
    - 対象ファイル: `src/Recognizer/Recognizer.csproj`(変更)
    - 設計参照: design.md §2.4(パッケージ表・バージョン), §6(csproj の契約), §10.1(サードパーティ依存の撤退条件)
    - 検証コマンド: `dotnet build && dotnet test`(224 件全 green・Failed 0 / Skipped 0)
  - [x] 1.3 3 RID すべてで `dotnet publish` を実行し、OpenCvSharp のネイティブ資産が当該 RID のもののみ配置されることを確認する(手動検証。テストコード化はしない)
        _Requirements: 1.2, 1.4, 4.5_
        _Boundary: Recognizer.csproj_
        _Depends: 1.2_
    - 対象ファイル: なし(検証のみ。成果物は確認ログ)
    - 設計参照: design.md §2.4, §9(テスト戦略), §10.3(ORT 由来の `onnxruntime.dll` 混入は検証対象外)
    - 検証コマンド: `for rid in linux-x64 win-x64 osx-arm64; do dotnet publish src/Recognizer/Recognizer.csproj -c Release -r $rid -o /tmp/pub-$rid; done` の後、各出力に当該 RID の OpenCvSharp native(`libOpenCvSharpExtern.so` / `OpenCvSharpExtern.dll` / `libOpenCvSharpExtern.dylib`)が 1 つだけ存在し、他 RID の OpenCvSharp native が無いことを確認する

- [ ] 2. CI ワークフローの新設
  - [x] 2.1 (P) GitHub Actions のワークフローを作成する(3 プラットフォームのマトリクス、`fail-fast: false`、.NET 10 SDK セットアップ、build → test → RID 別 publish)
        _Requirements: 5.1, 5.2, 5.3, 5.4, 5.5, 5.6_
        _Boundary: CI_
    - 対象ファイル: `.github/workflows/ci.yml`(新規)
    - 設計参照: design.md §4(CI 設計。YAML の全文と各設定の根拠)
    - 検証コマンド: `pip3 install --break-system-packages -q pyyaml && python3 -c "import yaml; d=yaml.safe_load(open('.github/workflows/ci.yml')); assert d['jobs']['test']['strategy']['fail-fast'] is False; print('OK', [m['os'] for m in d['jobs']['test']['strategy']['matrix']['include']])"`(構文が妥当で、`fail-fast: false` と 3 ランナーが定義されていること。devcontainer に PyYAML は未導入のため導入を含める)
  - [x] 2.2 ワークフローの `run:` コマンドがローカルの `dotnet build` / `dotnet test` と整合することを確認する(GitHub Actions 自体は devcontainer で実行できないため、ローカル検証はここまで)
        _Requirements: 5.7_
        _Boundary: CI_
        _Depends: 2.1, 1.2_
    - 対象ファイル: なし(検証のみ)
    - 設計参照: design.md §4(`--no-build` は直前の build と同一 configuration であること), §9
    - 検証コマンド: `dotnet build --configuration Release && dotnet test --configuration Release --no-build`(CI と同一のコマンド列がローカルで通ること。`_Depends: 1.2_` があるのは、タスク 1 が RED 状態(1.1 完了・1.2 未完了)のときにこのコマンドが CI と無関係な理由で失敗するため)

- [ ] 3. 恒久情報の更新
  - [ ] 3.1 `docs/api-spec.md` の §2「対象環境」に対応 RID を明示し、§2「画像処理」と §5「非機能要件」の依存パッケージ記述を実際の `PackageReference`(5 件)と一致させる
        _Requirements: 6.1, 6.2_
        _Boundary: Docs_
        _Depends: 1.2_
    - 対象ファイル: `docs/api-spec.md`(変更)
    - 設計参照: design.md §2.4(パッケージ表), §3(File Structure Plan)
    - 検証コマンド: `for r in linux-x64 win-x64 osx-arm64 OpenCvSharp4.runtime.win Sdcb.OpenCvSharp4.mini.runtime.osx-arm64; do grep -q "$r" docs/api-spec.md || echo "MISSING: $r"; done`(出力が空であること。3 RID と追加パッケージがすべて api-spec に明記されている)
  - [ ] 3.2 `README.md` に対応プラットフォームと CI での検証方法を追記する (P)
        _Requirements: 6.3_
        _Boundary: Docs_
        _Depends: 1.2_
    - 対象ファイル: `README.md`(変更)
    - 設計参照: design.md §4(CI 設計), §2.4
    - 検証コマンド: `for r in linux-x64 win-x64 osx-arm64 "GitHub Actions"; do grep -q "$r" README.md || echo "MISSING: $r"; done`(出力が空であること)
  - [ ] 3.3 `CLAUDE.md` の devcontainer 注意事項を訂正する。現行の「OpenCvSharp の公式ネイティブランタイムが linux-arm64 非対応」という理由は**事実に反する**(公式 `OpenCvSharp4.runtime.linux-arm64` は実在)。devcontainer が linux/amd64 であること自体は維持し、誤った理由の記述を実態へ書き換える (P)
        _Requirements: 6.4_
        _Boundary: Docs_
    - 対象ファイル: `CLAUDE.md`(変更)
    - 設計参照: design.md §10.4(訂正方針), research.md §2.1.1(公式 linux-arm64 ランタイムの実在)
    - 検証コマンド: `! grep -q "linux-arm64 非対応" CLAUDE.md`(誤った理由が除去されていること。終了コード 0 で合格)かつ `grep -q "linux/amd64" CLAUDE.md`(devcontainer の記述自体は残っていること)

- [ ] 4. 最終確認(非回帰・公開契約・設計判断の充足)
  - [ ] 4.1 既存テストの非回帰と公開 API の不変を確認する
        _Requirements: 3.1, 3.2, 3.3, 4.1_
        _Boundary: Recognizer.Tests_
        _Depends: 1.2, 2.1, 3.1_
    - 対象ファイル: なし(検証のみ)
    - 設計参照: design.md §9(テスト戦略), §5(トレーサビリティ 3.1・3.3 は既存 `PublicApiTests` で担保済み)
    - 検証コマンド: `dotnet test`(224 件全 green)。`git diff --stat main -- src/Recognizer/*.cs src/Recognizer/Internal/` が空(公開層・内部層のソース無変更 = シグネチャ不変)
  - [ ] 4.2 条件付き要件が不成立であることと、設計判断が記録済みであることを確認する(バックエンドを置換していない・ライセンス上の承認事項が無い・凍結済み unit を変更していない)
        _Requirements: 2.1, 2.2, 2.3, 2.4, 2.5, 3.4, 4.4, 6.5, 7.2, 7.3, 7.4_
        _Boundary: Docs_
        _Depends: 4.1_
    - 対象ファイル: なし(検証のみ)
    - 設計参照: design.md §2.3(選定の比較評価と採用理由), §10.1(撤退条件), §10.2(性能)
    - 検証コマンド: `git diff --stat main -- docs/specs/face-detection docs/specs/object-detection docs/specs/face-recognition` が空(凍結済み中間生成物の無変更 = 要件 6.5)。バックエンド非置換(= 要件 2.4・3.4・4.4・7.4 が不成立)は、タスク 4.1 のソース無変更 diff が既に立証しているため再検査しない。要件 2.1・2.2・2.3・7.3(選定の比較評価・撤退条件・ライセンスの記録)は承認済み design.md §2.3・§10.1・§2.4 で履行済みであることを目視確認する(コマンドによる機械判定はしない)

## Implementation Notes

(このセクションは dev-implement が実装中の学習・選択した知識 port・横断的な気付きを追記する領域)
