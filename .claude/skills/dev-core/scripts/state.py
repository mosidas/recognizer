#!/usr/bin/env python3
"""汎用状態機械エンジン(書き込み)。

composition が与えるワークフロー定義データ(JSON)に従い、workdir 内の
state.json を生成・更新する。定義にない状態・遷移・ゲートは拒否する。
state.json の手書き編集はせず、常に本スクリプトを使う。

使い方:
  state.py init      --def <workflow.json> --workdir <dir> [--unit <name>]
  state.py set-state --def <workflow.json> --workdir <dir> <state>
  state.py approve   --def <workflow.json> --workdir <dir> <gate>
  state.py show      --workdir <dir>
  state.py status    --def <workflow.json> --workdir <dir> [--json]
  state.py scan      --def <workflow.json> --root <dir> [--json]
"""

from __future__ import annotations

import argparse
import json
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).parent))
import lib  # noqa: E402


def cmd_init(args: argparse.Namespace) -> None:
    defn = lib.load_def(Path(args.def_path))
    workdir = Path(args.workdir)
    sp = lib.state_path(workdir)
    if sp.exists():
        lib.die(f"state.json は既に存在します: {sp}(再初期化はできません)")
    workdir.mkdir(parents=True, exist_ok=True)
    state = {
        "workflow": defn["name"],
        "unit": args.unit or workdir.name,
        "state": defn["initial"],
        "approvals": {g: False for g in lib.gates_of(defn)},
        "created": lib.today(),
        "updated": lib.today(),
    }
    lib.save_json(sp, state)
    print(
        f"初期化しました: {sp}(workflow={state['workflow']}, unit={state['unit']}, state={state['state']})"
    )


def _load_pair(args: argparse.Namespace) -> tuple[dict, Path, dict]:
    defn = lib.load_def(Path(args.def_path))
    workdir = Path(args.workdir)
    state = lib.load_state(workdir)
    missing = [f for f in lib.STATE_REQUIRED_FIELDS if f not in state]
    if missing:
        lib.die(f"state.json に必須フィールドがない: {', '.join(missing)}")
    problems = lib.validate_state_types(state)
    if problems:
        lib.die("; ".join(problems))
    if state.get("workflow") != defn["name"]:
        lib.die(
            f"ワークフロー不一致: state.json は {state.get('workflow')!r}、定義は {defn['name']!r}"
        )
    if state.get("state") not in defn["states"]:
        lib.die(
            f"state.json の状態 {state.get('state')!r} が定義の states に含まれません"
        )
    return defn, workdir, state


def _enter(defn: dict, workdir: Path, state: dict, target: str) -> None:
    """状態を target へ進め、完了状態なら凍結する。"""
    state["state"] = target
    if lib.is_final(defn, target):
        recorded = lib.freeze(workdir, defn, state)
        print(
            f"完了状態 {target!r} に到達。成果物を凍結しました: {', '.join(recorded) or '(なし)'}"
        )
    lib.save_state(workdir, state)


def cmd_set_state(args: argparse.Namespace) -> None:
    defn, workdir, state = _load_pair(args)
    current, target = state["state"], args.state
    if target not in defn["states"]:
        lib.die(
            f"状態 {target!r} は定義にありません(states: {', '.join(defn['states'])})"
        )
    match = [t for t in lib.transitions_from(defn, current) if t["to"] == target]
    if not match:
        allowed = (
            ", ".join(t["to"] for t in lib.transitions_from(defn, current)) or "(なし)"
        )
        lib.die(f"遷移 {current} -> {target} は定義にありません(遷移可能: {allowed})")
    if "gate" in match[0]:
        lib.die(
            f"遷移 {current} -> {target} は承認ゲート {match[0]['gate']!r} 付きです。"
            f"approve {match[0]['gate']} を使ってください"
        )
    _enter(defn, workdir, state, target)
    print(f"状態を更新しました: {current} -> {target}")


def cmd_approve(args: argparse.Namespace) -> None:
    defn, workdir, state = _load_pair(args)
    current, gate = state["state"], args.gate
    if gate not in lib.gates_of(defn):
        lib.die(
            f"ゲート {gate!r} は定義にありません(gates: {', '.join(lib.gates_of(defn))})"
        )
    match = [t for t in lib.transitions_from(defn, current) if t.get("gate") == gate]
    if not match:
        lib.die(f"現在の状態 {current!r} からゲート {gate!r} 付きの遷移はありません")
    state["approvals"][gate] = True
    _enter(defn, workdir, state, match[0]["to"])
    print(
        f"ゲート {gate!r} を承認し、状態を更新しました: {current} -> {match[0]['to']}"
    )


def cmd_show(args: argparse.Namespace) -> None:
    state = lib.load_state(Path(args.workdir))
    print(json.dumps(state, ensure_ascii=False, indent=2))


