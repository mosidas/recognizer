# gui — 仕様

## 1. 目的と背景

recognizer はコアライブラリと CLI のみで構成され、検出結果の確認には CLI の JSON 出力を別途描画する必要がある。モデル・画像・パラメータを変えながら**顔検出・物体検出**の結果を入力画像へ重ねて目視確認できる Avalonia デスクトップ GUI を追加し、モデル評価とパラメータ調整を容易にする。GUI はコアライブラリの公開 API(`FaceDetector` / `ObjectDetector`)を呼ぶだけの薄いフロントエンドとし、検出ロジックは複製しない。

## 2. スコープ

### 対象(やること)

- モデルファイルパス・入力画像・検出モード(顔 / 物体)・信頼度閾値・NMS 閾値・(物体検出時)クラス名ファイルを GUI から指定して検出を実行する。
- 顔検出結果(バウンディングボックス・信頼度・ランドマーク 5 点)と物体検出結果(バウンディングボックス・信頼度・クラス名)を入力画像に重ねて表示する。
- 検出結果の一覧(信頼度・クラス名 / 顔インデックス)をテキストでも提示する。
- 予期されるエラー(モデルロード失敗・画像ロード失敗・非対応モデル形式・検出 0 件)を画面上のメッセージとして表出し、クラッシュさせない。
- コンテナ内では build + `dotnet test` で検証する。UI スレッド外での検出実行・結果への座標変換など、描画に依存しないロジックをヘッドレステスト可能な形で切り出す。

### 対象外(やらないこと)

- 顔認証(compare-face 相当)— 理由: 依頼で明示的にスコープ外。必要になった時点で別 unit とする。
- 注釈画像の保存・検出結果の JSON エクスポート — 理由: 本 unit の目的は目視確認であり、保存機能は現時点で不要(YAGNI)。必要なら別途拡張する。
- 複数画像の一括処理・フォルダ走査・連続比較 — 理由: 目視確認は単一画像単位で成立する。
- モデル形式の判別・前処理・NMS 等の検出ロジック — 理由: コアライブラリ(`Recognizer`)の責務。GUI は公開 API を呼ぶだけ。
- コアライブラリ(`src/Recognizer`)への機能追加・依存追加 — 理由: 依頼で「Avalonia 依存は新規 GUI プロジェクト限定・コアライブラリの 5 依存制約は不変」と申し送り。

## 3. 前提(未検証の賭け)

- **視覚設計の正本は未提供** — デザイントークン定義・UI モック・共通レイアウト仕様は依頼で示されていない。最小機能レイアウト(単一ウィンドウ。左=入力パネル、中央=画像プレビュー+オーバーレイ、下=結果一覧)を採用案として §5.1 / Requirement 8 に定義する。検証方法: 統括が macOS 実機で目視確認し、レイアウトの要否を判断する / 状態: 未検証。
- **検出モードは利用者が明示選択する** — モデルの形式(顔用 / 物体用)はコアが自動判別しないため、`FaceDetector` と `ObjectDetector` のどちらを使うかは GUI 上のモード選択で決める。検証方法: 受け入れ基準(Requirement 2 / 3)/ 状態: 未検証。
- **入力画像は単一ファイル選択** — ファイルピッカーで 1 枚選ぶ。検証方法: Requirement 1 / 状態: 未検証。
- **クラス名ファイルは 1 行 1 クラス名のプレーンテキスト** — CLI の `--classes`(`ClassNamesFile`)と同一形式に揃える。行数がモデルのクラス数と不一致でもエラーにせず、範囲外 ID はコアが `class_{id}` にフォールバックする。検証方法: Requirement 3 / 状態: 未検証。
- **GUI はコアの `string imagePath` オーバーロードを使う** — 検出は入力画像パスをそのままコアに渡す。プレビュー画像の読み込みは Avalonia の `Bitmap`(パスから)で行い、GUI は `OpenCvSharp4` を参照しない。検証方法: build 時の依存グラフ確認 / 状態: 未検証。
- **ヘッドレステストは Avalonia 12 系 + `Avalonia.Headless`(xUnit 連携)を採用する** — Avalonia は現行安定版の 12 系(12.1.0 で存在確認済み)を用い、`Avalonia.Headless` も 12 系に揃える。検出実行サービス・座標変換・ViewModel 状態を画面表示なしで検証する。検出を伴うテストは既存テストが共有する fixture ONNX を用いる(GUI テストプロジェクトからの参照可否は design で確定)。検証方法: `dotnet test`(コンテナ内、GUI 表示不可)/ 状態: 未検証。
- **対象 RID は linux-x64 / win-x64 / osx-arm64** — コアライブラリと同一。macOS(osx-arm64)実機の目視確認は統括が担う。検証方法: `dotnet build` / 状態: 未検証。

