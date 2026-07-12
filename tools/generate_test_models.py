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

    print("全 fixture の生成と onnx.checker 検証が完了しました。")


if __name__ == "__main__":
    main()
