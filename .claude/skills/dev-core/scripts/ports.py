#!/usr/bin/env python3
"""ポートマッピングの走査(read-only)。

docs/dev/ports/ 以下の全 Markdown の frontmatter を再帰的に読み、
指定スキルが inject に含まれる port の一覧(name・パス・condition・description)を返す。
条件(condition)の判定は行わない(意味判断は AI の責務)。ファイルを書き換えない。

使い方:
  ports.py --skill <スキル名> [--root docs/dev/ports] [--json]
  ports.py [--root docs/dev/ports] [--json]        # 全 port の一覧(監査用)
"""

from __future__ import annotations

import argparse
import json
import sys
from pathlib import Path

DEFAULT_ROOT = "docs/dev/ports"


def parse_frontmatter(path: Path) -> dict | None:
    """frontmatter を辞書で返す。無ければ None。

    YAML のサブセット(スカラーと文字列リスト)のみ対応する。
    """
    try:
        lines = path.read_text(encoding="utf-8").splitlines()
    except (OSError, UnicodeDecodeError):
        return None
    if not lines or lines[0].strip() != "---":
        return None
    data: dict = {}
    key = None
    for line in lines[1:]:
        if line.strip() == "---":
            return data
        if not line.strip() or line.lstrip().startswith("#"):
            continue
        if line.startswith((" ", "\t")) and line.lstrip().startswith("- "):
            if key is None:
                continue
            data.setdefault(key, [])
            if isinstance(data[key], list):
                data[key].append(line.lstrip()[2:].strip().strip("\"'"))
            continue
        if ":" in line:
            key, _, value = line.partition(":")
            key = key.strip()
            value = value.strip().strip("\"'")
            data[key] = value if value else []
    return None  # 閉じの --- が無い(不正な frontmatter)


def _starts_with_frontmatter_marker(path: Path) -> bool:
    try:
        with path.open(encoding="utf-8") as f:
            return f.readline().strip() == "---"
    except (OSError, UnicodeDecodeError):
        return False


def scan(root: Path) -> tuple[list[dict], list[str], list[str]]:
    """(frontmatter 付き port 一覧, frontmatter なしファイル, 警告) を返す。"""
    ports: list[dict] = []
    no_frontmatter: list[str] = []
    warnings: list[str] = []
    seen: dict[str, str] = {}
    for p in sorted(root.rglob("*.md")):
        if p.name.lower() == "readme.md":
            continue  # README は port ではなく説明文書
        fm = parse_frontmatter(p)
        rel = str(p)
        if fm is None:
            if _starts_with_frontmatter_marker(p):
                warnings.append(f"frontmatter が閉じていない(--- 欠落): {rel}")
            no_frontmatter.append(rel)
            continue
        name = fm.get("name")
        inject = fm.get("inject", [])
        if isinstance(inject, str):
            inject = [inject] if inject else []
        if not name:
            warnings.append(f"name がない: {rel}")
            continue
        if name in seen:
            warnings.append(f"name 重複: {name!r} ({seen[name]} と {rel})")
        seen[name] = rel
        if not inject:
            warnings.append(f"inject がない: {rel}")
        elif any("[" in i or "]" in i or "," in i for i in inject):
            warnings.append(
                f"inject の値が flow 形式の疑い: {rel}(ブロック形式のリストで書く)"
            )
        condition = fm.get("condition", "")
        if not isinstance(condition, str):
            condition = ""
        if not condition:
            warnings.append(
                f"condition がない: {rel}(常時か条件かを明示する。無い port は注入しない)"
            )
        ports.append(
            {
                "name": name,
                "path": rel,
                "description": fm.get("description", ""),
                "inject": inject,
                "condition": condition,
            }
        )
    return ports, no_frontmatter, warnings


def main() -> None:
    parser = argparse.ArgumentParser(description="ポートマッピングの走査(read-only)")
    parser.add_argument("--skill", help="このスキルが inject に含まれる port のみ返す")
    parser.add_argument(
        "--root", default=DEFAULT_ROOT, help=f"port ルート(既定: {DEFAULT_ROOT})"
    )
    parser.add_argument("--json", action="store_true", help="JSON で出力する")
    args = parser.parse_args()

    root = Path(args.root)
    if not root.is_dir():
        result = {
            "matched": [],
            "no_frontmatter": [],
            "warnings": [f"port ルートが存在しない: {root}(port なしで進む)"],
        }
        print(
            json.dumps(result, ensure_ascii=False, indent=2)
            if args.json
            else result["warnings"][0]
        )
        return

    ports, no_frontmatter, warnings = scan(root)
    matched = [p for p in ports if not args.skill or args.skill in p["inject"]]
    result = {
        "matched": matched,
        "no_frontmatter": no_frontmatter,
        "warnings": warnings,
    }

    if args.json:
        print(json.dumps(result, ensure_ascii=False, indent=2))
        return
    if not matched:
        print(f"該当 port なし(skill={args.skill or '全件'})")
    for p in matched:
        inj = "" if args.skill else f" inject={','.join(p['inject'])}"
        print(f"{p['name']}\t{p['condition']}\t{p['path']}{inj}")
    for f in no_frontmatter:
        print(f"frontmatter なし(自律選択の対象): {f}")
    for w in warnings:
        print(f"警告: {w}", file=sys.stderr)


if __name__ == "__main__":
    main()
