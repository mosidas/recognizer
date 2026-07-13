# multi-platform — 設計

## 1. 概要

recognizer を win-x64 / osx-arm64 / linux-x64 の 3 プラットフォームで動作させる。画像処理ライブラリは比較評価の結果 **OpenCvSharp を継続**し、不足していた win-x64 / osx-arm64 のネイティブランタイムパッケージを追加する。公開 API(`Mat` オーバーロードを含む)は一切変更しない。あわせて GitHub Actions で 3 プラットフォームの実機テストを回す。

本設計はコードの振る舞いを変えない。変更の実体は **csproj の依存 2 行追加 / 依存検査テストの許可リスト更新 / CI ワークフローの新設 / 恒久情報の更新** に限られる。判断根拠と一次情報の出典は `research.md` にある。

### ゴール

- 3 RID すべてでネイティブ資産(画像処理・ONNX Runtime)が解決される
- 既存 224 テストの非回帰(振る舞い変更ゼロ)
- 3 プラットフォームの実機テストが CI で自動実行される
- 選定の根拠・ライセンス・サードパーティ依存の撤退条件が記録される

### 非ゴール

- 画像処理バックエンドの置換(比較評価の結果、継続と決定。§2.3)
- 公開 API の変更・追加(要件 3)
- linux-arm64 / win-arm64 / osx-x64 対応、NuGet パッケージ公開、CI でのカバレッジ・静的解析(要件のスコープ外)

## 2. アーキテクチャ

### 2.1 既存システムの分析

`src/Recognizer` は `OpenCvSharp.Mat` を**公開契約の一部**として露出する(`Mat` を引数に取る公開メソッドが 4 件。`research.md` §1.3)。内部では 6 操作(デコード・リサイズ・色変換・パディング・ROI 切り出し・画素バッファ連続アクセス)に OpenCvSharp を用いる(同 §1.2)。csproj は `OpenCvSharp4.official.runtime.linux-x64` のみを参照し、RID 宣言・条件付き `ItemGroup` を持たない。テストは Path API で OS 中立に書かれており、Windows / macOS で落ちる箇所は見つかっていない(同 §1.5)。

### 2.2 Boundary Map(責務境界)

コードの責務境界は**変更しない**。本 unit が触る境界は、コード外のビルド構成・CI・ドキュメントに限られる。

| コンポーネント | 層 | 責務 | 本 unit での変更 |
| --- | --- | --- | --- |
| `Recognizer.csproj` | ビルド構成 | 依存パッケージと RID 別ネイティブ資産の解決 | **変更**(ランタイムパッケージ 2 件追加) |
| `Recognizer`(公開 API) | ライブラリ公開層 | 検出・認証・物体検出の公開契約 | 変更なし(要件 3) |
| `Recognizer.Internal` | ライブラリ内部層 | 前処理・NMS・出力パース | 変更なし |
| `Recognizer.Tests` | 検証層 | 振る舞いと依存構成の検証 | **変更**(`PublicApiTests` の許可リスト) |
| `.github/workflows/ci.yml` | CI | 3 プラットフォームでの実機ビルド・テスト | **新規** |
| 恒久情報(api-spec / README / CLAUDE.md) | ドキュメント | 対応プラットフォームと依存の正本 | **変更** |

依存方向は従来どおり単方向(Tests → 公開 API → Internal → OpenCvSharp / ONNX Runtime)。

### 2.3 技術スタック — 画像処理ライブラリの選定(要件 2)

**決定: OpenCvSharp を継続する(候補 A)。**

5 評価軸での比較(詳細な表と一次情報の出典は `research.md` §4):

