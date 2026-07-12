#!/usr/bin/env python3
"""顔検出テスト用のダミー ONNX fixture 生成スクリプト。

目的
----
`tests/Recognizer.Tests/` の単体・契約テストが Python 非依存で自己完結できるよう、
「入力に依存しない定数出力」を返す最小 ONNX グラフを 6 種類生成する
(design.md §9 テスト戦略の案 A)。出力値はスクリプト内の定数なので、
テストの期待値を決定論的に記述できる。

なぜ入力を形式的に消費するか
----------------------------
出力は定数だが、モデル形式判別(design.md §6 規則 (a)〜(c))は「入力メタデータ」を
読む。入力を全く使わないと最適化で入力がプルーニングされ InputMetadata から
消える可能性があるため、`Mul(input, 0)`(= 全要素 0)→ `ReduceSum`(全軸縮約で
スカラ 0)→ `Add(定数出力, 0)` として入力を形式的に消費し、宣言された 1 入力を
グラフに残す。出力値は入力に依存しない(常に定数)ことは保たれる。

生成する fixture 一覧(design.md §9 の ①〜⑥)
--------------------------------------------
  ① face_nchw_transposed_f5.onnx : NCHW 入力・転置出力 [1, 5, N](F=5、ランドマーク無し)
  ② face_nchw_transposed_f20.onnx: NCHW 入力・転置出力 [1, 20, N](F=20、ランドマーク付き)
  ③ face_nchw_standard_f5.onnx   : NCHW 入力・標準出力 [1, N, 5](F=5)
  ④ face_nhwc_transposed_f5.onnx : NHWC 入力・転置出力 [1, 5, N](F=5)
  ⑤ face_dynamic_input_f5.onnx   : 動的軸入力(H/W が動的)・転置出力 [1, 5, N](640 既定の検証用)
  ⑥ face_unsupported_f7.onnx     : 非対応形式(F=7、NotSupportedException の検証用)

定数出力の候補(= テスト期待値の根拠)
--------------------------------------
bbox は YOLO 慣習の中心形式 (cx, cy, w, h)、座標系はモデル入力 640x640 の
レターボックス空間、信頼度は 0〜1。生の候補は「閾値フィルタ・NMS 抑制・降順整列」を
1 度に検証できるよう、意図的に順不同・重複矩形・低信頼度候補を含める。

  格納順(row/col の候補インデックス順) と (cx, cy, w, h, conf):
    idx0 B  : (300, 300, 60, 60, 0.85)  独立・高信頼
    idx1 A  : (100, 100, 50, 50, 0.95)  最高信頼(NMS 基準)
    idx2 D  : (200, 500, 80, 80, 0.75)  独立
    idx3 A' : (105, 105, 50, 50, 0.90)  A と高 IoU(≈0.68 > 0.5)→ NMS で抑制される
    idx4 C  : (500, 400, 40, 40, 0.60)  既定 conf 閾値 0.7 未満 → フィルタで除外
    idx5 E  : (400, 200, 70, 70, 0.50)  既定 conf 閾値 0.7 未満 → フィルタで除外

  既定閾値(conf=0.7, nms IoU=0.5)での期待結果(信頼度降順):
    A(0.95) → B(0.85) → D(0.75)  の 3 件
    (C・E は閾値未満で除外、A' は A との IoU>0.5 で NMS 抑制、
     生成順が順不同なので降順整列の検証にもなる)

  A と A' の IoU(検証用の内訳):
    A  = x[75,125] y[75,125] 面積 2500
    A' = x[80,130] y[80,130] 面積 2500
    交差 = 45*45 = 2025、和集合 = 2500+2500-2025 = 2975、IoU = 2025/2975 ≈ 0.6807

F=20 のランドマーク(5 点 × [x, y, conf])
------------------------------------------
各候補の bbox 中心付近に決定論的に配置する(値の詳細は landmarks_for() を参照)。
テストではランドマークの有無(F=20 のとき非 null、F=5 のとき null)を主に検証する。

再生成手順
----------
  python3 -m venv /tmp/onnx-venv
  /tmp/onnx-venv/bin/pip install onnx
  /tmp/onnx-venv/bin/python tools/generate_test_models.py

生成物は onnx.checker で全数検証され、各ファイル 100 KB 未満であることを確認する。
onnxruntime による実ロード確認は後続の C# テスト(タスク 4.1)に委ねる。
"""

from __future__ import annotations

import os

import numpy as np
import onnx
from onnx import TensorProto, checker, helper, numpy_helper

# 出力先(このスクリプトの位置からリポジトリルートを解決して固定する)
_SCRIPT_DIR = os.path.dirname(os.path.abspath(__file__))
_REPO_ROOT = os.path.dirname(_SCRIPT_DIR)
FIXTURE_DIR = os.path.join(_REPO_ROOT, "tests", "Recognizer.Tests", "Fixtures")

# onnxruntime 1.27.1 が確実にロードできる保守的な組み合わせ(古めの opset / IR を明示)
OPSET = 17
IR_VERSION = 10

# モデル入力の既定サイズ(レターボックス空間)
INPUT_H = 640
INPUT_W = 640

# 埋め込みモデルの既定入力サイズ(InsightFace 系 112x112。research.md §2)と出力次元
EMBED_H = 112
EMBED_W = 112
EMBED_DIM = 4

