# object-detection — 調査ログ(Gap 分析)

design.md の判断根拠。既存コードベース(unit face-detection、main へマージ済み)の拡張のため、要件と既存資産の差分を分析した。調査は dev-explorer に隔離し、ダイジェストを本ログに集約した。

## 1. 現状調査(再利用可能な既存資産)

| 資産 | シグネチャ(internal) | 本 unit での利用 |
| --- | --- | --- |
| `ImageDecoder` | `EnsureValid(Mat)` / `DecodeFile(string)` / `DecodeBytes(ReadOnlyMemory<byte>)` | そのまま再利用(要件 1.x) |
| `ModelIntrospector` | `Introspect(InferenceSession)` → `DetectionModelSpec` / `ClassifyOutput(ReadOnlySpan<int>)` → `OutputSpec` | 入力判別は再利用。出力判別は顔専用(F ∈ {5,20} が `IsFeatureCount` にハードコード)のため**拡張が必要** |
| `DetectionModelSpec` | `record(Layout, InputWidth, InputHeight, InputName, OutputName)` | そのまま再利用 |
| `Preprocessor` | `Preprocess(Mat, DetectionModelSpec)` → `(DenseTensor<float>, LetterboxParams)` | そのまま再利用 |
| `Letterbox` | `LetterboxParams.Create / InverseTransform / ClampToBounds` | そのまま再利用(要件 4.5) |
| `NonMaxSuppression` | `Apply(IReadOnlyList<(RectangleF, float)>, float)` → 採用インデックス(降順) | そのまま再利用。**クラス単位のグルーピングは呼び出し側で実装可能**(シグネチャ変更不要) |
| `FaceDetector` の骨格 | コンストラクタ(~40 行)/ 同期ガード + Task.Run(~15 行)/ Dispose(~10 行)/ RunPipeline(~25 行) | 構造を踏襲(複製)。共有基盤化はしない(下記 3.) |
| テスト基盤 | fixture 生成スクリプト(builder 関数構造)・`AppContext.BaseDirectory + "Fixtures"` のパス解決・`InternalsVisibleTo` | builder 追加で物体 fixture を増設可能 |

- 支配的パターン: 公開クラスが編成のみを持ち、無状態 internal static 部品へ委譲。異常系は 1 ガード 1 テスト。
- `PublicApiTests` は公開型集合を厳密比較しているため、公開 2 型の追加に伴い**期待集合の更新が必要**(5 型へ)。

## 2. 要件 → 資産マップ

| 要件 | 必要な技術要素 | 既存資産 | タグ |
| --- | --- | --- | --- |
| 1.1〜1.6(画像入力) | 3 形式解決・ガード | ImageDecoder + FaceDetector のオーバーロード委譲パターン | 再利用(パターン複製) |
| 2.1〜2.2(入力判別) | NCHW/NHWC・動的軸 640 | ModelIntrospector の入力判別 | 再利用 |
| 2.3〜2.5(出力判別・信頼度) | F=4+C / 5+C 判別・argmax・objectness 合成 | なし(顔専用 ClassifyOutput のみ) | **Missing** |
| 2.6〜2.9(例外契約) | ガード群 | FaceDetector コンストラクタのパターン | 再利用(複製) |
| 3.1〜3.4(クラス名) | COCO 80 名・フォールバック | なし | **Missing** |
| 4.1(閾値フィルタ) | パース時フィルタ | FaceOutputParser のパターン | Missing(物体版が必要) |
| 4.2(クラス単位 NMS) | クラス別グルーピング + NMS | NonMaxSuppression(グルーピングは新規) | 部分再利用 |
| 4.3〜4.7(結果契約) | 降順・空リスト・座標復元・既定値・範囲ガード | Letterbox・FaceDetector パターン | 再利用 |
| 5.1〜5.5(非同期契約) | Task.Run・チェックポイント・volatile | FaceDetector パターン | 再利用(複製) |
| 6.1〜6.5(非機能) | 公開面検査・非回帰 | PublicApiTests(**要更新**)・既存 80 テスト | Constraint |

