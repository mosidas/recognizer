# gui — 実装タスク

> 仕様の詳細は同じディレクトリの仕様文書 spec.md を参照する。
> このファイルには仕様を転記しない。要件 ID は spec.md §7 を、契約は §5/§6 を指す。

## File Structure Plan

| ファイルパス | 区分 | 責務 |
| ------------ | ---- | ---- |
| `src/Recognizer.Gui/Recognizer.Gui.csproj` | 新規 | Avalonia 12 デスクトップアプリのプロジェクト定義(net10.0・3 RID・`ProjectReference` で `Recognizer` 参照・`InternalsVisibleTo` テスト) |
| `src/Recognizer.Gui/Program.cs` | 新規 | エントリポイント(`BuildAvaloniaApp` の構成) |
| `src/Recognizer.Gui/App.axaml` / `App.axaml.cs` | 新規 | Application 定義・`MainWindow` 起動 |
| `src/Recognizer.Gui/Models/DetectionMode.cs` | 新規 | 検出モード enum(§6) |
| `src/Recognizer.Gui/Models/DetectionRequest.cs` | 新規 | 検出要求 record + 不変条件の検証(§6) |
| `src/Recognizer.Gui/Models/DetectionOverlay.cs` | 新規 | モード非依存の描画・一覧用 record(§6) |
| `src/Recognizer.Gui/Models/DetectionOutcome.cs` | 新規 | 結果型 record + `DetectionStatus` enum(§6) |
| `src/Recognizer.Gui/Services/IDetectionService.cs` | 新規 | 検出実行サービスの契約(§5.2)。ViewModel からの注入点 |
| `src/Recognizer.Gui/Services/DetectionService.cs` | 新規 | `RunAsync` 実装。コア API 呼び出し・例外→`DetectionStatus` 写像・破棄・キャンセル(§5.2) |
| `src/Recognizer.Gui/Services/ClassNamesFile.cs` | 新規 | クラス名ファイル(1 行 1 クラス名)の読み込み。CLI の internal 実装は参照不可のため GUI 側に再実装 |
| `src/Recognizer.Gui/Rendering/DisplayCoordinateMapper.cs` | 新規 | ピクセル座標→表示座標の純粋変換(§5.3) |
| `src/Recognizer.Gui/ViewModels/ViewModelBase.cs` | 新規 | `INotifyPropertyChanged` 基底 |
| `src/Recognizer.Gui/ViewModels/MainViewModel.cs` | 新規 | 入力状態・既定値・モード別有効化・実行コマンド・busy/多重実行防止・メッセージ(§5.1/§6/§7) |
| `src/Recognizer.Gui/Views/MainWindow.axaml` / `MainWindow.axaml.cs` | 新規 | 共通レイアウト(入力パネル・プレビュー・結果一覧)・ファイルピッカー配線 |
| `src/Recognizer.Gui/Views/DetectionOverlayControl.cs` | 新規 | プレビュー上に BBox・ランドマーク・ラベルを描く Avalonia コントロール(`DisplayCoordinateMapper` を使用) |
| `tests/Recognizer.Gui.Tests/Recognizer.Gui.Tests.csproj` | 新規 | xUnit + `Avalonia.Headless.XUnit` + fixture ONNX のリンク参照 |
| `tests/Recognizer.Gui.Tests/DetectionRequestTests.cs` | 新規 | 入力検証(閾値範囲・パス非空)のテスト |
| `tests/Recognizer.Gui.Tests/DisplayCoordinateMapperTests.cs` | 新規 | 座標変換のテスト |
| `tests/Recognizer.Gui.Tests/DetectionServiceTests.cs` | 新規 | 検出実行サービスのテスト(fixture ONNX) |
| `tests/Recognizer.Gui.Tests/MainViewModelTests.cs` | 新規 | ViewModel の状態・既定値・busy・メッセージのテスト(headless) |
| `Recognizer.sln` | 変更 | `Recognizer.Gui` / `Recognizer.Gui.Tests` の 2 プロジェクトを追加 |
| `README.md` | 変更 | GUI アプリの起動・使い方を追記 |