# 生の候補 (cx, cy, w, h, conf) — 格納順は意図的に順不同(降順整列の検証のため)
CANDIDATES = [
    (300.0, 300.0, 60.0, 60.0, 0.85),  # B  独立・高信頼
    (100.0, 100.0, 50.0, 50.0, 0.95),  # A  最高信頼(NMS 基準)
    (200.0, 500.0, 80.0, 80.0, 0.75),  # D  独立
    (105.0, 105.0, 50.0, 50.0, 0.90),  # A' A と高 IoU → NMS 抑制
    (500.0, 400.0, 40.0, 40.0, 0.60),  # C  低信頼 → 閾値除外
    (400.0, 200.0, 70.0, 70.0, 0.50),  # E  低信頼 → 閾値除外
]
N = len(CANDIDATES)


def landmarks_for(cx: float, cy: float, w: float, h: float) -> list[float]:
    """F=20 用の 5 点ランドマーク [x, y, conf] × 5 を決定論的に生成する。

    bbox 中心を基準に左目・右目・鼻・口左端・口右端をおおよその顔配置で置く。
    値そのものはテストの主対象ではない(有無の検証が主)ため簡潔な相対配置とする。
    """
    dx = w * 0.2
    up = h * 0.15
    down = h * 0.2
    pts = [
        (cx - dx, cy - up),  # 左目
        (cx + dx, cy - up),  # 右目
        (cx, cy),            # 鼻(中心)
        (cx - dx, cy + down),  # 口左端
        (cx + dx, cy + down),  # 口右端
    ]
    flat: list[float] = []
    for px, py in pts:
        flat.extend([px, py, 0.99])  # 各点 conf は 0.99 固定(C# 側は破棄する)
    return flat


def build_rows(feature_count: int) -> np.ndarray:
    """候補ごとの特徴ベクトル(長さ feature_count)を N 行の 2 次元配列で返す。

    feature_count=5  : [cx, cy, w, h, conf]
    feature_count=20 : [cx, cy, w, h, conf, (lm x,y,conf)×5]
    feature_count=7  : 非対応形式のダミー(bbox+conf の後ろに 0 を 2 個詰める)
    """
    rows = []
    for cx, cy, w, h, conf in CANDIDATES:
        base = [cx, cy, w, h, conf]
        if feature_count == 5:
            row = base
        elif feature_count == 20:
            row = base + landmarks_for(cx, cy, w, h)
        elif feature_count == 7:
            # 判別不能(F ∉ {5,20})を作るための埋め草。値に意味はない。
            row = base + [0.0, 0.0]
        else:
            raise ValueError(f"未対応の feature_count: {feature_count}")
        rows.append(row)
    return np.array(rows, dtype=np.float32)  # 形状 (N, feature_count)


def make_output_tensor(feature_count: int, transposed: bool) -> np.ndarray:
    """出力定数テンソルを (1, F, N)(転置)または (1, N, F)(標準)で返す。"""
    rows = build_rows(feature_count)  # (N, F)
    if transposed:
        data = rows.T  # (F, N)
    else:
        data = rows    # (N, F)
    return data[np.newaxis, :, :].astype(np.float32)  # 先頭に batch 次元


def make_model(
    filename: str,
    layout: str,          # "NCHW" | "NHWC"
    feature_count: int,
    transposed: bool,
    dynamic_input: bool = False,
) -> str:
    """1 つの fixture を構築してファイルに書き出し、絶対パスを返す。"""
    # --- 入力 value_info(形式判別が読むメタデータ) ---
    if layout == "NCHW":
        # [N, 3, H, W]
        if dynamic_input:
            in_shape = [1, 3, "height", "width"]  # H/W を動的軸(dim_param)にする
        else:
            in_shape = [1, 3, INPUT_H, INPUT_W]
    elif layout == "NHWC":
        # [N, H, W, 3]
        if dynamic_input:
            in_shape = [1, "height", "width", 3]
        else:
            in_shape = [1, INPUT_H, INPUT_W, 3]
    else:
        raise ValueError(f"未対応の layout: {layout}")

    input_vi = helper.make_tensor_value_info("images", TensorProto.FLOAT, in_shape)

    # --- 出力定数テンソル ---
    out_data = make_output_tensor(feature_count, transposed)
    out_shape = list(out_data.shape)
    output_vi = helper.make_tensor_value_info("output", TensorProto.FLOAT, out_shape)

    # 初期化子: 定数出力 と 入力消費用のスカラ 0
    const_out_init = numpy_helper.from_array(out_data, name="const_output")
    zero_init = numpy_helper.from_array(
        np.array(0.0, dtype=np.float32), name="zero_scalar"
    )

    # --- 入力を形式的に消費するサブグラフ(出力値は定数のまま) ---
    # Mul(images, 0) → 全要素 0(形状は入力と同じ)
    n_mul = helper.make_node("Mul", ["images", "zero_scalar"], ["zeroed"], name="mul_zero")
    # ReduceSum(全軸縮約, keepdims=0) → スカラ 0
    n_reduce = helper.make_node(
        "ReduceSum", ["zeroed"], ["reduced"], keepdims=0, name="reduce_all"
    )
    # Add(定数出力, スカラ 0) → 定数出力(ブロードキャスト)
    n_add = helper.make_node("Add", ["const_output", "reduced"], ["output"], name="add_zero")

    graph = helper.make_graph(
        [n_mul, n_reduce, n_add],
        f"dummy_{layout}_f{feature_count}",
        [input_vi],
        [output_vi],
        initializer=[const_out_init, zero_init],
    )
    model = helper.make_model(
        graph, opset_imports=[helper.make_operatorsetid("", OPSET)]
    )
    model.ir_version = IR_VERSION
    checker.check_model(model)

    os.makedirs(FIXTURE_DIR, exist_ok=True)
    path = os.path.join(FIXTURE_DIR, filename)
    onnx.save(model, path)
    return path