## 4. 用語定義

| 用語 | 定義 |
| ---- | ---- |
| 検出モード | 顔検出(`FaceDetector`)/ 物体検出(`ObjectDetector`)のいずれか。利用者が選択する |
| オーバーレイ | 入力画像のプレビューに重ねて描く検出結果の図形(BBox・ランドマーク点・ラベル) |
| 表示座標変換 | 画像のピクセル座標(左上原点)を、プレビュー表示領域上の座標へ写す幾何変換(等倍スケール + レターボックスのオフセット) |

## 5. 公開インターフェース(API)

GUI アプリケーションのため「公開 API」は (a) 利用者から見た操作契約と、(b) 描画に依存せずヘッドレステスト可能なコード契約の 2 層で定義する。ファイル構成・XAML の詳細は書かない(dev-decompose の責務)。

### 5.1 アプリケーション操作契約(メインウィンドウ)

- **定義**: 単一ウィンドウのデスクトップアプリケーション(publish 後の実行対象は Avalonia デスクトップ)。以下の操作要素を持つ。
  - モデルファイル選択(ファイルピッカー)
  - 入力画像選択(ファイルピッカー、単一)
  - 検出モード選択(顔検出 / 物体検出)
  - 信頼度閾値の入力(数値。既定は顔=0.7 / 物体=0.5)
  - NMS 閾値の入力(数値。既定 0.5)
  - クラス名ファイル選択(物体検出モードのときのみ有効。省略可)
  - 実行(検出の開始)
- **入力 / 出力**:
  - 入力: 上記の各値。
  - 出力: 入力画像のプレビューに検出結果を重ねた表示(§5.3 の変換を用いる)と、検出結果の一覧(顔=インデックス・信頼度・ランドマーク有無、物体=クラス名・信頼度)。
- **事前条件**: 実行にはモデルファイルパスと入力画像パスが指定されていること。未指定なら実行操作を無効化するか、実行時にエラーメッセージを表出する。
- **事後条件**: 実行後、成功時は検出件数に応じたオーバーレイと一覧を表示する(0 件でも空表示で成立)。失敗時はエラーメッセージを表示し、直前の表示状態を破壊しない。
- **エラー**: モデルロード失敗・画像ロード失敗・非対応モデル形式・引数不正を、画面上の人間可読な日本語メッセージとして表出する(§5.2 の結果型を経由)。未処理例外でアプリを終了させない。

### 5.2 検出実行サービス(テスト可能なコード契約)

- **定義**(擬似コード):

  ```csharp
  Task<DetectionOutcome> RunAsync(DetectionRequest request, CancellationToken cancellationToken);
  ```

  ViewModel からコアライブラリの検出を呼ぶ境界。UI スレッドをブロックせずに実行する(`Task.Run` 等でバックグラウンド実行)。
- **入力 / 出力**: 入力 `DetectionRequest`(§6)、出力 `DetectionOutcome`(§6)。
- **事前条件**: `DetectionRequest` の各パス・閾値が設定されていること(検証は本サービスまたは呼び出し側で行い、未設定は結果型のエラーで表す)。
- **事後条件**:
  - 顔モードは `new FaceDetector(modelPath)` + `DetectAsync(imagePath, confidence, nms, ct)` を、物体モードは `new ObjectDetector(modelPath, classNames?)` + `DetectAsync(imagePath, confidence, nms, ct)` を呼ぶ。
  - コアの結果(`IReadOnlyList<FaceDetection>` / `IReadOnlyList<ObjectDetection>`)を、モード非依存の描画・一覧用モデル `DetectionOverlay`(§6)へ写して返す。
  - `IDisposable` なコア型(`FaceDetector` / `ObjectDetector`)は実行ごとに確実に破棄する。
- **エラー表出**: コアが送出する例外を結果型 `DetectionOutcome.Status` に写す(下表)。予期されるエラーは例外として呼び出し側へ伝播させない。

  | コアの状況 | `DetectionOutcome.Status` |
  | --- | --- |
  | 正常(検出 0 件を含む) | `Success` |
  | モデルファイル不在・ロード失敗(`FileNotFoundException` 等) | `ModelLoadFailed` |
  | 画像ロード失敗(`ArgumentException`。パス不正・デコード不可) | `ImageLoadFailed` |
  | 非対応モデル形式(`NotSupportedException`) | `UnsupportedModel` |
  | クラス名ファイル読み込み失敗 | `ClassNamesFileFailed` |