## タスク一覧

- [ ] 1. GUI プロジェクトの土台
  - [ ] 1.1 Avalonia 12 デスクトップアプリのプロジェクトを作成し、最小の App/MainWindow がビルド・起動構成できる状態にする
        _Requirements: 9.1, 9.3_
        _Boundary: GuiProject_
    - 対象ファイル: `src/Recognizer.Gui/Recognizer.Gui.csproj`, `src/Recognizer.Gui/Program.cs`, `src/Recognizer.Gui/App.axaml`, `src/Recognizer.Gui/App.axaml.cs`, `src/Recognizer.Gui/Views/MainWindow.axaml`(最小), `src/Recognizer.Gui/Views/MainWindow.axaml.cs`, `Recognizer.sln`(変更)
    - 仕様参照: spec.md §8 実現方針, §7 Requirement 9
    - 検証コマンド: `dotnet build src/Recognizer.Gui/Recognizer.Gui.csproj`
  - [ ] 1.2 GUI テストプロジェクトを作成し、Avalonia.Headless(xUnit 連携)と fixture ONNX のリンク参照を整える(空のスモークテストが `dotnet test` で走る)
        _Requirements: 9.2_
        _Boundary: GuiProject_
        _Depends: 1.1_
    - 対象ファイル: `tests/Recognizer.Gui.Tests/Recognizer.Gui.Tests.csproj`, `tests/Recognizer.Gui.Tests/`(スモークテスト), `Recognizer.sln`(変更), `src/Recognizer.Gui/Recognizer.Gui.csproj`(`InternalsVisibleTo` 追記)
    - 仕様参照: spec.md §8, §3 前提(fixture ONNX 共有)
    - 検証コマンド: `dotnet test tests/Recognizer.Gui.Tests/Recognizer.Gui.Tests.csproj`

- [ ] 2. ドメインモデルと入力検証 (P)
  - [ ] 2.1 検出モデル型(`DetectionMode` / `DetectionRequest` / `DetectionOverlay` / `DetectionOutcome` + `DetectionStatus`)を定義する
        _Requirements: 4.3_
        _Boundary: Models_
        _Depends: 1.1_
    - 対象ファイル: `src/Recognizer.Gui/Models/DetectionMode.cs`, `src/Recognizer.Gui/Models/DetectionRequest.cs`, `src/Recognizer.Gui/Models/DetectionOverlay.cs`, `src/Recognizer.Gui/Models/DetectionOutcome.cs`
    - 仕様参照: spec.md §6 データ構造
    - 検証コマンド: `dotnet build src/Recognizer.Gui/Recognizer.Gui.csproj`
  - [ ] 2.2 `DetectionRequest` の不変条件(パス非空・閾値 [0,1])を検証し、違反を `InvalidInput` の結果型で表す経路を実装する(正常・パス空・閾値範囲外の各分岐をテスト)
        _Requirements: 1.4, 1.5_
        _Boundary: Models_
        _Depends: 2.1_
    - 対象ファイル: `src/Recognizer.Gui/Models/DetectionRequest.cs`(変更), `tests/Recognizer.Gui.Tests/DetectionRequestTests.cs`
    - 仕様参照: spec.md §6 `DetectionRequest`, §7 Requirement 1.4/1.5
    - 検証コマンド: `dotnet test tests/Recognizer.Gui.Tests/Recognizer.Gui.Tests.csproj`