| 評価軸 | A. OpenCvSharp 継続 | B. SkiaSharp | C. SixLabors.ImageSharp |
| --- | --- | --- | --- |
| 公開 API 契約への影響 | **なし** | 破壊的(`Mat` → `SKBitmap`) | 破壊的(`Mat` → `Image<Bgr24>`) |
| ライセンス | Apache-2.0(全パッケージ) | MIT | Six Labors Split License(年商 100 万 USD 以上の営利利用は有償) |
| 供給元の信頼 | 本体・win・linux は公式(shimat)。**osx-arm64 のみサードパーティ(sdcb)** | Microsoft | Six Labors |
| 性能 | ネイティブ OpenCV(現行ベースライン) | ネイティブ Skia | pure-managed のため一般にネイティブより遅い |
| ネイティブ依存 / RID 構成 | 3 RID すべてに資産あり(実証済み) | 公式 NativeAssets が 3 RID に存在 | ネイティブ依存ゼロ |
| 6 操作の充足 | 全操作を直接提供(実証済み) | パディングは canvas 描画で代替が必要 | 全操作を提供 |
| 移行コスト | **ゼロ**(csproj に 2 行) | 内部 5 ファイル + 公開 3 クラス + テスト 5 クラス(30 件超) | 同左 |

**採用理由(なぜ A か)**

1. **公開 API を壊さない唯一の選択肢**。`Mat` は api-spec §3.1 が定める公開契約であり、B・C はいずれも 4 つの公開メソッドのシグネチャを変える。要件 2.4 により、置換を選ぶと unit の再分割と再承認が必要になり、プラットフォーム対応という本 unit の目的達成が遅れる。プラットフォーム追加は利用者から見て機能追加であり、破壊的変更を正当化する理由がない(要件 3 のユーザーストーリー)。
2. **移行の最有力動機だった「ネイティブ依存の排除」は達成不能**。`Microsoft.ML.OnnxRuntime` が全 OS のネイティブライブラリを P/Invoke する(`research.md` §3.1 の publish 出力が実証)。画像処理を managed 化しても ORT は native のままであり、「pure-managed 化」は成立しない。ImageSharp の利点は「RID 別パッケージの管理が不要」という構成の単純さだけに縮退するが、その単純さは A でも **無条件参照 3 行**で得られることが実証された(§2.4)。
3. **ライセンス**: C は下流利用者に売上規模依存の課金義務を波及させうる(要件 7.2 の人間承認事項)。MIT のコアライブラリの依存として適さない。A は全パッケージ Apache-2.0。
4. **A の弱点(osx-arm64 のサードパーティ依存)は受容可能**。撤退条件を §10.1 に定める。

**なぜ B(SkiaSharp)を選ばないか**: 供給元の信頼(Microsoft)と MIT は魅力だが、破壊的変更のコストを払う対価が「サードパーティ依存の解消」だけであり、割に合わない。加えて Skia には `copyMakeBorder` 相当が無く、letterbox パディングを canvas 描画で再実装することになり、既存の数値挙動(Scalar(114,114,114) 充填)の同等性検証コストが増える。

**なぜ C(ImageSharp)を選ばないか**: 上記 2・3 のとおり、主要な利点が成立しないかライセンス上の負債になる。

### 2.4 RID 構成(要件 1)

3 つのランタイムパッケージを **無条件 `PackageReference`** で参照する。条件付き `ItemGroup` も `RuntimeIdentifiers` 宣言も置かない(YAGNI)。

| パッケージ | バージョン | 供給元 | ライセンス | 対象 RID |
| --- | --- | --- | --- | --- |
| `Microsoft.ML.OnnxRuntime` | 1.27.1 | Microsoft(公式) | MIT | 全 RID(1 パッケージに同梱) |
| `OpenCvSharp4` | 4.13.0.20260627 | shimat(公式) | Apache-2.0 | マネージド(RID 非依存) |
| `OpenCvSharp4.official.runtime.linux-x64` | 4.13.0.20260627 | shimat(公式) | Apache-2.0 | linux-x64 |
| `OpenCvSharp4.runtime.win` | 4.13.0.20260627 | shimat(公式) | Apache-2.0 | win-x64 |
| `Sdcb.OpenCvSharp4.mini.runtime.osx-arm64` | 4.13.0.45 | sdcb(**サードパーティ**) | Apache-2.0 | osx-arm64 |

この構成が要件 1 を満たすことは devcontainer 上のスパイクで実証済み(`research.md` §3):

