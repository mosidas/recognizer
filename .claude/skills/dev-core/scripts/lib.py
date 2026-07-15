"""dev-core 共通ロジック。

状態機械エンジン(state.py)と静的チェッカ(check.py)が共有する。
Python 3 標準ライブラリのみを使用する(追加インストール不要の担保)。
"""

from __future__ import annotations

import datetime
import hashlib
import json
import sys
from pathlib import Path

STATE_FILENAME = "state.json"


def die(msg: str) -> None:
    print(f"エラー: {msg}", file=sys.stderr)
    sys.exit(1)


def today() -> str:
    return datetime.date.today().isoformat()


def load_json(path: Path) -> dict:
    if not path.is_file():
        die(f"ファイルが存在しません: {path}")
    try:
        with path.open(encoding="utf-8") as f:
            data = json.load(f)
    except json.JSONDecodeError as e:
        die(f"JSON の構文エラー: {path}: {e}")
        raise AssertionError  # die は返らない
    if not isinstance(data, dict):
        die(f"JSON のルートがオブジェクト(辞書)でない: {path}")
    return data


def save_json(path: Path, data: dict) -> None:
    with path.open("w", encoding="utf-8") as f:
        json.dump(data, f, ensure_ascii=False, indent=2)
        f.write("\n")


def sha256_of(path: Path) -> str:
    h = hashlib.sha256()
    with path.open("rb") as f:
        for chunk in iter(lambda: f.read(65536), b""):
            h.update(chunk)
    return h.hexdigest()


# ---------------------------------------------------------------------------
# ワークフロー定義データ


def validate_def(defn: dict) -> list[str]:
    """定義データの構造を検査し、問題の一覧を返す(空なら妥当)。"""
    errors: list[str] = []
    if not isinstance(defn.get("name"), str) or not defn.get("name"):
        errors.append("name が未定義または文字列でない")
    states = defn.get("states")
    if (
        not isinstance(states, list)
        or not states
        or not all(isinstance(s, str) for s in states)
    ):
        errors.append("states が未定義または文字列配列でない")
        return errors  # 以降の検査は states 前提のため打ち切り
    if len(set(states)) != len(states):
        errors.append("states に重複がある")
    initial = defn.get("initial")
    if initial not in states:
        errors.append(f"initial ({initial!r}) が states に含まれない")
    for s in defn.get("final", []):
        if s not in states:
            errors.append(f"final の {s!r} が states に含まれない")
    transitions = defn.get("transitions")
    if not isinstance(transitions, list):
        errors.append("transitions が未定義または配列でない")
        return errors
    seen: set[tuple] = set()
    gate_seen: set[tuple] = set()
    for i, t in enumerate(transitions):
        if not isinstance(t, dict) or "from" not in t or "to" not in t:
            errors.append(f"transitions[{i}] に from/to がない")
            continue
        for key in ("from", "to"):
            if t[key] not in states:
                errors.append(
                    f"transitions[{i}].{key} ({t[key]!r}) が states に含まれない"
                )
        if "gate" in t and (not isinstance(t["gate"], str) or not t["gate"]):
            errors.append(f"transitions[{i}].gate が空または文字列でない")
        pair = (t.get("from"), t.get("to"))
        if pair in seen:
            errors.append(f"transitions に重複がある: {pair[0]} -> {pair[1]}")
        seen.add(pair)
        gate = t.get("gate")
        if isinstance(gate, str) and gate:
            gp = (t.get("from"), gate)
            if gp in gate_seen:
                errors.append(
                    f"同一 from・同一 gate の遷移が複数ある: from={gp[0]!r}, gate={gate!r}"
                    "(approve の行き先が一意に決まらない)"
                )
            gate_seen.add(gp)
    artifacts = defn.get("artifacts", {})
    if not isinstance(artifacts, dict):
        errors.append("artifacts が辞書でない")
    else:
        for s in artifacts:
            if s not in states:
                errors.append(f"artifacts のキー {s!r} が states に含まれない")
    return errors


def load_def(path: Path) -> dict:
    defn = load_json(path)
    problems = validate_def(defn)
    if problems:
        for p in problems:
            print(f"定義エラー: {p}", file=sys.stderr)
        sys.exit(1)
    return defn


def gates_of(defn: dict) -> list[str]:
    """定義データに現れる承認ゲート名の一覧(出現順・重複なし)。"""
    gates: list[str] = []
    for t in defn.get("transitions", []):
        g = t.get("gate")
        if g and g not in gates:
            gates.append(g)
    return gates


def transitions_from(defn: dict, state: str) -> list[dict]:
    return [t for t in defn.get("transitions", []) if t.get("from") == state]


def is_final(defn: dict, state: str) -> bool:
    return state in defn.get("final", [])


def artifacts_for(defn: dict, state: str) -> list[str]:
    return list(defn.get("artifacts", {}).get(state, []))


def all_artifacts(defn: dict) -> list[str]:
    """全状態の artifacts の和集合(宣言順・重複なし)。凍結対象の決定に使う。"""
    names: list[str] = []
    for files in defn.get("artifacts", {}).values():
        for f in files:
            if f not in names:
                names.append(f)
    return names


# ---------------------------------------------------------------------------
# インスタンス状態ファイル

STATE_REQUIRED_FIELDS = ("workflow", "unit", "state", "approvals", "created", "updated")


