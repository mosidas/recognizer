# face-recognition — 調査ログ(Gap 分析)

design.md の判断根拠。既存コードベース(unit 1・2 完了、main マージ済み、公開 5 型・138 テスト)の拡張。調査は dev-explorer に隔離し、埋め込みモデルの前処理慣習は一次情報で確認した。

## 1. 現状調査(再利用可能な既存資産)

| 資産 | 本 unit での利用 |
| --- | --- |
| `FaceDetector`(public) | **内包して再利用**(§3 の方針決定)。`DetectAsync` は信頼度降順の `IReadOnlyList<FaceDetection>` を返すため「最高信頼度の顔」= 先頭要素で要件 3.1/4.1 に十分 |
| `ImageDecoder` | そのまま再利用(パス/バイト列 → Mat、ガード) |
| `Letterbox.ClampToBounds` | 正方形切り出しの境界クリップに再利用 |
| `ModelIntrospector.IntrospectInput`(private 共通) | 埋め込みモデルの入力判別に再利用(既定サイズのパラメタ化が必要: 検出 640 / 埋め込み 112) |
| `Preprocessor.Preprocess` | **埋め込みには再利用しない**。letterbox(パディング 114)+ /255 正規化は検出モデル向けで、ArcFace 系の慣習(単純リサイズ + (x−127.5)/128)と異なる(§2) |
| `NonMaxSuppression` / `FaceOutputParser` | FaceRecognizer は直接使わない(FaceDetector 内包経由) |
| テスト基盤 | fixture 生成スクリプトの builder 構造・`SquareImage` ヘルパ(輝度指定版へ拡張) |

## 2. 埋め込みモデルの前処理慣習(一次情報)

- InsightFace(ArcFace)公式の ONNX 推論コード: 入力 112x112・**単純リサイズ**・BGR→RGB・正規化 `(x − 127.5) / 128.0`(≈ [-1, 1])。出典: https://github.com/deepinsight/insightface/blob/master/recognition/arcface_torch/onnx_ijbc.py
- OpenVINO Model Zoo の arcface-onnx README: 入力形状 `[1, 3, 112, 112]`(RGB)、出力 `[1, 512]` の埋め込みベクトル。出典: https://github.com/openvinotoolkit/open_model_zoo/blob/master/models/public/face-recognition-resnet100-arcface-onnx/README.md
- **決定**: 埋め込み前処理は「切り出し済み正方形 → モデル入力サイズへ単純リサイズ(`Cv2.Resize`)→ BGR→RGB → `(x − 127.5) / 128`」とする。letterbox は使わない(切り出しが正方形のためアスペクト歪みは生じない)。動的軸時の既定入力サイズは **112x112**(ArcFace 系の標準)。
- 顔アライメント(ランドマークによるアフィン変換)は ArcFace の学習時前提だが、api-spec がスコープ外としているため行わない(要件スコープどおり。実運用の精度は呼び出し側がアライン済み画像を渡すことで改善可能)。

## 3. 実装方針の選択肢

| 案 | 内容 | トレードオフ | 工数 | リスク |
| --- | --- | --- | --- | --- |
| A(**採用**): FaceDetector 内包 | `FaceRecognizer` が `FaceDetector` を所有(コンストラクタで生成・Dispose 連鎖)し、検出は public 契約経由 | 検出パイプライン(ガード・キャンセル・並行安全・座標復元)を凍結済みのテスト済み契約ごと再利用。編成の三重複製を回避。非同期の入れ子(detector の Task を await)は許容 | S | 低 |
| B: internal 部品の直接編成 | dev-explorer の推奨。検出パイプラインを FaceRecognizer 内に三たび複製 | 依存が細かく制御できるが、~90 行の編成複製が 3 例目になり drift リスク増。YAGNI 的にも不利 | M | 中 |
| C: 検出パイプラインの共通基盤化 | FaceDetector/ObjectDetector/FaceRecognizer の共通基底抽出 | 凍結済み unit の改変で非回帰リスク。3 例目でも編成差(2 モデル・結果型)が大きく共通化の利得が小さい | L | 高 |

- 案 A の注意点: `CompareFacesAsync` の引数ガード(image2 の null/空・閾値)は **FaceRecognizer 先頭で同期検証**する(detector へ委譲すると image2 の検証が image1 の検出後になり、同期送出契約に反するため)。

## 4. テスト fixture の増設方針(入力依存の決定論的出力)

既存 fixture は入力非依存の定数出力のため、(a) 2 画像で埋め込みが異なること、(b) 片方の画像だけ顔未検出になること、を検証できない。入力依存かつ決定論的な最小グラフを追加する:

- **埋め込みダミー**: 出力 `[1, 4]` = 各チャネルの空間平均 + 定数 1.0 の連結(`ReduceMean`(空間軸)× 3 + `Concat`)。単色画像なら埋め込みが解析的に計算でき、異なる色 → 異なるベクトル → コサイン類似度の期待値を手計算できる。正規化 `(x−127.5)/128` を通るため、色 (r,g,b) の画像の埋め込みは `[(r−127.5)/128, (g−127.5)/128, (b−127.5)/128, 1.0]`(RGB 順)。
- **入力依存 conf の顔検出ダミー**: 出力 `[1, 5, N]`(F=5)で bbox は定数、conf = 入力全体の平均(`ReduceMean`)。検出前処理(/255)後の平均のため、白画像(255)→ conf≈1.0(検出)、黒画像(0)→ conf≈0(未検出。ただし letterbox パディング 114 が混じる非正方入力では平均が変わる点に注意 — テストは 640x640 の正方画像を使う)。`NoFaceInImage1/2` の分岐検証に必須。
- fixture 一覧(4 + 1 種): ⑰ `embed_nchw_meanrgb_d4`(112x112・NCHW)/ ⑱ `embed_nhwc_meanrgb_d4`(NHWC の詰め順検証)/ ⑲ `embed_dynamic_input_d4`(動的軸 → 112 既定)/ ⑳ `embed_unsupported_rank3`(出力 rank3 → NotSupported)/ ㉑ `face_inputconf_f5`(入力依存 conf)。
- 使用 op(ReduceMean / Concat / Slice / Gather 等)は既存スクリプトで実績があり onnx 1.22 / ORT 1.27 で動作確認済みの範囲。

## 5. 埋め込み出力の判別

- 受理する出力形状: rank 1 `[D]` または rank 2 `[1, D]`(D は静的な正値)。D が動的・rank 3 以上・複数出力は `NotSupportedException`(次元 D は API 契約(要件 3.6)の要であり、構築時に確定できないモデルは対象外とする)。
- 既存 `IntrospectInput` は既定サイズ 640 をハードコードしているため、既定サイズを引数化する(既存呼び出しは 640 を渡し挙動不変)。