- **RID 未指定 `dotnet build` / `dotnet test`**: 出力の `runtimes/<rid>/native/` に全 RID の資産が配置され、ホストの RID で解決される → 要件 1.3
- **`dotnet publish -r <RID>`**: 3 RID すべてで成功し、**画像処理バックエンド(OpenCvSharp)のネイティブは対象 RID のものだけ**が配置される(他 RID の OpenCvSharp 資産は入らない)→ 要件 1.2 / 1.4。ただし ONNX Runtime に由来する例外がある(§10.3)
- **`Sdcb.OpenCvSharp4.mini.runtime.osx-arm64`**: dylib に必要 6 操作の関数(`core_copyMakeBorder` 等。`copyMakeBorder` は OpenCV の core モジュール所属)とテスト用 API(`imgcodecs_imencode` / `imgcodecs_imwrite` / `core_absdiff` / `core_countNonZero`)、および画像コーデック(libjpeg-turbo / libpng / libwebp / libtiff)が含まれることを確認 → 要件 1.1・2.5

`OpenCvSharp4.official.runtime.win-x64` は **存在しない**(Windows の公式パッケージ ID は `OpenCvSharp4.runtime.win`)。osx-arm64 の公式ランタイムは 4.13 系に存在せず、公式 macOS パッケージは 4.6.0(2023 年、x64)で更新停止している(`research.md` §2.1)。

## 3. File Structure Plan

| ファイルパス | 区分 | 責務 |
| --- | --- | --- |
| `src/Recognizer/Recognizer.csproj` | 変更 | `OpenCvSharp4.runtime.win` と `Sdcb.OpenCvSharp4.mini.runtime.osx-arm64` を `PackageReference` に追加(why コメント付き) |
| `tests/Recognizer.Tests/PublicApiTests.cs` | 変更 | 依存パッケージ許可リスト(`AllowedPackageReferences`)を 3 → 5 パッケージへ更新(テスト名の「3つ」も実数へ改名)。厳密一致検査のため、テストの追加は不要 |
| `.github/workflows/ci.yml` | 新規 | 3 プラットフォームのマトリクスでビルド・テスト・RID 別 publish を実行 |
| `docs/api-spec.md` | 変更 | §2「対象環境」に対応 RID を明示、§2「画像処理」と §5「非機能要件」の依存パッケージ記述を実態と一致させる |
| `README.md` | 変更 | 対応プラットフォームと CI での検証方法を追記 |
| `CLAUDE.md` | 変更 | devcontainer の注意事項(linux-arm64 非対応の理由)を実態へ更新 |

削除対象なし。`docs/specs/{face-detection,object-detection,face-recognition}/` は凍結済みのため変更しない(要件 6.5)。

## 4. CI 設計(要件 5)

`.github/workflows/ci.yml`:

```yaml
name: CI
on:
  push:
    branches: [main]
  pull_request:
    branches: [main]
jobs:
  test:
    strategy:
      fail-fast: false          # 1 ジョブの失敗で他を打ち切らない(要件 5.6)
      matrix:
        include:
          - os: ubuntu-latest
            rid: linux-x64
          - os: windows-latest
            rid: win-x64
          - os: macos-15        # Apple Silicon(arm64)。macos-14 は deprecated
            rid: osx-arm64
    runs-on: ${{ matrix.os }}
    steps:
      - uses: actions/checkout@v5
      - uses: actions/setup-dotnet@v5
        with:
          dotnet-version: '10.0.x'
      - run: dotnet build --configuration Release
      - run: dotnet test --configuration Release --no-build
      - run: dotnet publish src/Recognizer/Recognizer.csproj -c Release -r ${{ matrix.rid }} -o publish-${{ matrix.rid }}
```

設計上の判断:

