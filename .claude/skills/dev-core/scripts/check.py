#!/usr/bin/env python3
"""静的チェッカ(read-only)。

workdir の state.json・成果物・中間生成物 Markdown を規約に照らして検査する。
ファイルを一切書き換えない。決定論的に判定できる違反のみを扱い、
意味的な判断(内容の妥当性・網羅性の解釈)は AI のレビューに委ねる。

検査の 2 系統:
  状態検査     --def でワークフロー定義を与えたときのみ(state.json・成果物存在・凍結)
  Markdown 検査 常に実行(workdir 内に存在するファイルだけ検査する):
    spec.md          要件番号・受け入れ基準 ID の連番/欠番/重複
    tasks.md         _Requirements: の前方/後方照合・_Depends: 循環・タスク固有情報(対象ファイル・検証コマンド)・_Knowledge: の実在
    共通             残存マーカー([要確認:]・UNVERIFIED)・曖昧語(spec.md のみ)

重大度:
  error   機械的に確実な規約違反(exit code 1)
  warning ヒューリスティックな指摘。最終判断は人間/AI が行う
  info    参考情報

使い方:
  check.py --workdir <dir> [--def <workflow.json>] [--ports-root docs/dev/ports] [--json]
"""

from __future__ import annotations

import argparse
import json
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).parent))
import lib  # noqa: E402
import ports as ports_mod  # noqa: E402


class Report:
    def __init__(self) -> None:
        self.findings: list[dict] = []

    def add(self, severity: str, message: str) -> None:
        self.findings.append({"severity": severity, "message": message})

    def error(self, message: str) -> None:
        self.add("error", message)

    def warning(self, message: str) -> None:
        self.add("warning", message)

    def info(self, message: str) -> None:
        self.add("info", message)

    def count(self, severity: str) -> int:
        return sum(1 for f in self.findings if f["severity"] == severity)


# ---------------------------------------------------------------------------
# 状態検査(--def があるときのみ)


def check_state(defn: dict, workdir: Path, report: Report) -> None:
    sp = lib.state_path(workdir)
    if not sp.is_file():
        report.error(f"state.json が存在しない: {sp}")
        return
    try:
        with sp.open(encoding="utf-8") as f:
            state = json.load(f)
    except json.JSONDecodeError as e:
        report.error(f"state.json の JSON 構文エラー: {e}")
        return

    for field in lib.STATE_REQUIRED_FIELDS:
        if field not in state:
            report.error(f"state.json に必須フィールド {field!r} がない")
    if any(f not in state for f in lib.STATE_REQUIRED_FIELDS):
        return

    type_errors = lib.validate_state_types(state)
    for msg in type_errors:
        report.error(msg)
    if type_errors:
        return

    if state["workflow"] != defn["name"]:
        report.error(
            f"ワークフロー不一致: state.json は {state['workflow']!r}、定義は {defn['name']!r}"
        )
    current = state["state"]
    if current not in defn["states"]:
        report.error(f"状態 {current!r} が定義の states に含まれない")
        return

    gates = lib.gates_of(defn)
    for g in gates:
        if g not in state["approvals"]:
            report.warning(f"approvals にゲート {g!r} のキーがない")
    for g in state["approvals"]:
        if g not in gates:
            report.warning(f"approvals の {g!r} は定義にないゲート")

    for name in lib.artifacts_for(defn, current):
        if not (workdir / name).is_file():
            report.error(f"状態 {current!r} で存在すべき成果物がない: {name}")

    if lib.is_final(defn, current):
        frozen = state.get("frozen")
        if frozen is None:
            report.error("完了状態だが frozen(凍結ハッシュ)が記録されていない")
            return
        for name, recorded in frozen.items():
            p = workdir / name
            if not p.is_file():
                report.error(f"凍結された成果物が削除されている: {name}")
            elif lib.sha256_of(p) != recorded:
                report.error(
                    f"凍結違反: {name} が凍結後に変更されている。"
                    "中間生成物は変更せず、差異はコードと恒久情報へ反映する"
                )
        report.info(f"凍結済み成果物: {len(frozen)} 件を照合した")
    elif "frozen" in state:
        report.warning("完了状態でないのに frozen が記録されている")


# ---------------------------------------------------------------------------
# Markdown 検査(常に実行。存在するファイルのみ)


def check_spec_md(path: Path, report: Report) -> dict | None:
    parsed = lib.parse_requirements(path)
    reqs, criteria = parsed["requirements"], parsed["criteria"]
    if not reqs:
        report.warning(f"{path.name} に要件見出し(### Requirement N:)が見つからない")
        return parsed
    expected = list(range(1, len(reqs) + 1))
    if sorted(reqs) != expected:
        report.warning(
            f"要件番号に欠番または重複がある: {reqs}(期待: 1..{len(reqs)} の連番)"
        )
    for n in reqs:
        ms = sorted(int(c.split(".")[1]) for c in criteria if c.split(".")[0] == str(n))
        if not ms:
            report.warning(f"Requirement {n} に受け入れ基準(N.M 形式)が無い")
        elif ms != list(range(1, len(ms) + 1)):
            report.warning(f"Requirement {n} の受け入れ基準に欠番がある: {ms}")
    if parsed["duplicates"]:
        report.warning(
            f"受け入れ基準 ID に重複がある: {', '.join(parsed['duplicates'])}"
        )
    return parsed