# ---------------------------------------------------------------------------
# 非対応形式の判別分岐(design.md §6 規則 (a)(f))を Introspect レベルで検証するための
# 追加 fixture。いずれも onnxruntime がロード可能な正当な ONNX だが、顔検出モデルとしては
# 非対応の形状を持ち、ModelIntrospector が NotSupportedException を送出することを検証する。
# ⑤ の tiny_ambiguous_3x3 のみ「両形一致 → NCHW 優先」(規則 (a))の正常系検証用。
# ---------------------------------------------------------------------------

def _consume_input_to_scalar(input_name: str, suffix: str) -> tuple[list, str]:
    """入力を形式的に消費してスカラ 0 を得るノード列と、その出力名を返す。

    最適化で入力がプルーニングされ InputMetadata から消えるのを防ぐため
    (make_model と同じ理由・手法)。
    """
    zeroed = f"zeroed_{suffix}"
    reduced = f"reduced_{suffix}"
    n_mul = helper.make_node(
        "Mul", [input_name, "zero_scalar"], [zeroed], name=f"mul_zero_{suffix}"
    )
    n_reduce = helper.make_node(
        "ReduceSum", [zeroed], [reduced], keepdims=0, name=f"reduce_all_{suffix}"
    )
    return [n_mul, n_reduce], reduced


def make_multi_output_model(filename: str) -> str:
    """出力を 2 個持つモデル(YOLOv3 系のスコープ外境界 = 規則 (f))。"""
    out_data = make_output_tensor(5, transposed=True)  # (1, 5, N)
    out_shape = list(out_data.shape)
    output_vi = helper.make_tensor_value_info("output", TensorProto.FLOAT, out_shape)
    output2_vi = helper.make_tensor_value_info("output2", TensorProto.FLOAT, out_shape)

    input_vi = helper.make_tensor_value_info(
        "images", TensorProto.FLOAT, [1, 3, INPUT_H, INPUT_W]
    )
    const_out_init = numpy_helper.from_array(out_data, name="const_output")
    const_out2_init = numpy_helper.from_array(out_data, name="const_output2")
    zero_init = numpy_helper.from_array(np.array(0.0, dtype=np.float32), name="zero_scalar")

    consume, reduced = _consume_input_to_scalar("images", "a")
    n_add1 = helper.make_node("Add", ["const_output", reduced], ["output"], name="add1")
    n_add2 = helper.make_node("Add", ["const_output2", reduced], ["output2"], name="add2")

    graph = helper.make_graph(
        consume + [n_add1, n_add2],
        "dummy_multi_output",
        [input_vi],
        [output_vi, output2_vi],
        initializer=[const_out_init, const_out2_init, zero_init],
    )
    return _finalize(graph, filename)


def make_multi_input_model(filename: str) -> str:
    """入力を 2 個持つモデル(規則 (a): 入力は 1 個を要求)。"""
    out_data = make_output_tensor(5, transposed=True)
    output_vi = helper.make_tensor_value_info(
        "output", TensorProto.FLOAT, list(out_data.shape)
    )
    input_vi = helper.make_tensor_value_info(
        "images", TensorProto.FLOAT, [1, 3, INPUT_H, INPUT_W]
    )
    input2_vi = helper.make_tensor_value_info(
        "images2", TensorProto.FLOAT, [1, 3, INPUT_H, INPUT_W]
    )
    const_out_init = numpy_helper.from_array(out_data, name="const_output")
    zero_init = numpy_helper.from_array(np.array(0.0, dtype=np.float32), name="zero_scalar")

    consume_a, reduced_a = _consume_input_to_scalar("images", "a")
    consume_b, reduced_b = _consume_input_to_scalar("images2", "b")
    n_add1 = helper.make_node("Add", ["const_output", reduced_a], ["t1"], name="add1")
    n_add2 = helper.make_node("Add", ["t1", reduced_b], ["output"], name="add2")

    graph = helper.make_graph(
        consume_a + consume_b + [n_add1, n_add2],
        "dummy_multi_input",
        [input_vi, input2_vi],
        [output_vi],
        initializer=[const_out_init, zero_init],
    )
    return _finalize(graph, filename)


def make_custom_input_model(filename: str, in_shape: list, graph_name: str) -> str:
    """入力形状のみ差し替えた単一入出力モデル(rank≠4・チャネル不明・両形一致の検証用)。

    出力は正常な転置 F=5。入力形状で判別分岐を切り替える。
    """
    out_data = make_output_tensor(5, transposed=True)
    output_vi = helper.make_tensor_value_info(
        "output", TensorProto.FLOAT, list(out_data.shape)
    )
    input_vi = helper.make_tensor_value_info("images", TensorProto.FLOAT, in_shape)
    const_out_init = numpy_helper.from_array(out_data, name="const_output")
    zero_init = numpy_helper.from_array(np.array(0.0, dtype=np.float32), name="zero_scalar")

    consume, reduced = _consume_input_to_scalar("images", "a")
    n_add = helper.make_node("Add", ["const_output", reduced], ["output"], name="add1")

    graph = helper.make_graph(
        consume + [n_add],
        graph_name,
        [input_vi],
        [output_vi],
        initializer=[const_out_init, zero_init],
    )
    return _finalize(graph, filename)