- **ランナー**: `macos-15`(= `macos-latest`)は arm64。`macos-14` も arm64 だが deprecated のため使わない(`research.md` §2.3)。`ubuntu-latest` は x64、`windows-latest` は x64。
- **`fail-fast: false`**: 要件 5.6(1 つ落ちても他を完走させ全プラットフォームの結果を得る)。既定は `true` なので明示が必要。
- **ジョブの失敗伝播**: いずれかのステップが非ゼロ終了すればジョブが失敗し、マトリクスジョブの失敗はワークフロー全体の失敗になる(GitHub Actions の既定挙動)→ 要件 5.5。
- **publish ステップ**: 要件 1.2(RID 別ネイティブ資産の配置)は devcontainer では win/osx の**実行**まで確認できない。各ランナーで実 RID の publish を通すことで、ローカル検証(§9)を実機で裏取りする。検証対象は画像処理バックエンドのネイティブ資産であり、ORT 由来の `onnxruntime.dll` 混入(§10.3)は対象外とする。
- **`--no-build`**: 直前の `dotnet build` の成果物を使い、ビルドの二重実行を避ける。
- **ローカル検証の範囲**(要件 5.7): YAML の構文妥当性(パーサでの検証)と、実行コマンドがローカルの `dotnet build` / `dotnet test` と整合すること。GitHub Actions 自体は devcontainer で実行できない。

## 5. Requirements Traceability(要件トレーサビリティ)