- **キャンセル**: `cancellationToken` のキャンセルで実行中の検出を中止でき、`OperationCanceledException` は結果型 `Cancelled` に写すか、呼び出し側で握って状態を復帰する。

### 5.3 表示座標変換(純粋ロジック・テスト可能)

- **定義**(擬似コード):

  ```csharp
  static Rect ToDisplayRect(RectangleF pixelBox, PixelSize imageSize, Size viewport);
  static Point ToDisplayPoint(PointF pixelPoint, PixelSize imageSize, Size viewport);
  ```

  画像のピクセル座標(左上原点)を、プレビュー表示領域上の座標へ写す。プレビューは画像をアスペクト比維持で表示領域に収める(uniform フィット)前提とし、等倍スケール係数と中央寄せのレターボックスオフセットを用いる。
- **入力 / 出力**: 入力 = ピクセル座標の矩形 / 点・画像のピクセルサイズ・表示領域サイズ。出力 = 表示領域上の矩形 / 点。
- **事前条件**: `imageSize` の幅・高さが正、`viewport` の幅・高さが正。
- **事後条件**: スケール係数は `min(viewport.W / imageSize.W, viewport.H / imageSize.H)`。オフセットは表示画像を表示領域中央に置く量。変換は原点・スケールについて一貫し、画像全体を表す矩形は表示画像の外接矩形に一致する。
- **エラー**: `imageSize` または `viewport` が非正のとき `ArgumentOutOfRangeException`(描画前に呼ばないためのガード)。

## 6. データ構造

- **`DetectionMode`**(enum): `Face` / `Object`。検出モードを型で表す。

- **`DetectionRequest`**(record・不変):

  ```csharp
  record DetectionRequest(
      DetectionMode Mode,
      string ModelPath,
      string ImagePath,
      float ConfidenceThreshold,
      float NmsThreshold,
      string? ClassNamesPath);   // 物体モードのみ。省略可
  ```

  - 不変条件: `ModelPath` / `ImagePath` は非空。`ConfidenceThreshold` / `NmsThreshold` は [0, 1]。`ClassNamesPath` は `Mode == Object` のときのみ意味を持つ(顔モードでは無視)。生成時(または実行前の検証)にこれらを強制し、違反は §5.2 の結果型エラーにする。

- **`DetectionOverlay`**(record・不変): モード非依存の描画・一覧用モデル。

  ```csharp
  record DetectionOverlay(
      RectangleF BBox,                    // 画像ピクセル座標
      float Confidence,
      string Label,                       // 物体=クラス名 / 顔="face #{index}" 等
      IReadOnlyList<PointF>? Landmarks);  // 顔のランドマーク 5 点。物体・ランドマーク無し顔は null
  ```

  - `FaceDetection` → `Landmarks` に 5 点(あれば)、物体 → `Landmarks` は null。この統合により描画・一覧のロジックをモード間で共有する(ロジックの集約先: §5.2 の写像 + §5.3 の変換)。

- **`DetectionOutcome`**(record・不変): 検出実行の結果型。

  ```csharp
  enum DetectionStatus { Success, ModelLoadFailed, ImageLoadFailed, UnsupportedModel, ClassNamesFileFailed, Cancelled, InvalidInput }

  record DetectionOutcome(
      DetectionStatus Status,
      IReadOnlyList<DetectionOverlay> Detections,   // 非成功時は空
      string? ImageDisplayPath,                     // 成功時のプレビュー元(通常は入力画像パス)
      string? Message);                             // エラー時の日本語メッセージ(成功時は null)
  ```

  - 不変条件: `Status == Success` のとき `Message == null`、それ以外は `Message` に人間可読な日本語説明を持つ。`Detections` は `Success` のときのみ非空になりうる(0 件成功もある)。

- **ViewModel 状態(`MainViewModel` 等)**: 上記入力値(モデルパス・画像パス・モード・両閾値・クラス名パス)、実行中フラグ(busy)、直近の `DetectionOutcome`、表示用メッセージを観測可能プロパティとして保持する。busy 中は実行操作を抑止する(多重実行防止)。プロパティ変更通知と状態遷移(idle → running → 完了/失敗 → idle)を ViewModel に集約する(貧血モデルを避ける)。

