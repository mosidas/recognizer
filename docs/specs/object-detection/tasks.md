# object-detection — 実装タスク

> 仕様の詳細は同じディレクトリの requirements.md(要件 ID)・design.md(節)を参照する。
> このファイルには仕様を転記しない。

## タスク一覧

- [ ] 1. テスト fixture の増設
  - [x] 1.1 物体検出用 fixture 4 種(⑫〜⑮)の生成と README 追記
        _Requirements: 2.3, 2.4, 2.5, 2.8, 3.2, 4.2_
        _Boundary: TestFixtures_
    - 対象ファイル: `tools/generate_test_models.py`(変更。builder 追加), `tests/Recognizer.Tests/Fixtures/object_*.onnx`(新規 4 種), `tests/Recognizer.Tests/Fixtures/README.md`(変更)
    - 設計参照: design.md §9(⑫ 転置 4+C・異クラス同一座標ペア / ⑬ 標準 5+C・objectness 合成既知値 / ⑭ 転置 C=80 / ⑮ F=4 非対応。N > F 規則)、research.md §4・§6
    - 検証コマンド: `/tmp/onnx-venv/bin/python tools/generate_test_models.py` の後、新規 4 ファイルの存在・各 100 KB 未満・既存 fixture 11 種のバイト列不変(SHA 比較)

- [ ] 2. モデル判別の拡張
  - [x] 2.1 ModelIntrospector への IntrospectObject / ClassifyObjectOutput 追加のテストと実装
        _Requirements: 2.1, 2.2, 2.3, 2.8_
        _Boundary: ModelIntrospector_
        _Depends: 1.1_
    - 対象ファイル: `src/Recognizer/Internal/ModelIntrospector.cs`(変更。既存メソッドのシグネチャ・挙動不変、入力判別の private 共通化のみ), `tests/Recognizer.Tests/ModelIntrospectorTests.cs`(変更。既存テスト不変)
    - 設計参照: design.md §6 ModelIntrospector 規則 (o-d)〜(o-g)(F/N 識別・d1=d2 転置優先・転置=4+C/標準=5+C・F 下限・複数出力・動的出力軸の保留を 1 ガード 1 テストで)
    - 検証コマンド: `dotnet test --filter "FullyQualifiedName~ModelIntrospectorTests"` + `dotnet test`(全体。既存 80 テストの非回帰)

- [ ] 3. 出力パースとクラス名定数
  - [x]* 3.1 (P) ObjectOutputParser(4+C/5+C パース・argmax・信頼度合成・閾値フィルタ)の実装
        _Requirements: 2.4, 2.5, 4.1_
        _Boundary: ObjectOutputParser_
        _Depends: 2.1_
    - 対象ファイル: `src/Recognizer/Internal/ObjectOutputParser.cs`(新規。internal 候補 struct `ObjectCandidate` を同居)
    - 設計参照: design.md §6 ObjectOutputParser。契約検証は fixture パイプライン経由で 5.1 が担う(design.md §9)
    - 検証コマンド: `dotnet build`
  - [x]* 3.2 (P) CocoClassNames(Ultralytics 標準 80 クラス名)の実装
        _Requirements: 3.2_
        _Boundary: CocoClassNames_
    - 対象ファイル: `src/Recognizer/Internal/CocoClassNames.cs`(新規)
    - 設計参照: design.md §6 CocoClassNames、research.md §5(coco.yaml が正本)。要素数 80 等の検証は 5.3 が担う
    - 検証コマンド: `dotnet build`

- [ ] 4. ObjectDetector 骨格
  - [x] 4.1 ObjectDetection record とコンストラクタ(ガード・物体用判別・classNames 保持)・Dispose のテストと実装
        _Requirements: 2.1, 2.6, 2.7, 2.8, 2.9, 5.4_
        _Boundary: ObjectDetector_
        _Depends: 1.1, 2.1_
    - 対象ファイル: `src/Recognizer/ObjectDetector.cs`(新規), `src/Recognizer/ObjectDetection.cs`(新規), `tests/Recognizer.Tests/ObjectDetectorTests.cs`(新規)
    - 設計参照: design.md §6 ObjectDetector(コンストラクタ契約・classNames 非防御コピー)、§8(null / FileNotFound / ORT 透過 / NotSupported=fixture ⑮ を 1 ガード 1 テストで)。公開シグネチャは api-spec §3.5 と文字単位一致
    - 検証コマンド: `dotnet test --filter "FullyQualifiedName~ObjectDetectorTests"`