| 要件 ID | 要件内容(requirements.md より転記) | 設計要素 | 根拠・備考 |
| --- | --- | --- | --- |
| 1.1 | `linux-x64` / `win-x64` / `osx-arm64` の 3 つの RID すべてに対して、画像処理バックエンドのネイティブ資産を解決できなければならない | `Recognizer.csproj`(§2.4)、`PublicApiTests` の依存パッケージ厳密一致検査(§9) | `research.md` §3.1・§3.3 で実証。CI の 3 ジョブ(§4)が実機で解決を検証 |
| 1.2 | `dotnet publish -r <RID>` 実行時、publish 出力に当該 RID 向けの画像処理バックエンドと ONNX Runtime のネイティブ資産を配置しなければならない | `Recognizer.csproj`(§2.4)、CI の publish ステップ(§4) | `research.md` §3.2 で 3 RID すべて実証(classlib 単体・consumer app の両構成) |
| 1.3 | RID を指定しない `dotnet build` / `dotnet test` は、ホストプラットフォームで従来どおり終了コード 0 で完了しなければならない | `Recognizer.csproj`(無条件参照。§2.4) | `research.md` §3.1。条件付き参照にしないことでホスト解決を保つ |
| 1.4 | 特定の 1 プラットフォームでのみ有効なネイティブランタイムパッケージを、他のプラットフォーム向けの publish 出力に含めてはならない | `Recognizer.csproj`(§2.4)、CI の publish ステップ(§4) | RID 別ランタイムパッケージ(OpenCvSharp の 3 件)について `research.md` §3.2 で確認。ONNX Runtime は RID 別ランタイムパッケージではない(単一パッケージが全 RID を同梱)ため本基準の対象外。ただし classlib 単体 publish 時に ORT の targets が Windows native を混入させる事象を §10.3 に記録し、設計ゲートで人間に確認する |
| 2.1 | 画像処理バックエンドの候補を比較評価し、採否とその根拠を design.md に記録しなければならない | §2.3(比較表と採用理由)、`research.md` §4 | 本文書が成果物 |
| 2.2 | 比較評価において 5 評価軸(公開 API 影響 / ライセンス / 供給元の信頼性 / 3 プラットフォームでのネイティブ資産入手可否 / 6 操作の充足)をすべて明示的に扱わなければならない | §2.3 の比較表(要求された 5 軸すべて。第 5 軸「6 操作の充足」は独立行として明示。参考として移行コストも併記) | `research.md` §4 に一次情報の出典付きで詳述 |
| 2.3 | 選定結果がサードパーティ提供のパッケージに依存する場合、採用理由・供給停止時の影響・代替への撤退条件を記録しなければならない | §10.1(Sdcb 依存のリスクと撤退条件) | osx-arm64 が該当 |
| 2.4 | 選定結果が OpenCvSharp の置換を伴う場合、実装に進まず再分割案を提示して人間の承認を求めなければならない | §2.3(**置換しない**決定) | 発火せず。継続のため本 unit のまま実装へ進む |
| 2.5 | 対象 3 プラットフォームのいずれかでネイティブ資産または実行可能な代替が存在しない候補は、採用してはならない | §2.4(3 RID すべてに資産が存在することを確認) | `research.md` §2.1・§3.3 |
| 3.1 | 公開型を 9 型に保ち、増減させてはならない | 公開層に変更なし(§2.2)。`PublicApiTests.ExportedTypes_公開型は許可された9型のみ` が既存で検証 | 既存テスト(`PublicApiTests.cs`)で担保済み |
| 3.2 | 既存の公開メソッドのシグネチャを変更してはならない | 公開層に変更なし(§2.2)。OpenCvSharp 継続により `Mat` オーバーロードを維持 | ソース無変更が根拠 |
| 3.3 | 内部実装型を公開してはならない | 変更なし。`PublicApiTests.ExportedTypes_内部実装型は非公開` が既存で検証 | 既存テストで担保済み |
| 3.4 | 公開 API 契約の維持が不可能と判明した場合、要件 2.4 の手順に従わなければならない | §2.3(継続決定により維持可能) | 発火せず |
| 4.1 | `dotnet test` で既存テスト 224 件を全件 green(Failed 0 / Skipped 0)で完了しなければならない | §9(テスト戦略)。振る舞いを変えないため全件維持 | ベースライン 224 件 green を確認済み |
| 4.2 | `Recognizer.csproj` の `PackageReference` 変更時、`PublicApiTests` の許可リストを同一集合へ更新しテストを green に保たなければならない | `PublicApiTests.cs` の `AllowedPackageReferences` を 5 パッケージへ更新(§3) | 許可リストは厳密一致検査のため、csproj 変更と同一タスクで行う |
| 4.3 | 依存パッケージ許可リストと `Recognizer.csproj` の `PackageReference` 集合が厳密一致することをテストで検証しなければならない | 既存の `PublicApiTests.Csproj_依存パッケージは許可された3つのみ`(名称は 5 件へ更新) | 既存テストの検査ロジックを流用 |
| 4.4 | 画像処理バックエンドが置換された場合、6 操作の同等性テストを追加しなければならない | 該当なし(置換しない。§2.3) | 条件不成立のため追加テストなし |
| 4.5 | ローカル検証は `dotnet publish -r <RID>` によるネイティブ資産の配置確認までとし、実行検証は CI に委ねなければならない | §9(ローカル検証の範囲)、§4(CI での実機検証) | devcontainer は linux/amd64 |
| 5.1 | GitHub Actions のワークフロー定義ファイルを `.github/workflows/` 配下に持たなければならない | `.github/workflows/ci.yml`(新規。§3・§4) | — |
| 5.2 | main への push または main 宛 pull request でワークフローを起動しなければならない | §4 の `on: push/pull_request(branches: [main])` | — |
| 5.3 | `ubuntu-latest` / `windows-latest` / `macos-14` 以降の Apple Silicon ランナーの 3 ジョブをマトリクスで実行しなければならない | §4 の `strategy.matrix`(`macos-15` を採用) | `macos-15` は arm64。`macos-14` は deprecated(`research.md` §2.3) |
| 5.4 | 各ジョブで .NET 10 SDK をセットアップし、`dotnet build` と `dotnet test` を実行しなければならない | §4 の `actions/setup-dotnet@v5`(`10.0.x`)+ build/test ステップ | 出典: https://github.com/actions/setup-dotnet |
| 5.5 | いずれかのジョブでビルドまたはテストが失敗した場合、ワークフロー全体を失敗として報告しなければならない | §4(GitHub Actions の既定挙動。ステップの非ゼロ終了 → ジョブ失敗 → ワークフロー失敗) | 明示設定は不要 |
| 5.6 | 1 つのジョブが失敗しても他のジョブを完走させなければならない | §4 の `fail-fast: false` | 既定 `true` のため明示が必須 |
| 5.7 | ワークフロー YAML の構文妥当性と、実行コマンドがローカルの `dotnet build` / `dotnet test` と整合することをローカル検証の範囲としなければならない | §9(YAML パース検査 + コマンド整合の確認) | GitHub Actions は devcontainer で実行不可 |
| 6.1 | `docs/api-spec.md` §2「対象環境」を、対応 RID を明示した記述へ更新しなければならない | `docs/api-spec.md`(§3 の File Structure Plan) | — |
| 6.2 | `docs/api-spec.md` §2「画像処理」と §5「非機能要件」の依存パッケージ記述を、実際の `PackageReference` と一致させなければならない | 同上 | 5 パッケージを列挙する |
| 6.3 | `README.md` に対応プラットフォームと CI での検証方法を記載しなければならない | `README.md`(§3) | — |
| 6.4 | `CLAUDE.md` の devcontainer に関する注意事項を、選定結果を踏まえた実態へ更新しなければならない | `CLAUDE.md`(§3)、§10.4 | **現行の記述は誤り**。`OpenCvSharp4.runtime.linux-arm64` 4.13.0.20260627(shimat / Apache-2.0)は実在する(`research.md` §2.1.1)ため、「公式ランタイムが linux-arm64 非対応だから devcontainer は linux/amd64」という理由は成り立たない。§10.4 の方針で訂正する |
| 6.5 | 凍結済みの 3 unit の中間生成物を変更してはならない | File Structure Plan に含めない(§3) | — |
| 7.1 | 依存パッケージを ONNX Runtime と画像処理バックエンド(+ RID 別ランタイムパッケージ)に限定しなければならない | §2.4 の 5 パッケージ | 追加は OpenCvSharp の RID 別ランタイム 2 件のみ |
| 7.2 | 売上規模等に連動して追加ライセンス費用が発生しうるライブラリを含める場合、実装に進まず人間承認を求めなければならない | §2.3(ImageSharp を採用しない決定) | 発火せず。全依存が Apache-2.0 / MIT |
| 7.3 | 依存パッケージ(RID 別ランタイムパッケージを含む)のライセンス名を設計成果物に記録しなければならない | §2.4 の表(ライセンス列) | `research.md` §2 に nuspec からの取得結果 |
| 7.4 | 画像処理バックエンドが置換された場合、前処理性能を計測し 2 倍超の悪化を記録・再判断しなければならない | 該当なし(置換しない。§2.3) | 条件不成立。前処理コードは無変更のため性能は不変 |