def _finalize(graph, filename: str) -> str:
    """グラフをモデル化・検証して書き出し、絶対パスを返す(make_model と同一手順)。"""
    model = helper.make_model(graph, opset_imports=[helper.make_operatorsetid("", OPSET)])
    model.ir_version = IR_VERSION
    checker.check_model(model)
    os.makedirs(FIXTURE_DIR, exist_ok=True)
    path = os.path.join(FIXTURE_DIR, filename)
    onnx.save(model, path)
    return path


# ---------------------------------------------------------------------------
# 物体検出用 fixture(object-detection design.md §9 の ⑫〜⑮)。
# 特徴ベクトルがクラススコア列を持ち face fixture と構造が異なるため専用 builder を置く。
# 既存 make_model / CANDIDATES / 追加 fixture のロジックには一切触れないので、
# 既存 11 fixture のバイト列は不変(生成後に SHA で確認する)。
#
# 判別規則(design §6 (o-d)〜(o-g)):N > F(小さい方が F)。転置 [1,F,N] = 4+C(YOLOv8/v11、
# objectness 無し・信頼度=最大クラススコア)、標準 [1,N,F] = 5+C(YOLOv5、信頼度=objectness×最大クラススコア)。
# 座標は 640x640 レターボックス空間・中心形式 (cx, cy, w, h)。既定閾値は conf=0.5 / NMS IoU=0.5。
# 期待結果の正本は tests/Recognizer.Tests/Fixtures/README.md。
# ---------------------------------------------------------------------------

# 埋め草クラススコア(既定閾値 0.5 未満に確実に収め、フィルタで除外させる)
_OBJ_FILLER_SCORE = 0.01


def _object_row(
    cx: float,
    cy: float,
    w: float,
    h: float,
    class_count: int,
    class_scores: dict[int, float],
    objectness: float | None = None,
) -> list[float]:
    """物体候補 1 件の特徴ベクトルを返す。

    転置(4+C): [cx, cy, w, h, cls_0..cls_{C-1}]
    標準(5+C): [cx, cy, w, h, objectness, cls_0..cls_{C-1}]
    class_scores は {クラス添字: スコア} の疎な指定。未指定クラスは _OBJ_FILLER_SCORE で埋める
    (argmax 検証のため最大でないクラスのスコアも 0 より大きくする)。
    """
    scores = [class_scores.get(i, _OBJ_FILLER_SCORE) for i in range(class_count)]
    head = [float(cx), float(cy), float(w), float(h)]
    if objectness is not None:
        head = head + [float(objectness)]
    return head + scores


def _object_filler_row(idx: int, class_count: int, has_objectness: bool) -> list[float]:
    """閾値で確実に除外される埋め草候補。座標は候補ごとに散らして重複扱いを避ける。"""
    cx = 10.0 + (idx % 50)
    cy = 10.0 + (idx % 50)
    head = [cx, cy, 8.0, 8.0]
    if has_objectness:
        head = head + [_OBJ_FILLER_SCORE]
    return head + [_OBJ_FILLER_SCORE] * class_count


# ⑫ object_nchw_transposed_4c3: [1, 7, 60](転置、F=7=4+3、C=3)。信頼度=最大クラススコア。
#   P0/P1 は同一クラス(class0)の高 IoU ペア(P1 が NMS 抑制)、P2 は P0 と同座標の別クラス
#   (class1 → 両方残る = 要件 4.2 の核心)、P3 は独立(class2)、P4 は閾値未満で除外。
OBJECT_T_4C3 = [
    _object_row(100, 100, 50, 50, 3, {0: 0.90, 1: 0.10, 2: 0.05}),  # P0 class0 conf 0.90
    _object_row(105, 105, 50, 50, 3, {0: 0.80, 1: 0.05, 2: 0.02}),  # P1 class0 conf 0.80(P0 と IoU≈0.68>0.5 → 抑制)
    _object_row(100, 100, 50, 50, 3, {0: 0.10, 1: 0.85, 2: 0.05}),  # P2 class1 conf 0.85(P0 と同座標・別クラス → 残る)
    _object_row(400, 400, 60, 60, 3, {0: 0.05, 1: 0.10, 2: 0.70}),  # P3 class2 conf 0.70 独立
    _object_row(300, 300, 40, 40, 3, {0: 0.30, 1: 0.20, 2: 0.10}),  # P4 conf 0.30 < 0.5 → 除外
]

# ⑬ object_nchw_standard_5c3: [1, 60, 8](標準、F=8=5+3、C=3、YOLOv5)。信頼度=objectness×最大クラススコア。
#   Q2 は積が閾値を跨いで下回る(0.60×0.70=0.42<0.5)候補。
OBJECT_S_5C3 = [
    _object_row(100, 100, 50, 50, 3, {0: 0.80, 1: 0.10, 2: 0.05}, objectness=0.90),  # Q0 class0 conf 0.90×0.80=0.72
    _object_row(400, 200, 70, 70, 3, {0: 0.10, 1: 0.75, 2: 0.05}, objectness=0.80),  # Q1 class1 conf 0.80×0.75=0.60
    _object_row(500, 500, 50, 50, 3, {0: 0.70, 1: 0.20, 2: 0.05}, objectness=0.60),  # Q2 conf 0.60×0.70=0.42 < 0.5 → 除外
]

# ⑭ object_transposed_coco80: [1, 84, 100](転置、C=80)。classNames 省略時に COCO 名が解決されることの検証。
#   ClassId 0=person, 2=car, 15=cat(Ultralytics coco.yaml 順)。
OBJECT_T_COCO80 = [
    _object_row(100, 100, 50, 50, 80, {0: 0.95}),   # R0 person(class0) conf 0.95
    _object_row(300, 300, 80, 80, 80, {2: 0.88}),   # R1 car(class2)    conf 0.88
    _object_row(500, 200, 60, 60, 80, {15: 0.75}),  # R2 cat(class15)   conf 0.75
]