## 3. 実装方針の選択肢

| 案 | 内容 | トレードオフ | 工数 | リスク |
| --- | --- | --- | --- | --- |
| A: 既存拡張(共通基底抽出) | FaceDetector と ObjectDetector の共通骨格を internal 基底クラス化 | 重複最小。ただし凍結済み unit の FaceDetector を大きく改変し、非回帰リスクと過度な事前抽象化(YAGNI 違反)を招く | M | 中 |
| B: 完全新規(独立複製) | ModelIntrospector も含め物体用を全複製 | 既存に一切触れないが、入力判別・前処理まで重複し drift の温床 | M | 中 |
| C: ハイブリッド(**採用**) | 無状態部品(ImageDecoder/Preprocessor/Letterbox/NMS/入力判別)は再利用。ModelIntrospector に物体用の出力判別を**追加**(既存メソッドの挙動不変)。編成(コンストラクタ・ガード・Task.Run・Dispose)は ObjectDetector 側に薄く複製 | 既存公開契約・既存テストに触れない。編成の複製は各 ~90 行で許容範囲(3 クラス目が必要になった時点で抽象化を再検討) | S〜M | 低 |

- 案 C の縫い目: `ModelIntrospector.Introspect` の入力判別部を private 共通化し、既存 `Introspect`(顔: 構築時に F∈{5,20} を早期検証)と新規 `IntrospectObject`(物体: F≧5 を早期検証)を並置する。`ClassifyOutput`(顔用)は不変のまま、新規 `ClassifyObjectOutput` を追加する。FaceDetector のコードは 1 行も変更しない。

## 4. 4+C / 5+C・転置/標準の判別根拠(一次情報)

- YOLOv5 の ONNX エクスポート出力は **標準形式 `[1, 25200, 85]`**(85 = 4 bbox + 1 objectness + 80 クラス)。出典: https://github.com/ultralytics/ultralytics/issues/751 、 https://docs.ultralytics.com/yolov5/tutorials/model-export
- YOLOv8/v11 の ONNX エクスポート出力は **転置形式 `[1, 84, 8400]`**(84 = 4 bbox + 80 クラス。objectness 列は廃止)。出典: https://github.com/ultralytics/ultralytics/issues/751 、 https://github.com/orgs/ultralytics/discussions/6205
- したがって「**転置形式 = 4 + C(YOLOv8/v11)、標準形式 = 5 + C(YOLOv5)**」をレイアウト由来の判別規則として採用できる(Ultralytics 公式エクスポートの慣習)。
- F と N の識別: 実モデルでは N(候補数。640 入力で 8400 / 25200)が F(≦ 数百)より常に大きい。「**小さい方の次元を F とする**」規則が成立する。テスト fixture もこの前提(N > F)で生成する。
- 顔モデル(F ∈ {5,20})との両義性は、承認済み前提のとおり使用クラス側の解釈を正とする(ObjectDetector は {5,20} を特別扱いしない)。

## 5. COCO 80 クラス名

- Ultralytics 標準の 80 クラス名・順序(person, bicycle, car, ..., toothbrush)を internal 定数として保持する。正本: https://github.com/ultralytics/ultralytics/blob/main/ultralytics/cfg/datasets/coco.yaml
- 配置は `src/Recognizer/Internal/CocoClassNames.cs`(Internal/ 配下の既存慣習に合致)。

## 6. テスト fixture の増設方針

- 既存 builder 関数(定数出力・入力を形式的に消費する最小グラフ)を再利用し、物体用 fixture を追加する。N > F の規則を満たすため N = 60 以上とする。
- 既存 `face_unsupported_f7`([1,7,6])は物体解釈では有効形状になり得るが、顔テスト専用 fixture のため影響しない(使用クラス側解釈の前提どおり)。