- [ ] 5. DetectAsync 統合
  - [x] 5.1 DetectAsync(Mat)のパイプライン統合(クラス単位 NMS 含む)のテストと実装
        _Requirements: 1.1, 1.5, 1.6, 2.4, 2.5, 4.1, 4.2, 4.3, 4.4, 4.5, 4.6, 4.7_
        _Boundary: ObjectDetector_
        _Depends: 3.1, 4.1_
    - 対象ファイル: `src/Recognizer/ObjectDetector.cs`(変更), `tests/Recognizer.Tests/ObjectDetectorTests.cs`(変更)
    - 設計参照: design.md §4 フロー、§6(クラス単位 NMS のグルーピングとマージ)。fixture ⑫で異クラス同一座標ペアの両残り・同クラス抑制・argmax・降順・空リスト・既定値 0.5/0.5・閾値範囲外・空 Mat・**null Mat(1.6 の Mat 側分岐。1 ガード 1 テスト)**、fixture ⑬で objectness×最大クラススコア、非正方入力で座標復元を個別テスト
    - 検証コマンド: `dotnet test --filter "FullyQualifiedName~ObjectDetectorTests"` + `dotnet test`(全体)
  - [ ] 5.2 パス / バイト列オーバーロードのテストと実装
        _Requirements: 1.2, 1.3, 1.4, 1.6_
        _Boundary: ObjectDetector_
        _Depends: 5.1_
    - 対象ファイル: `src/Recognizer/ObjectDetector.cs`(変更), `tests/Recognizer.Tests/ObjectDetectorTests.cs`(変更)
    - 設計参照: design.md §6(FaceDetector と同一の委譲・Mat 所有権パターン)、§8(不存在パス・非画像・不正/空バイト列・null の各分岐を個別テスト。Mat 版との結果同一性)
    - 検証コマンド: `dotnet test --filter "FullyQualifiedName~ObjectDetectorTests"`
  - [ ] 5.3 クラス名解決(4 規則)のテストと実装確認
        _Requirements: 3.1, 3.2, 3.3, 3.4_
        _Boundary: ObjectDetector_
        _Depends: 3.2, 5.1_
    - 対象ファイル: `tests/Recognizer.Tests/ObjectDetectorTests.cs`(変更), `src/Recognizer/ObjectDetector.cs`(必要時のみ変更)
    - 設計参照: design.md §6 クラス名解決。classNames 指定(fixture ⑫)・省略時 COCO 名(fixture ⑭、person 等の実名)・省略時 class_{id}(fixture ⑫、C=3)・範囲外フォールバック(短い classNames)の 4 規則 + CocoClassNames の要素数 80・先頭/末尾名を個別テスト
    - 検証コマンド: `dotnet test --filter "FullyQualifiedName~ObjectDetectorTests"`
  - [ ] 5.4 非同期契約(キャンセル・並行・Dispose 後)のテストと実装確認
        _Requirements: 5.1, 5.2, 5.3, 5.5_
        _Boundary: ObjectDetector_
        _Depends: 5.1_
    - 対象ファイル: `tests/Recognizer.Tests/ObjectDetectorTests.cs`(変更), `src/Recognizer/ObjectDetector.cs`(必要時のみ変更)
    - 設計参照: design.md §6・§10(キャンセル済みトークン・並行 Task.WhenAll 同一結果・Dispose 後の同期送出を個別テスト。FaceDetector の 6.4 と同一方式)
    - 検証コマンド: `dotnet test --filter "FullyQualifiedName~ObjectDetectorTests"`(2 回実行し並行テストの安定性確認)

- [ ] 6. 公開面の検査と仕上げ
  - [ ] 6.1 公開 API 面の期待集合更新と全体非回帰の最終確認
        _Requirements: 6.1, 6.2, 6.3, 6.4, 6.5_
        _Boundary: Solution_
        _Depends: 5.2, 5.3, 5.4_
    - 対象ファイル: `tests/Recognizer.Tests/PublicApiTests.cs`(変更。公開型の期待集合を 5 型へ更新、内部型検査に ObjectOutputParser / CocoClassNames を追加)
    - 設計参照: design.md §5(6.1〜6.5)。既存の厳密集合比較・ソース走査・csproj 検査の仕組みを流用
    - 検証コマンド: `dotnet build && dotnet test`(終了コード 0)+ `grep -rn "Console\." src/Recognizer/` 0 件 + `grep -c "<PackageReference" src/Recognizer/Recognizer.csproj` が 3 のまま

## Implementation Notes

- 知識 port: 注入なし(`docs/dev/ports/` 不在)。準拠規約は CLAUDE.md と design.md の契約。
- 再利用資産の internal API 一覧は research.md §1(ImageDecoder / Preprocessor / Letterbox / NonMaxSuppression / DetectionModelSpec)。FaceDetector のソースは変更禁止(非回帰)。
- 既存 fixture の期待値正本は `tests/Recognizer.Tests/Fixtures/README.md`。物体用 fixture の定数・期待値も同 README に追記して正本とする。
- レビュー教訓(unit 1): 異常系ガードは 1 ガード 1 テスト。非同期の同期送出検証は非 async の Assert.Throws で。
- 物体 fixture(1.1)の期待値(正本は Fixtures/README.md): ⑫ P0(class0,0.90)→P2(class1,0.85,P0と同座標・別クラスで両残)→P3(class2,0.70) の 3 件。P1 は P0 と IoU≈0.68 で同クラス抑制、P4(0.30)は閾値除外。⑬ Q0(0.9×0.8=0.72,class0)→Q1(0.8×0.75=0.60,class1)、Q2(0.42)除外。⑭ person(0.95)→car(0.88)→cat(0.75)(ClassId 0/2/15)。⑮ F=4 で NotSupported。float32 丸めのため許容誤差比較を推奨。
- ModelIntrospector 追加 API(2.1): `IntrospectObject(InferenceSession)` → `DetectionModelSpec`(動的出力は分類保留)。`ClassifyObjectOutput(ReadOnlySpan<int>)` → `ObjectOutputSpec(Format, FeatureCount, CandidateCount, ClassCount, HasObjectness)`(転置=C=F−4/objectness なし、標準=C=F−5/あり)。初回 Run 実形状の確定は呼び出し側。fixture ⑯ object_dynamic_output_4c3(動的出力軸。実形状 [1,7,60]、定数は ⑫ と同一)。
- 教訓(2.1): 定数出力グラフは ORT の shape inference で出力形状が静的解決される。動的出力メタデータが要る fixture は出力軸を動的入力次元に依存させる(Slice + Shape/Gather)。
- 4.1 で PublicApiTests の期待集合(3 型→5 型)を前倒し更新した(トランクを green に保つため。6.1 の残作業は内部型検査の追加と最終非回帰確認)。ObjectDetector フィールド: _session / _modelSpec / _classNames(非防御コピー)/ _disposed。