def make_object_model(
    filename: str,
    class_count: int,
    has_objectness: bool,
    candidate_count: int,
    meaningful: list[list[float]],
    graph_name: str,
    dynamic_output: bool = False,
) -> str:
    """物体用 fixture を 1 つ生成する(入力は NCHW [1,3,640,640] 固定)。

    has_objectness=False → 転置形式 [1, F, N](F=4+C)。
    has_objectness=True  → 標準形式 [1, N, F](F=5+C)。
    meaningful を先頭に置き、残りは埋め草で candidate_count 件まで補う。
    dynamic_output=True → 出力 N 軸を動的入力次元に依存させ、ORT の静的 shape 推論を不能にする
        (規則 o-g の保留分岐を実行させるため)。宣言形状の N 軸は dim_param("num")。
    """
    feature_count = (5 if has_objectness else 4) + class_count
    rows = list(meaningful)
    for idx in range(len(meaningful), candidate_count):
        rows.append(_object_filler_row(idx, class_count, has_objectness))
    arr = np.array(rows, dtype=np.float32)  # (N, F)
    assert arr.shape == (candidate_count, feature_count), (
        f"{filename}: 期待形状 (N,F)=({candidate_count},{feature_count}) 実際 {arr.shape}"
    )
    # 標準(5+C)は [1, N, F]、転置(4+C)は [1, F, N]。いずれも N > F(規則 o-d)。
    if has_objectness:
        out_data = arr[np.newaxis, :, :]        # (1, N, F)
        n_axis = 1
    else:
        out_data = arr.T[np.newaxis, :, :]      # (1, F, N)
        n_axis = 2
    out_data = np.ascontiguousarray(out_data, dtype=np.float32)

    # 宣言形状: dynamic_output なら N 軸を dim_param("num")にし、構築時の分類保留(規則 o-g)を検証可能にする。
    declared_shape: list = list(out_data.shape)
    if dynamic_output:
        declared_shape[n_axis] = "num"
    output_vi = helper.make_tensor_value_info(
        "output", TensorProto.FLOAT, declared_shape
    )
    # 動的出力 fixture は入力 H/W も動的にする(下記 Why 参照)。IntrospectObject は規則 (c) で 640 に既定化。
    in_shape = [1, 3, "h", "w"] if dynamic_output else [1, 3, INPUT_H, INPUT_W]
    input_vi = helper.make_tensor_value_info("images", TensorProto.FLOAT, in_shape)
    const_out_init = numpy_helper.from_array(out_data, name="const_output")
    initializers = [const_out_init]

    if not dynamic_output:
        # zero_scalar は入力を形式的に消費する Mul→ReduceSum 経路でのみ使う。
        initializers.append(numpy_helper.from_array(np.array(0.0, dtype=np.float32), name="zero_scalar"))
        consume, reduced = _consume_input_to_scalar("images", "a")
        nodes = consume + [
            helper.make_node("Add", ["const_output", reduced], ["output"], name="add1")
        ]
    else:
        # 規則 (o-g) 検証用に出力 N 軸を「ORT が構築時に確定できない」動的軸にする。
        # Why not 定数由来: ORT は既定の graph 最適化(constant-fold + shape inference)で、
        # 静的に辿れる出力形状を具体値へ解決してしまう(dim_param 宣言や Reshape でも解決される)。
        # そこで N 軸長を動的入力次元 h に依存させる: Slice(const_output, ends=h, axes=[2])。
        # Slice は ends が次元長を超えると clamp するため、実行時 h=640 → [1,7,60](⑫ と同一定数値)。
        # ends が実行時値(h 由来)なので shape inference は N 軸を未知(-1)のままにする。
        # 入力は Shape 経由で実消費されるため形式的消費(Mul→ReduceSum)は不要。
        initializers += [
            numpy_helper.from_array(np.array([2], dtype=np.int64), name="dyn_h_index"),
            numpy_helper.from_array(np.array([0], dtype=np.int64), name="dyn_starts"),
            numpy_helper.from_array(np.array([n_axis], dtype=np.int64), name="dyn_axes"),
        ]
        nodes = [
            helper.make_node("Shape", ["images"], ["dyn_in_shape"], name="dyn_shape"),
            # 入力 H 軸(index 2、動的 'h')を [1] 形状で取り出す → 実行時 640・構築時 symbolic。
            helper.make_node(
                "Gather", ["dyn_in_shape", "dyn_h_index"], ["dyn_ends"], axis=0, name="dyn_gather_h"
            ),
            # ends=h(=640)は N 軸長 60 を超えるため clamp され、実行時 [1,7,60]。
            helper.make_node(
                "Slice",
                ["const_output", "dyn_starts", "dyn_ends", "dyn_axes"],
                ["output"],
                name="dyn_slice",
            ),
        ]

    graph = helper.make_graph(
        nodes,
        graph_name,
        [input_vi],
        [output_vi],
        initializer=initializers,
    )
    return _finalize(graph, filename)


# ---------------------------------------------------------------------------
# 顔認証(face-recognition)用 fixture(design.md §9 の ⑰〜㉓)。
# 既存の入力非依存(定数出力)fixture では「2 画像で埋め込みが異なる」「片方だけ顔未検出」
# を検証できないため、入力依存かつ決定論的な最小グラフを追加する(research.md §4)。
# 既存 make_model / make_object_model 等の関数・CANDIDATES には一切触れないので、
# 既存 16 fixture のバイト列は不変(生成後に git status で確認する)。
# ---------------------------------------------------------------------------