## 7. 振る舞い(受け入れ基準)

### Requirement 1: 入力の指定

**対象**: §5.1 アプリケーション操作契約 / §6 `DetectionRequest` / `DetectionMode`

**受け入れ基準**:
1.1. システムは、モデルファイルパス・入力画像パス・検出モード・信頼度閾値・NMS 閾値を利用者が指定する手段を提供しなければならない。(常時)
1.2. 起動直後のとき、システムは信頼度閾値の既定値(顔モード 0.7 / 物体モード 0.5)と NMS 閾値の既定値(0.5)を初期表示しなければならない。(イベント)
1.3. 物体検出モードが選択されているとき、システムはクラス名ファイルの指定手段を有効にしなければならない。(状態)
1.4. モデルパスまたは入力画像パスが未指定の場合、システムは検出の実行を抑止するか、実行時に未指定を示すエラーメッセージを表示しなければならない。(異常系)
1.5. 信頼度閾値または NMS 閾値が [0, 1] の範囲外で指定された場合、システムは検出を実行せず範囲外である旨のメッセージを表示しなければならない。(異常系)

### Requirement 2: 顔検出の実行と重畳表示

**対象**: §5.2 検出実行サービス / §6 `DetectionOverlay`

**受け入れ基準**:
2.1. 顔検出モードで実行されたとき、システムは指定モデル・画像・閾値で `FaceDetector.DetectAsync` を呼び、結果を入力画像プレビューに重ねて表示しなければならない。(イベント)
2.2. システムは、各顔のバウンディングボックスと信頼度を重畳表示しなければならない。(常時)
2.3. 検出結果がランドマークを含むとき、システムは 5 点のランドマークを重畳表示しなければならない。(イベント)
2.4. 検出結果がランドマークを含まないとき、システムはランドマークを描画せずバウンディングボックスと信頼度のみを表示しなければならない。(状態)

### Requirement 3: 物体検出の実行と重畳表示

**対象**: §5.2 検出実行サービス / §6 `DetectionRequest`(`ClassNamesPath`)/ `DetectionOverlay`

**受け入れ基準**:
3.1. 物体検出モードで実行されたとき、システムは指定モデル・画像・閾値で `ObjectDetector.DetectAsync` を呼び、各物体のバウンディングボックス・信頼度・クラス名を入力画像プレビューに重ねて表示しなければならない。(イベント)
3.2. クラス名ファイルが指定されたとき、システムはそのファイルを 1 行 1 クラス名として読み込み、`ObjectDetector` へ渡さなければならない。(イベント)
3.3. クラス名ファイルが未指定のとき、システムはクラス名をコアライブラリの既定解決(80 クラスなら COCO 名、それ以外は `class_{id}`)に委ねなければならない。(状態)
3.4. クラス名ファイルの読み込みに失敗した場合、システムは検出を中止し、その旨のメッセージを表示しなければならない。(異常系)

### Requirement 4: 検出実行サービスの契約

**対象**: §5.2 検出実行サービス / §6 `DetectionOutcome`

**受け入れ基準**:
4.1. 検出が要求されたとき、システムは UI スレッドをブロックせずにバックグラウンドで検出を実行しなければならない。(イベント)
4.2. システムは、検出モードに応じて対応するコア型(`FaceDetector` / `ObjectDetector`)を用い、実行後にそれらを破棄しなければならない。(常時)
4.3. システムは、コアの検出結果をモード非依存の `DetectionOverlay` の列へ写して返さなければならない。(常時)
4.4. 検出が完了したとき、システムは `Status = Success` の `DetectionOutcome` を返し、検出 0 件でも成功として空の結果を返さなければならない。(イベント)
4.5. 実行中にキャンセルが要求された場合、システムは検出を中止し、`Cancelled` として状態を実行前へ復帰しなければならない。(異常系)

### Requirement 5: 表示座標変換

**対象**: §5.3 表示座標変換

**受け入れ基準**:
5.1. システムは、画像ピクセル座標の矩形・点を、アスペクト比を維持して表示領域に収めた表示座標へ変換しなければならない。(常時)
5.2. システムは、スケール係数を `min(表示領域幅 / 画像幅, 表示領域高 / 画像高)` として計算し、表示画像を表示領域中央に配置するオフセットを適用しなければならない。(常時)
5.3. 画像サイズまたは表示領域サイズが非正の場合、システムは `ArgumentOutOfRangeException` を送出しなければならない。(異常系)

