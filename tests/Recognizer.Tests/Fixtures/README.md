# テスト用 ONNX fixture

`Recognizer.Tests` が **Python 非依存で自己完結**するためのダミー ONNX モデル群
(design.md §9 テスト戦略の案 A)。各モデルは「入力に依存しない定数出力」を返す
最小グラフで、テストの期待値を決定論的に記述できる。

> これらは実推論用モデルではなくテスト資産(fixture)である。`.gitignore` の
> `models/*.onnx`(実モデル置き場)の対象外として、生成物をリポジトリにコミットする。

## fixture 一覧

| # | ファイル | 入力レイアウト | 入力形状 | 出力形状 | F | 用途(検証観点) |
| - | -------- | -------------- | -------- | -------- | - | ---------------- |
| ① | `face_nchw_transposed_f5.onnx` | NCHW | `[1,3,640,640]` | `[1,5,N]` 転置 | 5 | 標準経路(ランドマーク無し・Landmarks=null) |
| ② | `face_nchw_transposed_f20.onnx` | NCHW | `[1,3,640,640]` | `[1,20,N]` 転置 | 20 | ランドマーク付き(FaceLandmarks を含む) |
| ③ | `face_nchw_standard_f5.onnx` | NCHW | `[1,3,640,640]` | `[1,N,5]` 標準 | 5 | 標準形式の出力パース |
| ④ | `face_nhwc_transposed_f5.onnx` | NHWC | `[1,640,640,3]` | `[1,5,N]` 転置 | 5 | NHWC 入力レイアウトの判別・前処理 |
| ⑤ | `face_dynamic_input_f5.onnx` | NCHW(動的) | `[1,3,H,W]` | `[1,5,N]` 転置 | 5 | 動的軸入力 → 640x640 既定の適用 |
| ⑥ | `face_unsupported_f7.onnx` | NCHW | `[1,3,640,640]` | `[1,7,N]` | 7 | 非対応形式 → `NotSupportedException` |

- N(候補数)= 6。入力名は `images`、出力名は `output`。
- opset 17 / IR version 10(onnxruntime 1.27.x が確実にロードできる保守的な組み合わせ)。

### 追加 fixture(判別分岐の検証用)

`ModelIntrospector` の非対応判別(design.md §6 規則 (a)(f))と「両形一致 → NCHW 優先」
(規則 (a))を Introspect レベルで独立に検証するための追加ダミー。いずれも onnxruntime が
ロード可能な正当な ONNX だが、顔検出モデルとしては非対応の入出力形状を持つ(⑪ を除く)。

| # | ファイル | 入力形状 | 出力 | 検証観点(期待) |
| - | -------- | -------- | ---- | ---------------- |
| ⑦ | `face_multi_output.onnx` | `[1,3,640,640]` | `output` + `output2` の 2 出力 | 複数出力 → `NotSupportedException`(規則 f) |
| ⑧ | `face_rank3_input.onnx` | `[1,3,640]`(rank3) | `[1,5,6]` | 入力 rank≠4 → `NotSupportedException`(規則 a) |
| ⑨ | `face_channel_unknown.onnx` | `[1,4,640,640]` | `[1,5,6]` | チャネル軸(値 3)不明 → `NotSupportedException`(規則 a) |
| ⑩ | `face_multi_input.onnx` | `images` + `images2` の 2 入力 | `[1,5,6]` | 複数入力 → `NotSupportedException`(規則 a) |
| ⑪ | `face_tiny_ambiguous_3x3.onnx` | `[1,3,3,3]`(両形一致) | `[1,5,6]` | NCHW を優先(正常系。規則 a) |

- 出力 rank≠3・先頭次元≠1・F∉{5,20} の分岐は純粋関数 `ClassifyOutput` のリテラル形状で検証する(fixture 不要)。

## 定数出力の候補(= テスト期待値の根拠)

bbox は YOLO 慣習の中心形式 `(cx, cy, w, h)`、座標系はモデル入力 640x640 の
レターボックス空間、信頼度は 0〜1。生の候補は「閾値フィルタ・NMS 抑制・降順整列」を
1 度に検証できるよう、意図的に順不同・重複矩形・低信頼度候補を含む。

| 格納順 | ラベル | (cx, cy, w, h, conf) | 役割 |
| ------ | ------ | -------------------- | ---- |
| 0 | B | (300, 300, 60, 60, **0.85**) | 独立・高信頼 |
| 1 | A | (100, 100, 50, 50, **0.95**) | 最高信頼(NMS 基準) |
| 2 | D | (200, 500, 80, 80, **0.75**) | 独立 |
| 3 | A' | (105, 105, 50, 50, **0.90**) | A と高 IoU(≈0.68 > 0.5)→ NMS で抑制 |
| 4 | C | (500, 400, 40, 40, **0.60**) | conf 閾値 0.7 未満 → フィルタで除外 |
| 5 | E | (400, 200, 70, 70, **0.50**) | conf 閾値 0.7 未満 → フィルタで除外 |

**既定閾値(conf=0.7, NMS IoU=0.5)での期待結果(信頼度降順)**:
`A(0.95) → B(0.85) → D(0.75)` の 3 件。

- C・E は閾値未満で除外、A' は A との IoU>0.5 で NMS 抑制。
- 生成順が順不同なので降順整列の検証にもなる。

A と A' の IoU 内訳: A=x[75,125] y[75,125](面積 2500)、A'=x[80,130] y[80,130]
(面積 2500)、交差 45×45=2025、和集合 2975、IoU ≈ **0.6807**。

F=20 のランドマーク(5 点 × [x, y, conf])は各 bbox 中心付近に決定論的に配置される
(値の詳細は生成スクリプト `landmarks_for()` を参照)。テストでは有無の検証が主。

## 再生成手順

```bash
python3 -m venv /tmp/onnx-venv
/tmp/onnx-venv/bin/pip install onnx
/tmp/onnx-venv/bin/python tools/generate_test_models.py
```

生成ロジックと期待値の正本は `tools/generate_test_models.py`(docstring に詳細)。
生成物は onnx.checker で全数検証され、各ファイルは 100 KB 未満(実際は 1 KB 未満)。