- [ ] 3. 表示座標変換 (P)
  - [ ] 3.1 `DisplayCoordinateMapper`(ピクセル→表示座標。uniform フィットのスケール・中央レターボックス)を実装し、変換・スケール式・非正サイズのガードをテストする
        _Requirements: 5.1, 5.2, 5.3_
        _Boundary: Rendering_
        _Depends: 1.1_
    - 対象ファイル: `src/Recognizer.Gui/Rendering/DisplayCoordinateMapper.cs`, `tests/Recognizer.Gui.Tests/DisplayCoordinateMapperTests.cs`
    - 仕様参照: spec.md §5.3 表示座標変換, §7 Requirement 5
    - 検証コマンド: `dotnet test tests/Recognizer.Gui.Tests/Recognizer.Gui.Tests.csproj`

- [ ] 4. 検出実行サービス
  - [ ] 4.1 クラス名ファイル読み込み(1 行 1 クラス名・空行除去・読み込み失敗の表出)を実装しテストする
        _Requirements: 3.2, 3.4_
        _Boundary: Services_
        _Depends: 2.1_
    - 対象ファイル: `src/Recognizer.Gui/Services/ClassNamesFile.cs`, `tests/Recognizer.Gui.Tests/DetectionServiceTests.cs`(クラス名分)
    - 仕様参照: spec.md §5.2, §7 Requirement 3.2/3.4
    - 検証コマンド: `dotnet test tests/Recognizer.Gui.Tests/Recognizer.Gui.Tests.csproj`
  - [ ] 4.2 `IDetectionService` / `DetectionService.RunAsync` を実装する(顔=`FaceDetector`・物体=`ObjectDetector` を生成→`DetectAsync`→`DetectionOverlay` へ写像→破棄、UI スレッド外実行、0 件成功)
        _Requirements: 4.1, 4.2, 4.3, 4.4, 2.1, 3.1, 3.3_
        _Boundary: Services_
        _Depends: 4.1_
    - 対象ファイル: `src/Recognizer.Gui/Services/IDetectionService.cs`, `src/Recognizer.Gui/Services/DetectionService.cs`, `tests/Recognizer.Gui.Tests/DetectionServiceTests.cs`
    - 仕様参照: spec.md §5.2 検出実行サービス, §7 Requirement 2.1/3.1/3.3/4
    - 検証コマンド: `dotnet test tests/Recognizer.Gui.Tests/Recognizer.Gui.Tests.csproj`
  - [ ] 4.3 コア例外を結果型へ写す分岐(モデルロード失敗→`ModelLoadFailed`・画像ロード失敗→`ImageLoadFailed`・非対応形式→`UnsupportedModel`・キャンセル→`Cancelled`)を実装し、各分岐を個別にテストする(非対応形式は fixture の `*_unsupported_*.onnx` を使用)
        _Requirements: 6.1, 6.2, 6.3, 4.5_
        _Boundary: Services_
        _Depends: 4.2_
    - 対象ファイル: `src/Recognizer.Gui/Services/DetectionService.cs`(変更), `tests/Recognizer.Gui.Tests/DetectionServiceTests.cs`
    - 仕様参照: spec.md §5.2 エラー表出テーブル, §7 Requirement 6.1-6.3/4.5
    - 検証コマンド: `dotnet test tests/Recognizer.Gui.Tests/Recognizer.Gui.Tests.csproj`