### Requirement 6: エラーの表出

**対象**: §5.2 検出実行サービス / §6 `DetectionOutcome`(`Status` / `Message`)

**受け入れ基準**:
6.1. モデルファイルの不在またはロードに失敗した場合、システムは `ModelLoadFailed` を返し、モデルを読み込めない旨の日本語メッセージを表示しなければならない。(異常系)
6.2. 入力画像のロードに失敗した場合、システムは `ImageLoadFailed` を返し、画像を読み込めない旨のメッセージを表示しなければならない。(異常系)
6.3. モデル形式が非対応の場合、システムは `UnsupportedModel` を返し、非対応である旨のメッセージを表示しなければならない。(異常系)
6.4. いずれのエラーが発生した場合も、システムは未処理例外でアプリケーションを終了させてはならない。(異常系)

### Requirement 7: 実行状態の提示

**対象**: §6 ViewModel 状態(busy)

**受け入れ基準**:
7.1. 検出の実行中、システムは実行中であることを示す状態(busy)を提示しなければならない。(状態)
7.2. 検出の実行中に再度の実行が要求された場合、システムは新たな実行を開始してはならない(多重実行の防止)。(異常系)
7.3. 検出が完了または失敗したとき、システムは busy 状態を解除し、再実行を許可しなければならない。(イベント)

### Requirement 8: 視覚レイアウト

**対象**: §5.1 アプリケーション操作契約

**受け入れ基準**:
8.1. システムは、入力パネル・画像プレビュー・結果一覧を同一ウィンドウ内に配置した共通レイアウト(§3 の採用案)を適用しなければならない。(常時)
8.2. システムは、バウンディングボックス・ランドマーク・ラベルを、プレビュー画像上で判別可能な配色・線で描画しなければならない。(常時)
8.3. 検出結果一覧のとき、システムは各検出の信頼度と識別名(物体=クラス名 / 顔=インデックス)をテキストで提示しなければならない。(状態)

### Requirement 9: 依存境界と検証手段(非機能)

**対象**: §5.2 検出実行サービス / §8 実現方針

**受け入れ基準**:
9.1. システムは、Avalonia への依存を新規 GUI プロジェクト(および GUI テストプロジェクト)に限定し、コアライブラリ(`src/Recognizer`)の依存パッケージ(5 件)を変更してはならない。(常時)
9.2. システムは、検出実行サービス・表示座標変換・ViewModel 状態を、画面表示を伴わずに `dotnet test`(ヘッドレス)で検証可能でなければならない。(常時)
9.3. システムは、対象 RID(linux-x64 / win-x64 / osx-arm64)で `dotnet build` が成功しなければならない。(常時)

## 8. 実現方針(要点のみ)

- **GUI フレームワーク**: Avalonia 12 系(現行安定版。12.1.0 で存在確認済み)。MVVM で ViewModel にロジックを集約し、描画に依存しない部分(§5.2 / §5.3 / ViewModel 状態)を単体テスト対象にする。ヘッドレステストは `Avalonia.Headless`(xUnit 連携)を用いる。パッチバージョンの確定は dev-decompose / 実装で行う。
- **プロジェクト構成の方針**: 新規 `src/Recognizer.Gui`(Avalonia アプリ)+ `tests/Recognizer.Gui.Tests`(xUnit)。GUI はコアライブラリを `ProjectReference` する。具体的なファイル配置は File Structure Plan(dev-decompose)で定める。
- **依存方向**: `Recognizer.Gui` → `Recognizer`(単方向)。コアは GUI を知らない。GUI はコアの `string imagePath` オーバーロードを使い、`OpenCvSharp4` を参照しない(プレビュー画像の読み込みは Avalonia `Bitmap`)。
- **CLI との関係**: CLI の入力(モデル・画像・信頼度・NMS・`--classes`)とクラス名ファイル形式(1 行 1 クラス名)に揃える。GUI は CLI を参照せず、両者は独立してコアを呼ぶ。

## 9. 参考資料

- 公開 API 仕様: `docs/api-spec.md`(§3.3 FaceDetector / §3.5 ObjectDetector / §3.6 エラー処理)
- CLI のクラス名ファイル形式: `src/Recognizer.Cli/Commands/ClassNamesFile.cs`
- 既存 unit(すべて completed): `docs/specs/{face-detection,object-detection,face-recognition,multi-platform,cli}/`