def make_embedding_model(
    filename: str,
    layout: str,          # "NCHW" | "NHWC"
    dynamic_input: bool = False,
    graph_name: str = "dummy_embed",
) -> str:
    """埋め込みダミー(⑰⑱⑲)。出力 [1, 4] = [mean(R), mean(G), mean(B), 1.0]。

    入力は前処理(BGR→RGB・(x−127.5)/128)済みのテンソルなので、各チャネルの空間平均が
    そのまま正規化後の値になる。単色 (r,g,b) 画像なら埋め込みは
    [(r−127.5)/128, (g−127.5)/128, (b−127.5)/128, 1.0](RGB 順)で解析的に計算でき、
    2 色のコサイン類似度の期待値を手計算できる(design §9 ⑰)。
    末尾 1.0 は ReduceMean で消えない定数成分を持たせ、ゼロベクトル退化を避けるため。
    """
    if layout == "NCHW":
        # [N, 3, H, W]。空間軸は H(2)・W(3)、チャネル軸(1)を残す。
        in_shape = [1, 3, "height", "width"] if dynamic_input else [1, 3, EMBED_H, EMBED_W]
        spatial_axes = [2, 3]
    elif layout == "NHWC":
        # [N, H, W, 3]。空間軸は H(1)・W(2)、チャネル軸(3)を残す。
        in_shape = [1, "height", "width", 3] if dynamic_input else [1, EMBED_H, EMBED_W, 3]
        spatial_axes = [1, 2]
    else:
        raise ValueError(f"未対応の layout: {layout}")

    input_vi = helper.make_tensor_value_info("images", TensorProto.FLOAT, in_shape)
    output_vi = helper.make_tensor_value_info("output", TensorProto.FLOAT, [1, EMBED_DIM])

    # 末尾成分 1.0(定数)。[1,1] にして ch_mean [1,3] と axis=1 で連結し [1,4] にする。
    one_const = numpy_helper.from_array(
        np.array([[1.0]], dtype=np.float32), name="one_const"
    )
    # 空間軸で平均 → [1, 3](チャネル平均)。opset 17 の ReduceMean は axes を属性で取る。
    n_mean = helper.make_node(
        "ReduceMean", ["images"], ["ch_mean"], axes=spatial_axes, keepdims=0, name="ch_mean"
    )
    n_concat = helper.make_node(
        "Concat", ["ch_mean", "one_const"], ["output"], axis=1, name="concat_one"
    )
    graph = helper.make_graph(
        [n_mean, n_concat],
        graph_name,
        [input_vi],
        [output_vi],
        initializer=[one_const],
    )
    return _finalize(graph, filename)


def make_embedding_unsupported_const(
    filename: str, out_data: np.ndarray, graph_name: str
) -> str:
    """非対応の静的形状埋め込み(⑳ rank3・㉒ rank2 先頭次元≠1)。

    出力は定数だが判別が読むのは OutputMetadata の形状なので、入力を形式的に消費して
    (make_model と同じ Mul→ReduceSum→Add 手法で入力プルーニングを防ぎつつ)定数を返す。
    ⑳: out_data=[1,4,4](rank3)、㉒: out_data=[2,4](rank2・先頭次元 2)。規則 (e-d)。
    """
    input_vi = helper.make_tensor_value_info(
        "images", TensorProto.FLOAT, [1, 3, EMBED_H, EMBED_W]
    )
    output_vi = helper.make_tensor_value_info(
        "output", TensorProto.FLOAT, list(out_data.shape)
    )
    const_out_init = numpy_helper.from_array(out_data, name="const_output")
    zero_init = numpy_helper.from_array(np.array(0.0, dtype=np.float32), name="zero_scalar")
    consume, reduced = _consume_input_to_scalar("images", "a")
    n_add = helper.make_node("Add", ["const_output", reduced], ["output"], name="add1")
    graph = helper.make_graph(
        consume + [n_add],
        graph_name,
        [input_vi],
        [output_vi],
        initializer=[const_out_init, zero_init],
    )
    return _finalize(graph, filename)


def make_embedding_dynamic_dim(filename: str, graph_name: str) -> str:
    """非対応の動的次元埋め込み(㉓)。出力 [1, D] の D が動的軸(≤0)。規則 (e-d)。

    ORT の shape inference が D を具体値へ解決しないよう、object の動的出力(⑯)と同じ手法で
    D 軸長を動的入力次元に依存させる:Slice(const[1,4], ends=Gather(Shape(images),[2]), axes=[1])。
    ends=H(実行時 112)は D 軸長 4 を超えるため clamp され実行時 [1,4]、shape inference は D を未知(-1)にする。
    宣言形状の D 軸は dim_param("dim")。**入力 H/W も動的**にしないと Shape が定数畳み込みされ
    D が静的 4 に解決されてしまう(⑯ と同じ理由)。入力の動的軸は判別上 112 既定に落ちるため無害。
    """
    input_vi = helper.make_tensor_value_info(
        "images", TensorProto.FLOAT, [1, 3, "height", "width"]
    )
    output_vi = helper.make_tensor_value_info("output", TensorProto.FLOAT, [1, "dim"])
    const_out_init = numpy_helper.from_array(
        np.array([[0.1, 0.2, 0.3, 0.4]], dtype=np.float32), name="const_output"
    )  # [1, 4]
    initializers = [
        const_out_init,
        numpy_helper.from_array(np.array([2], dtype=np.int64), name="dyn_h_index"),
        numpy_helper.from_array(np.array([0], dtype=np.int64), name="dyn_starts"),
        numpy_helper.from_array(np.array([1], dtype=np.int64), name="dyn_axes"),
    ]
    nodes = [
        helper.make_node("Shape", ["images"], ["dyn_in_shape"], name="dyn_shape"),
        helper.make_node(
            "Gather", ["dyn_in_shape", "dyn_h_index"], ["dyn_ends"], axis=0, name="dyn_gather_h"
        ),
        helper.make_node(
            "Slice",
            ["const_output", "dyn_starts", "dyn_ends", "dyn_axes"],
            ["output"],
            name="dyn_slice",
        ),
    ]
    graph = helper.make_graph(nodes, graph_name, [input_vi], [output_vi], initializer=initializers)
    return _finalize(graph, filename)