def check_tasks_md(
    path: Path,
    criteria: set[str] | None,
    ports_root: Path,
    report: Report,
) -> None:
    tasks = lib.parse_tasks(path)
    if not tasks:
        report.warning("tasks.md にタスク(チェックボックス行)が見つからない")
        return
    numbers = {t["number"] for t in tasks}
    subtasks = [t for t in tasks if "." in t["number"]] or tasks

    # トレーサビリティ(仕様文書がある場合のみ)
    if criteria is not None:
        covered: set[str] = set()
        for t in tasks:
            for rid in t["annotations"].get("Requirements", []):
                covered.add(rid)
                if rid not in criteria:
                    report.warning(
                        f"タスク {t['number']} の _Requirements: {rid} が spec.md に無い"
                    )
        uncovered = sorted(criteria - covered)
        if uncovered:
            report.warning(
                f"どのタスクにもカバーされていない要件 ID: {', '.join(uncovered)}"
            )

    # 依存
    for t in tasks:
        for dep in t["annotations"].get("Depends", []):
            if dep not in numbers:
                report.warning(f"タスク {t['number']} の _Depends: {dep} が実在しない")
    cycle = lib.detect_depends_cycle(tasks)
    if cycle:
        report.error(f"_Depends: に循環がある: {' -> '.join(cycle)}")

    # タスク固有情報
    for t in subtasks:
        body = "\n".join(t["body"])
        for field in ("対象ファイル", "検証コマンド"):
            if field not in body:
                report.warning(f"タスク {t['number']} に「{field}」が無い")

    # _Knowledge: の実在
    knowledge_tasks = [t for t in tasks if t["annotations"].get("Knowledge")]
    if ports_root.is_dir():
        port_names = {p["name"] for p in ports_mod.scan(ports_root)[0]}
        for t in knowledge_tasks:
            for name in t["annotations"]["Knowledge"]:
                if name not in port_names:
                    report.warning(
                        f"タスク {t['number']} の _Knowledge: {name} が port 走査結果に無い(改名・削除の疑い)"
                    )
    elif knowledge_tasks:
        report.warning(
            f"_Knowledge: 注記があるが port ルートが存在しない: {ports_root}(name を照合できない)"
        )


def check_markdown(workdir: Path, ports_root: Path, report: Report) -> None:
    # 仕様文書: spec.md(契約 + 受け入れ基準の 1 文書)
    spec_path = workdir / "spec.md"
    parsed = check_spec_md(spec_path, report) if spec_path.is_file() else None
    criteria = parsed["criteria"] if parsed else None

    tasks_path = workdir / "tasks.md"
    if tasks_path.is_file():
        check_tasks_md(tasks_path, criteria, ports_root, report)

    for name in ("spec.md", "tasks.md"):
        p = workdir / name
        if not p.is_file():
            continue
        for line_no, marker in lib.find_markers(p):
            report.warning(f"{name}:{line_no} に残存マーカー {marker}")
    if spec_path.is_file():
        for line_no, word in lib.find_ambiguous(spec_path):
            report.info(f"{spec_path.name}:{line_no} に曖昧語「{word}」(定量化を検討)")


def main() -> None:
    parser = argparse.ArgumentParser(description="静的チェッカ(read-only)")
    parser.add_argument(
        "--def", dest="def_path", help="ワークフロー定義 JSON(状態検査を行う場合)"
    )
    parser.add_argument("--workdir", required=True, help="成果物ディレクトリ")
    parser.add_argument(
        "--ports-root", default="docs/dev/ports", help="port ルート(_Knowledge: 照合用)"
    )
    parser.add_argument("--json", action="store_true", help="JSON で出力する")
    args = parser.parse_args()

    report = Report()
    workdir = Path(args.workdir)
    if not workdir.is_dir():
        lib.die(f"workdir が存在しません: {workdir}")

    if args.def_path:
        defn_raw = lib.load_json(Path(args.def_path))
        for p in lib.validate_def(defn_raw):
            report.error(f"定義エラー: {p}")
        if report.count("error") == 0:
            check_state(defn_raw, workdir, report)

    check_markdown(workdir, Path(args.ports_root), report)

    errors, warnings = report.count("error"), report.count("warning")
    if args.json:
        print(
            json.dumps(
                {"errors": errors, "warnings": warnings, "findings": report.findings},
                ensure_ascii=False,
                indent=2,
            )
        )
    else:
        icons = {"error": "🔴", "warning": "🟡", "info": "🔵"}
        for f in report.findings:
            print(f"{icons[f['severity']]} {f['severity']}: {f['message']}")
        print(f"結果: error {errors} 件 / warning {warnings} 件")
    sys.exit(1 if errors else 0)


if __name__ == "__main__":
    main()