## 6. コンポーネントとインターフェース

コードのインターフェースに変更はない。本 unit が定義する契約はビルド構成レベルのもの:

### Recognizer.csproj(ビルド構成)

- **依存(inbound)**: `Recognizer.Tests`(ProjectReference)
- **依存(outbound)**: なし
- **外部依存(external)**: §2.4 の 5 パッケージ
- **契約**:
  - 事前条件: NuGet からこれら 5 パッケージが復元できること
  - 事後条件 1(RID 未指定ビルド): 本ライブラリを参照する consumer(`Recognizer.Tests` 等)の出力に `runtimes/<rid>/native/` として全 RID の資産が配置され、ホストの RID で解決される。**クラスライブラリ単体のビルド出力には native 資産は現れない**(`Recognizer.dll` 等のみ)。これは .NET の既定挙動であり、native 資産の配置は consumer 側で行われる
  - 事後条件 2(`-r <RID>` 指定の publish): **OpenCvSharp の** native は当該 RID のもののみが配置される(他 RID の OpenCvSharp native は入らない)。ONNX Runtime の native については、クラスライブラリを直接 publish した場合に限り Windows 用 `onnxruntime.dll` が混入する(§10.3 の既知事項。consumer app の publish では混入しない)
  - 不変条件: `PackageReference` の集合は `PublicApiTests.AllowedPackageReferences` と厳密一致する(要件 4.3)

## 7. データモデル

変更なし(本 unit はドメインモデルに触れない)。

## 8. エラーハンドリング

コードの異常系は変更しない。本 unit で新たに生じうる失敗は次の 2 つで、いずれもビルド/CI 時に顕在化する:

| 失敗 | 検知 | 対応 |
| --- | --- | --- |
| ランタイムパッケージの復元失敗(NuGet 側の供給停止・版の削除) | `dotnet restore` / CI が失敗 | §10.1 の撤退条件へ |
| 実機で native の解決に失敗(dylib / dll がロードできない) | CI の該当プラットフォームのジョブが失敗 | ローカルでは再現不能。CI ログで判断し、必要なら撤退条件へ |

## 9. テスト戦略

**振る舞いを変えないため、テストは原則として追加しない。** 変更・追加は依存構成の契約に関わるものだけに留める(YAGNI)。