- [ ] 5. ViewModel と状態管理
  - [ ] 5.1 `ViewModelBase` と `MainViewModel` の入力状態・既定値(顔 0.7/物体 0.5/NMS 0.5)・物体モード時のクラス名指定有効化を実装しテストする
        _Requirements: 1.1, 1.2, 1.3_
        _Boundary: ViewModels_
        _Depends: 2.1_
    - 対象ファイル: `src/Recognizer.Gui/ViewModels/ViewModelBase.cs`, `src/Recognizer.Gui/ViewModels/MainViewModel.cs`, `tests/Recognizer.Gui.Tests/MainViewModelTests.cs`
    - 仕様参照: spec.md §5.1, §6 ViewModel 状態, §7 Requirement 1.1-1.3
    - 検証コマンド: `dotnet test tests/Recognizer.Gui.Tests/Recognizer.Gui.Tests.csproj`
  - [ ] 5.2 実行コマンドを実装する(`IDetectionService` を注入呼び出し・busy 提示・多重実行防止・完了/失敗で busy 解除・`DetectionOutcome` からメッセージと結果一覧を反映)。busy 中の再実行抑止と失敗時のメッセージ表示を分岐ごとにテストする
        _Requirements: 7.1, 7.2, 7.3, 6.4, 8.3_
        _Boundary: ViewModels_
        _Depends: 5.1, 4.2_
    - 対象ファイル: `src/Recognizer.Gui/ViewModels/MainViewModel.cs`(変更), `tests/Recognizer.Gui.Tests/MainViewModelTests.cs`
    - 仕様参照: spec.md §6 ViewModel 状態, §7 Requirement 6.4/7/8.3
    - 検証コマンド: `dotnet test tests/Recognizer.Gui.Tests/Recognizer.Gui.Tests.csproj`

- [ ] 6. ビュー(レイアウトとオーバーレイ描画)
  - [ ] 6.1 メインウィンドウの共通レイアウト(入力パネル・画像プレビュー・結果一覧)と各入力要素・ファイルピッカーを `MainViewModel` にバインドする
        _Requirements: 8.1, 8.3_
        _Boundary: Views_
        _Depends: 5.2_
    - 対象ファイル: `src/Recognizer.Gui/Views/MainWindow.axaml`(変更), `src/Recognizer.Gui/Views/MainWindow.axaml.cs`(変更)
    - 仕様参照: spec.md §5.1, §3 前提(最小レイアウト), §7 Requirement 8.1/8.3
    - 検証コマンド: `dotnet build src/Recognizer.Gui/Recognizer.Gui.csproj`
  - [ ] 6.2 `DetectionOverlayControl` を実装し、プレビュー画像上に BBox・信頼度ラベル・(顔で存在時のみ)ランドマークを `DisplayCoordinateMapper` 経由で描画する(ランドマーク有無の分岐を含む。ピクセル描画の目視確認は統括の macOS 実機に委ね、座標写像の適用はテストで確認)
        _Requirements: 2.2, 2.3, 2.4, 3.1, 8.2_
        _Boundary: Views_
        _Depends: 6.1, 3.1_
    - 対象ファイル: `src/Recognizer.Gui/Views/DetectionOverlayControl.cs`, `src/Recognizer.Gui/Views/MainWindow.axaml`(変更), `tests/Recognizer.Gui.Tests/`(座標写像適用の headless テスト)
    - 仕様参照: spec.md §5.3, §7 Requirement 2.2-2.4/3.1/8.2
    - 検証コマンド: `dotnet test tests/Recognizer.Gui.Tests/Recognizer.Gui.Tests.csproj`

- [ ] 7. ドキュメント反映と全体検証
  - [ ] 7.1 README に GUI アプリの起動方法・使い方(モデル/画像/パラメータ指定・モード切替・クラス名ファイル)を追記する
        _Requirements: 1.1_
        _Boundary: Docs_
        _Depends: 6.2_
    - 対象ファイル: `README.md`(変更)
    - 仕様参照: spec.md §1, §5.1
    - 検証コマンド: `dotnet build`
  - [ ] 7.2 ソリューション全体を対象 RID(linux-x64 / win-x64 / osx-arm64)でビルドし、全テストを実行して通ることを確認する(コンテナ内は build + `dotnet test`。macOS 実機の目視確認は統括が担う)
        _Requirements: 9.1, 9.3_
        _Boundary: GuiProject_
        _Depends: 7.1_
    - 対象ファイル: (検証のみ。コード変更が要れば当該タスクへ戻す)
    - 仕様参照: spec.md §7 Requirement 9
    - 検証コマンド: `dotnet build && dotnet test`

## Implementation Notes

(このセクションは dev-implement が実装中の学習・選択した知識 port・横断的な気付きを追記する領域。初期は空でよい)