def make_face_inputconf_model(filename: str, graph_name: str) -> str:
    """入力依存 conf の顔検出ダミー(㉑)。出力 [1, 5, N] 転置・bbox 定数・conf = 入力平均。

    検出前処理(/255・letterbox)後の入力全体平均を全候補の conf に流し込む。640x640 の
    単色画像で: 白(255)→ /255 = 1.0 → conf≈1.0(検出)、黒(0)→ conf≈0(未検出)。
    NoFaceInImage1/2(4.3,4.4)・抽出未検出(3.5)の分岐検証に使う(research.md §4)。
    N=6 > F=5 にして判別が転置 [1,5,N](F=5・ランドマーク無し)と確定できるようにする。
    bbox は全候補同一(NMS で 1 件に収れん)、中心 (320,320)・幅高 200 の 640 空間内の矩形。
    """
    n_cand = 6
    input_vi = helper.make_tensor_value_info(
        "images", TensorProto.FLOAT, [1, 3, INPUT_H, INPUT_W]
    )
    output_vi = helper.make_tensor_value_info(
        "output", TensorProto.FLOAT, [1, 5, n_cand]
    )
    # bbox 行 [1, 4, N](cx, cy, w, h を全候補で同値)。
    bbox = np.zeros((1, 4, n_cand), dtype=np.float32)
    bbox[0, 0, :] = 320.0  # cx
    bbox[0, 1, :] = 320.0  # cy
    bbox[0, 2, :] = 200.0  # w
    bbox[0, 3, :] = 200.0  # h
    bbox_init = numpy_helper.from_array(bbox, name="bbox_const")
    ones_init = numpy_helper.from_array(
        np.ones((1, 1, n_cand), dtype=np.float32), name="conf_ones"
    )
    # ReduceMean(全軸) → スカラ(入力平均)。conf 行 = ones × 平均 → [1,1,N]。
    n_mean = helper.make_node(
        "ReduceMean", ["images"], ["mean_scalar"], keepdims=0, name="input_mean"
    )
    n_conf = helper.make_node(
        "Mul", ["conf_ones", "mean_scalar"], ["conf_row"], name="conf_row"
    )
    n_concat = helper.make_node(
        "Concat", ["bbox_const", "conf_row"], ["output"], axis=1, name="concat_conf"
    )
    graph = helper.make_graph(
        [n_mean, n_conf, n_concat],
        graph_name,
        [input_vi],
        [output_vi],
        initializer=[bbox_init, ones_init],
    )
    return _finalize(graph, filename)