def cmd_scan(args: argparse.Namespace) -> None:
    """root 以下の全 workdir(state.json を含むディレクトリ)の状態を横断集約する。

    再開時の現在地復元に使う。定義と workflow 名が一致しない state.json は
    対象外として別掲する(他ワークフローの workdir が混在してよい)。
    """
    defn = lib.load_def(Path(args.def_path))
    root = Path(args.root)
    if not root.is_dir():
        lib.die(f"root が存在しません: {root}")
    rows, others = [], []
    for sp in sorted(root.rglob(lib.STATE_FILENAME)):
        workdir = sp.parent
        try:
            with sp.open(encoding="utf-8") as f:
                state = json.load(f)
        except json.JSONDecodeError:
            others.append({"workdir": str(workdir), "note": "state.json が不正な JSON"})
            continue
        if state.get("workflow") != defn["name"]:
            others.append(
                {
                    "workdir": str(workdir),
                    "note": f"別ワークフロー: {state.get('workflow')}",
                }
            )
            continue
        current = state.get("state", "")
        nexts = [
            {"to": t["to"], "gate": t.get("gate")}
            for t in lib.transitions_from(defn, current)
        ]
        rows.append(
            {
                "workdir": str(workdir),
                "unit": state.get("unit", ""),
                "state": current,
                "final": lib.is_final(defn, current),
                "next_transitions": nexts,
                "updated": state.get("updated", ""),
            }
        )
    done = sum(1 for r in rows if r["final"])
    result = {
        "workflow": defn["name"],
        "total": len(rows),
        "completed": done,
        "units": rows,
        "others": others,
    }
    if args.json:
        print(json.dumps(result, ensure_ascii=False, indent=2))
        return
    print(f"workflow: {defn['name']}  対象: {len(rows)} 件(完了 {done} 件)")
    for r in rows:
        mark = "✅" if r["final"] else "▶"
        nxt = (
            ", ".join(
                f"{t['to']}" + (f"(要承認: {t['gate']})" if t["gate"] else "")
                for t in r["next_transitions"]
            )
            or "(なし)"
        )
        print(f"{mark} {r['unit']}\t{r['state']}\t次: {nxt}\t({r['workdir']})")
    for o in others:
        print(f"— 対象外: {o['workdir']}({o['note']})")
    if not rows:
        print("対象の workdir が見つからない")


def cmd_status(args: argparse.Namespace) -> None:
    defn, workdir, state = _load_pair(args)
    current = state["state"]
    artifacts = {
        name: (workdir / name).is_file() for name in lib.artifacts_for(defn, current)
    }
    nexts = [
        {"to": t["to"], "gate": t.get("gate")}
        for t in lib.transitions_from(defn, current)
    ]
    result = {
        "workflow": state["workflow"],
        "unit": state["unit"],
        "state": current,
        "final": lib.is_final(defn, current),
        "approvals": state["approvals"],
        "artifacts": artifacts,
        "next_transitions": nexts,
    }
    if args.json:
        print(json.dumps(result, ensure_ascii=False, indent=2))
        return
    print(f"workflow: {result['workflow']}  unit: {result['unit']}")
    print(f"状態: {current}" + ("(完了・凍結済み)" if result["final"] else ""))
    print(
        "承認: "
        + (
            ", ".join(
                f"{g}={'済' if v else '未'}" for g, v in state["approvals"].items()
            )
            or "(ゲートなし)"
        )
    )
    for name, exists in artifacts.items():
        print(f"成果物: {name} {'あり' if exists else 'なし'}")
    for t in nexts:
        gate = f"(要承認: {t['gate']})" if t["gate"] else ""
        print(f"次の遷移: -> {t['to']} {gate}")
    if not nexts:
        print("次の遷移: (なし)")


def main() -> None:
    parser = argparse.ArgumentParser(description="汎用状態機械エンジン")
    sub = parser.add_subparsers(dest="command", required=True)

    def add_common(p: argparse.ArgumentParser, need_def: bool = True) -> None:
        if need_def:
            p.add_argument(
                "--def", dest="def_path", required=True, help="ワークフロー定義 JSON"
            )
        p.add_argument("--workdir", required=True, help="成果物ディレクトリ")

    p = sub.add_parser("init", help="state.json を初期化する")
    add_common(p)
    p.add_argument("--unit", help="作業単位名(省略時は workdir 名)")
    p.set_defaults(func=cmd_init)

    p = sub.add_parser("set-state", help="ゲートなし遷移で状態を進める")
    add_common(p)
    p.add_argument("state", help="遷移先の状態")
    p.set_defaults(func=cmd_set_state)

    p = sub.add_parser("approve", help="承認ゲートを通過して状態を進める")
    add_common(p)
    p.add_argument("gate", help="承認するゲート名")
    p.set_defaults(func=cmd_approve)

    p = sub.add_parser("show", help="state.json を表示する(read-only)")
    add_common(p, need_def=False)
    p.set_defaults(func=cmd_show)

    p = sub.add_parser("status", help="進捗サマリを表示する(read-only)")
    add_common(p)
    p.add_argument("--json", action="store_true", help="JSON で出力する")
    p.set_defaults(func=cmd_status)

    p = sub.add_parser(
        "scan", help="root 以下の全 workdir の状態を横断集約する(read-only)"
    )
    p.add_argument(
        "--def", dest="def_path", required=True, help="ワークフロー定義 JSON"
    )
    p.add_argument("--root", required=True, help="走査ルート(例: docs/specs)")
    p.add_argument("--json", action="store_true", help="JSON で出力する")
    p.set_defaults(func=cmd_scan)

    args = parser.parse_args()
    args.func(args)


if __name__ == "__main__":
    main()