def validate_state_types(state: dict) -> list[str]:
    """state.json のフィールド型を検査し、問題の一覧を返す(存在検査は呼び出し側が行う)。"""
    errors: list[str] = []
    for field in ("workflow", "unit", "state", "created", "updated"):
        if field in state and not isinstance(state[field], str):
            errors.append(f"state.json の {field} が文字列でない")
    approvals = state.get("approvals")
    if approvals is not None and (
        not isinstance(approvals, dict)
        or not all(isinstance(v, bool) for v in approvals.values())
    ):
        errors.append("state.json の approvals が {ゲート名: 真偽値} の辞書でない")
    frozen = state.get("frozen")
    if frozen is not None and (
        not isinstance(frozen, dict)
        or not all(isinstance(v, str) for v in frozen.values())
    ):
        errors.append("state.json の frozen が {ファイル名: ハッシュ} の辞書でない")
    return errors


def state_path(workdir: Path) -> Path:
    return workdir / STATE_FILENAME


def load_state(workdir: Path) -> dict:
    return load_json(state_path(workdir))


def save_state(workdir: Path, state: dict) -> None:
    state["updated"] = today()
    save_json(state_path(workdir), state)


def freeze(workdir: Path, defn: dict, state: dict) -> list[str]:
    """完了状態への到達時に、存在する成果物の sha256 を記録する。

    記録対象は定義データの artifacts に宣言された全ファイル(存在するもののみ)。
    戻り値は記録したファイル名の一覧。
    """
    frozen: dict[str, str] = {}
    for name in all_artifacts(defn):
        p = workdir / name
        if p.is_file():
            frozen[name] = sha256_of(p)
    state["frozen"] = frozen
    return list(frozen)


# ---------------------------------------------------------------------------
# 中間生成物 Markdown の決定論的パース補助(check.py が使う。正規表現ベースの
# ヒューリスティックのため、結果の最終判断は AI/人間が行う)

import re

REQ_HEADING_RE = re.compile(r"^###\s+Requirement\s+(\d+)\s*:")
CRITERIA_RE = re.compile(r"^(\d+)\.(\d+)\.\s")
TASK_RE = re.compile(r"^\s*-\s\[(?: |x)\]\*?\s+(\d+(?:\.\d+)?)[\s.]")
ANNOTATION_RE = re.compile(
    r"_(Requirements|Boundary|Depends|Knowledge|Blocked):\s*([^_]*)_"
)
MARKERS = ("[要確認:", "UNVERIFIED")
AMBIGUOUS_WORDS = ("適切に", "高速に", "柔軟に", "十分な", "ユーザーフレンドリー")


def read_lines(path: Path) -> list[str]:
    return path.read_text(encoding="utf-8").splitlines()


def parse_requirements(path: Path) -> dict:
    """仕様文書(spec.md)から要件番号と受け入れ基準 ID を抽出する。

    返り値: {"requirements": [int], "criteria": {"1.1", ...}, "duplicates": ["1.1", ...]}
    """
    reqs: list[int] = []
    criteria_list: list[str] = []
    for line in read_lines(path):
        m = REQ_HEADING_RE.match(line)
        if m:
            reqs.append(int(m.group(1)))
            continue
        m = CRITERIA_RE.match(line.strip())
        if m:
            criteria_list.append(f"{m.group(1)}.{m.group(2)}")
    counts: dict[str, int] = {}
    for c in criteria_list:
        counts[c] = counts.get(c, 0) + 1
    return {
        "requirements": reqs,
        "criteria": set(criteria_list),
        "duplicates": sorted(c for c, n in counts.items() if n > 1),
    }


def parse_tasks(path: Path) -> list[dict]:
    """tasks.md からタスク一覧を抽出する。

    各タスク: {"number", "line", "annotations": {種別: [値]}, "body": [行]}
    annotations の値はカンマ区切りを分解済み。body はタスク行から次のタスク行まで。
    """
    tasks: list[dict] = []
    current: dict | None = None
    for i, line in enumerate(read_lines(path), start=1):
        m = TASK_RE.match(line)
        if m:
            current = {"number": m.group(1), "line": i, "annotations": {}, "body": []}
            tasks.append(current)
            continue
        if current is None:
            continue
        current["body"].append(line)
        for kind, value in ANNOTATION_RE.findall(line):
            values = [v.strip() for v in value.split(",") if v.strip()]
            current["annotations"].setdefault(kind, []).extend(values)
    return tasks


def find_markers(path: Path) -> list[tuple[int, str]]:
    """残存マーカー([要確認:]・UNVERIFIED)の (行番号, マーカー) を返す。"""
    found: list[tuple[int, str]] = []
    for i, line in enumerate(read_lines(path), start=1):
        for marker in MARKERS:
            if marker in line:
                found.append((i, marker))
    return found


def find_ambiguous(path: Path) -> list[tuple[int, str]]:
    """曖昧語の (行番号, 語) を返す(参考情報。規約の引用行も拾う点に注意)。"""
    found: list[tuple[int, str]] = []
    for i, line in enumerate(read_lines(path), start=1):
        for word in AMBIGUOUS_WORDS:
            if word in line:
                found.append((i, word))
    return found


def detect_depends_cycle(tasks: list[dict]) -> list[str] | None:
    """_Depends: の循環を検出し、循環経路(タスク番号列)を返す。無ければ None。"""
    graph = {t["number"]: t["annotations"].get("Depends", []) for t in tasks}
    WHITE, GRAY, BLACK = 0, 1, 2
    color = {n: WHITE for n in graph}
    path: list[str] = []

    def visit(node: str) -> list[str] | None:
        color[node] = GRAY
        path.append(node)
        for dep in graph.get(node, []):
            if dep not in graph:
                continue  # 実在しない依存は別途 warning にする
            if color[dep] == GRAY:
                return path[path.index(dep) :] + [dep]
            if color[dep] == WHITE:
                cycle = visit(dep)
                if cycle:
                    return cycle
        color[node] = BLACK
        path.pop()
        return None

    for node in graph:
        if color[node] == WHITE:
            cycle = visit(node)
            if cycle:
                return cycle
    return None