def main() -> None:
    specs = [
        # (ファイル名, layout, F, transposed, dynamic_input)
        ("face_nchw_transposed_f5.onnx", "NCHW", 5, True, False),    # ①
        ("face_nchw_transposed_f20.onnx", "NCHW", 20, True, False),  # ②
        ("face_nchw_standard_f5.onnx", "NCHW", 5, False, False),     # ③
        ("face_nhwc_transposed_f5.onnx", "NHWC", 5, True, False),    # ④
        ("face_dynamic_input_f5.onnx", "NCHW", 5, True, True),       # ⑤
        ("face_unsupported_f7.onnx", "NCHW", 7, True, False),        # ⑥
    ]

    print(f"出力先: {FIXTURE_DIR}")
    for filename, layout, f, transposed, dynamic in specs:
        path = make_model(filename, layout, f, transposed, dynamic)
        size = os.path.getsize(path)
        assert size < 100 * 1024, f"{filename} が 100 KB 以上: {size} bytes"
        print(f"  生成: {filename}  ({size} bytes)  layout={layout} F={f} "
              f"transposed={transposed} dynamic={dynamic}")

    # 追加 fixture(Introspect の非対応/両形一致分岐の検証用。design §6 規則 (a)(f))
    extra = [
        make_multi_output_model("face_multi_output.onnx"),        # ⑦ 出力 2 個 → 非対応
        make_custom_input_model(
            "face_rank3_input.onnx", [1, 3, INPUT_W], "dummy_rank3_input"
        ),                                                        # ⑧ 入力 rank3 → 非対応
        make_custom_input_model(
            "face_channel_unknown.onnx", [1, 4, INPUT_H, INPUT_W], "dummy_channel_unknown"
        ),                                                        # ⑨ チャネル軸不明 → 非対応
        make_multi_input_model("face_multi_input.onnx"),          # ⑩ 入力 2 個 → 非対応
        make_custom_input_model(
            "face_tiny_ambiguous_3x3.onnx", [1, 3, 3, 3], "dummy_ambiguous_3x3"
        ),                                                        # ⑪ 両形一致 → NCHW 優先(正常系)
    ]
    for path in extra:
        size = os.path.getsize(path)
        assert size < 100 * 1024, f"{os.path.basename(path)} が 100 KB 以上: {size} bytes"
        print(f"  生成(追加): {os.path.basename(path)}  ({size} bytes)")

    # 物体用 fixture(object-detection design §9 ⑫〜⑮)。
    object_specs = [
        # (ファイル名, C, has_objectness, N, meaningful, graph_name)
        ("object_nchw_transposed_4c3.onnx", 3, False, 60, OBJECT_T_4C3, "dummy_object_t_4c3"),   # ⑫
        ("object_nchw_standard_5c3.onnx", 3, True, 60, OBJECT_S_5C3, "dummy_object_s_5c3"),       # ⑬
        ("object_transposed_coco80.onnx", 80, False, 100, OBJECT_T_COCO80, "dummy_object_coco80"),  # ⑭
        ("object_unsupported_f4.onnx", 0, False, 60, [], "dummy_object_f4"),                      # ⑮
    ]
    for filename, c, has_obj, n, meaningful, graph_name in object_specs:
        path = make_object_model(filename, c, has_obj, n, meaningful, graph_name)
        size = os.path.getsize(path)
        assert size < 100 * 1024, f"{filename} が 100 KB 以上: {size} bytes"
        f = (5 if has_obj else 4) + c
        print(f"  生成(物体): {filename}  ({size} bytes)  F={f} C={c} N={n} "
              f"{'標準 5+C' if has_obj else '転置 4+C'}")

    # ⑯ object_dynamic_output_4c3: 宣言形状 [1, 7, "num"](N 軸が dim_param)。
    # 構築時は分類を保留し例外を投げずに DetectionModelSpec を返す分岐(規則 o-g)を検証する。
    # 実行時実形状は [1, 7, 60] で ⑫ と同じ定数出力を流用(転置 4+C=3)。
    dyn_path = make_object_model(
        "object_dynamic_output_4c3.onnx", 3, False, 60, OBJECT_T_4C3,
        "dummy_object_dyn_4c3", dynamic_output=True,
    )
    dyn_size = os.path.getsize(dyn_path)
    assert dyn_size < 100 * 1024, f"object_dynamic_output_4c3.onnx が 100 KB 以上: {dyn_size} bytes"
    print(f"  生成(物体・動的出力): object_dynamic_output_4c3.onnx  ({dyn_size} bytes)  "
          f"宣言 [1,7,'num'] 実形状 [1,7,60]")

    # 顔認証用 fixture(face-recognition design §9 ⑰〜㉓)。
    # 埋め込み系(⑰⑱⑲)は入力依存の決定論出力、非対応系(⑳㉒㉓)は規則 (e-d) の分岐行使、
    # ㉑ は入力平均を conf にした顔検出ダミー。
    embed_specs = [
        # (ファイル名, layout, dynamic_input, graph_name)
        ("embed_nchw_meanrgb_d4.onnx", "NCHW", False, "dummy_embed_nchw"),   # ⑰
        ("embed_nhwc_meanrgb_d4.onnx", "NHWC", False, "dummy_embed_nhwc"),   # ⑱
        ("embed_dynamic_input_d4.onnx", "NCHW", True, "dummy_embed_dyn"),    # ⑲
    ]
    for filename, layout, dynamic, graph_name in embed_specs:
        path = make_embedding_model(filename, layout, dynamic, graph_name)
        size = os.path.getsize(path)
        assert size < 100 * 1024, f"{filename} が 100 KB 以上: {size} bytes"
        print(f"  生成(埋め込み): {filename}  ({size} bytes)  layout={layout} "
              f"D={EMBED_DIM} dynamic={dynamic}")

    embed_unsupported = [
        # ⑳ 出力 [1,4,4](rank3)
        make_embedding_unsupported_const(
            "embed_unsupported_rank3.onnx",
            np.zeros((1, EMBED_DIM, EMBED_DIM), dtype=np.float32),
            "dummy_embed_rank3",
        ),
        # ㉒ 出力 [2,4](rank2・先頭次元 2)
        make_embedding_unsupported_const(
            "embed_unsupported_rank2_batch2.onnx",
            np.zeros((2, EMBED_DIM), dtype=np.float32),
            "dummy_embed_rank2_batch2",
        ),
        # ㉓ 出力 [1,D] の D が動的
        make_embedding_dynamic_dim("embed_unsupported_dynamic_dim.onnx", "dummy_embed_dyn_dim"),
    ]
    for path in embed_unsupported:
        size = os.path.getsize(path)
        assert size < 100 * 1024, f"{os.path.basename(path)} が 100 KB 以上: {size} bytes"
        print(f"  生成(埋め込み非対応): {os.path.basename(path)}  ({size} bytes)")

    # ㉑ 入力依存 conf の顔検出ダミー(出力 [1,5,6] 転置)。
    inputconf_path = make_face_inputconf_model("face_inputconf_f5.onnx", "dummy_face_inputconf")
    inputconf_size = os.path.getsize(inputconf_path)
    assert inputconf_size < 100 * 1024, f"face_inputconf_f5.onnx が 100 KB 以上: {inputconf_size} bytes"
    print(f"  生成(入力依存 conf): face_inputconf_f5.onnx  ({inputconf_size} bytes)  出力 [1,5,6]")

    print("全 fixture の生成と onnx.checker 検証が完了しました。")


if __name__ == "__main__":
    main()