| 検証対象 | 手段 | 要件 |
| --- | --- | --- |
| 既存の振る舞い(224 件) | `dotnet test`(全件 green を維持) | 4.1 |
| 依存パッケージ集合の厳密一致(= 3 RID のランタイムパッケージが揃っていることの担保を兼ねる) | 既存 `PublicApiTests` の csproj 検査(許可リストを 5 パッケージへ更新)。許可リストは**厳密一致**検査なので、3 つのランタイムパッケージのいずれかが csproj から失われればテストが落ちる | 1.1, 4.2, 4.3 |
| RID 別ネイティブ資産の配置(ローカル) | `dotnet publish -r <RID>` を 3 RID で実行し、出力に当該 RID の **OpenCvSharp** native が存在し、他 RID の OpenCvSharp native が無いことを確認(手動検証。テストコード化はしない) | 1.2, 1.4, 4.5 |
| ワークフロー YAML の妥当性(ローカル) | YAML パーサで構文検査し、`run:` のコマンドがローカルの `dotnet build` / `dotnet test` と整合することを目視確認 | 5.7 |
| ORT 由来の `onnxruntime.dll` 混入(§10.3) | 検証対象から除外する(要件 1.4 の対象外。RID 別ランタイムパッケージではないため) | — |
| 3 プラットフォームでの実機実行 | CI(`ubuntu-latest` / `windows-latest` / `macos-15`) | 5.3, 5.4, 4.5 |

`dotnet publish` の配置確認をテストコード化しない理由: publish はビルドの外側の操作で、xUnit から起動すると実行時間が数分単位で伸び、テストの独立性も損なう。要件 4.5 が求めるのは「ローカル検証の範囲」であり、CI の publish ステップ(§4)が恒久的な自動検証を担う。

## 10. その他

### 10.1 サードパーティ依存のリスクと撤退条件(要件 2.3)

**対象**: `Sdcb.OpenCvSharp4.mini.runtime.osx-arm64` 4.13.0.45(osx-arm64 のネイティブ資産のみ)

**採用理由**: OpenCvSharp の公式ランタイムは osx-arm64 の現行版(4.13 系)を提供しておらず、公式 macOS パッケージは 4.6.0(2023 年、x64)で更新停止している(`research.md` §2.1)。**Apple Silicon 対応の現行選択肢はこれのみ**。ライセンスは本体と同じ Apache-2.0。必要な全関数とコーデックの同梱を検証済み(同 §3.3)。

**供給停止時の影響**: osx-arm64 のみビルド不能になる。linux-x64 / win-x64 は公式パッケージのため影響を受けない。ライブラリのコード変更は不要で、csproj の 1 行が解決できなくなるだけ。

**撤退条件**(いずれかに該当したら再評価する):

1. OpenCvSharp 公式が osx-arm64 ランタイムを提供し始めたら、公式へ乗り換える(サードパーティ依存の解消)
2. Sdcb パッケージが OpenCvSharp 本体のバージョンに追随しなくなり、本体との版ズレが 1 マイナーバージョン以上開いたら、本体のバージョン固定か代替バックエンドへの移行を検討する
3. パッケージが NuGet から削除された、またはライセンスが Apache-2.0 から変更されたら、即座に代替(SkiaSharp への移行)を新 unit として起票する

**リスクの限定**: この依存は osx-arm64 の**ネイティブ資産だけ**であり、マネージド API(`OpenCvSharp4`)は公式のまま。撤退時もコードの書き換えは発生しない(バックエンド移行を選ぶ場合を除く)。

### 10.2 性能

前処理コード(`Internal/Preprocessor.cs` / `Internal/EmbeddingPreprocessor.cs`)は無変更で、linux-x64 のネイティブも従来と同じ公式パッケージのため、**性能は不変**。要件 7.4 の計測は「バックエンドを置換した場合」の条件付き要件であり、発火しない。

なお osx-arm64 の native は mini ビルド(videoio / dnn 等を除いた縮小版)であり、公式 linux ビルドとバイナリが異なる。本ライブラリが使う core / imgproc / imgcodecs の実装は同じ OpenCV 4.13 系だが、**プラットフォーム間で前処理の浮動小数点結果が完全にビット一致する保証はない**(SIMD 実装差)。既存テストは検出結果を「入力非依存の定数出力」の fixture で検証しており、画素値のビット一致に依存していないため、この差は非回帰の判定に影響しない。CI で実証する。

### 10.3 既知事項: classlib 単体 publish への `onnxruntime.dll` 混入(受容する)

`dotnet publish src/Recognizer/Recognizer.csproj -r linux-x64`(または `osx-arm64`)を実行すると、出力に **Windows 用の `onnxruntime.dll` / `onnxruntime_providers_shared.dll` が混入する**(`research.md` §3.2 (b) で実測)。

- **原因**: `Microsoft.ML.OnnxRuntime` の `build/netstandard2.0/Microsoft.ML.OnnxRuntime.targets` が、**直接参照するプロジェクト**に対して Windows ネイティブを RID 非依存でコピーするため。
- **利用者への影響はない**: ライブラリの実際の消費形態(consumer app が recognizer を参照して publish する)では `buildTransitive/` 経由となりこの targets が効かず、混入しない(同 §3.2 (a) で実測)。混入するのは「クラスライブラリを単体で publish する」という、ライブラリとしては通常行わない操作のときだけ。
- **要件 1.4 との関係**: 要件 1.4 が禁じるのは「特定の 1 つでのみ有効な**ネイティブランタイムパッケージ**」の混入であり、ORT は単一パッケージが全 RID のネイティブを同梱する構成でこれに該当しない。画像処理バックエンド(OpenCvSharp)の RID 別パッケージについては混入がないことを実測で確認済み。
- **対処しない理由**: `ExcludeAssets="build"` 等で ORT の targets を無効化すると、ORT が想定するネイティブ配置の仕組みごと壊すリスクがある。利用者に影響がなく、要件にも抵触しない事象のために依存パッケージの既定挙動へ介入するのは、YAGNI かつリスクに見合わない。
- **設計ゲートでの確認事項**: 要件 1.4 の解釈(ORT 由来の混入を対象外とすること)を人間に確認する。対象とする判断であれば、csproj での除外設定を追加設計する。

### 10.4 `CLAUDE.md` の devcontainer 注記の訂正方針(要件 6.4)

現在の `CLAUDE.md` は次のように書いている:

> devcontainer は linux/amd64(Apple Silicon では Rosetta エミュレーション)で動作する。OpenCvSharp の公式ネイティブランタイムが linux-arm64 非対応のため

**この理由は事実に反する**。`OpenCvSharp4.runtime.linux-arm64` 4.13.0.20260627(shimat / Apache-2.0)が現行版として存在する(`research.md` §2.1.1)。

訂正方針: devcontainer が linux/amd64 であること自体は維持する(linux-arm64 対応は要件のスコープ外であり、本 unit では検証しない)。書き換えるのは**理由の部分**で、「公式ランタイムが非対応だから」という誤った根拠を削り、実態(本リポジトリが検証対象とする RID は linux-x64 / win-x64 / osx-arm64 の 3 つであり、linux-arm64 は対象外)に基づく記述にする。

参考(スコープ外の将来的機会): 公式 linux-arm64 ランタイムが存在するため、devcontainer を arm64 ネイティブ化する余地はある。必要になった時点で別 unit として起票する。

## 11. 参考資料

- 調査ログ・一次情報の出典・スパイク結果: [`research.md`](./research.md)
- 要件: [`requirements.md`](./requirements.md)
- 公開 API の正本: [`docs/api-spec.md`](../../api-spec.md)
- NuGet: https://www.nuget.org/packages/OpenCvSharp4 / https://www.nuget.org/packages/OpenCvSharp4.runtime.win
- Sdcb OpenCvSharp mini runtime: https://github.com/sdcb/opencvsharp-mini-runtime
- GitHub Actions ランナーイメージ: https://github.com/actions/runner-images
- actions/setup-dotnet: https://github.com/actions/setup-dotnet
- Six Labors Split License: https://github.com/SixLabors/ImageSharp/blob/main/LICENSE
