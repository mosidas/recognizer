# 状態管理と静的チェック

状態機械エンジン(`scripts/state.py`)と静的チェッカ(`scripts/check.py`)の利用規約を定める。いずれも Python 3 標準ライブラリのみで動作する。

## 1. 役割分担

| スクリプト | 役割                                                                                                    | 副作用                                       |
| ---------- | ------------------------------------------------------------------------------------------------------- | -------------------------------------------- |
| `state.py` | state.json の生成・状態遷移・承認・凍結(「実行」)。`scan` で複数 workdir の状態を横断集約(再開の機械化) | state.json を書き換える(`scan` は read-only) |
| `check.py` | 状態検査(state.json・成果物存在・凍結)と中間生成物 Markdown の機械検査                                  | read-only                                    |
| `ports.py` | ポートマッピングの走査(自スキル向け port の一覧を返す)                                                  | read-only                                    |

- state.json を手書きで編集してはならない。常に `state.py` を使う。
- 決定論的に判定できること(定義との整合・成果物の存在・凍結ハッシュの照合)はスクリプトが担い、意味的な判断は AI(dev-reviewer・dev-check)が担う。

## 2. ワークフロー定義データ

状態・遷移・ゲートは composition が JSON 定義データで与える。エンジンは定義にない状態・遷移・ゲートを拒否する。

```json
{
  "name": "sdd",
  "states": ["initialized", "spec-generated", "spec-approved", "completed"],
  "initial": "initialized",
  "final": ["completed"],
  "transitions": [
    { "from": "initialized", "to": "spec-generated" },
    { "from": "spec-generated", "to": "spec-approved", "gate": "spec" }
  ],
  "artifacts": { "spec-generated": ["spec.md"] }
}
```

- `final`: 完了状態の集合。完了状態への遷移時にエンジンが成果物(artifacts に宣言された全ファイルのうち存在するもの)の sha256 を state.json の `frozen` に記録する
- `transitions[].gate`: 承認ゲート名。gate 付き遷移は `approve` サブコマンドでのみ通過できる(`set-state` は拒否する)
- `artifacts`: 各状態で存在すべき成果物。`check.py` が存在検査に使う

## 3. コマンド

```sh
state.py init      --def <workflow.json> --workdir <dir> [--unit <name>]
state.py set-state --def <workflow.json> --workdir <dir> <state>
state.py approve   --def <workflow.json> --workdir <dir> <gate>
state.py show      --workdir <dir>
state.py status    --def <workflow.json> --workdir <dir> [--json]
state.py scan      --def <workflow.json> --root <dir> [--json]
check.py           --workdir <dir> [--def <workflow.json>] [--ports-root docs/dev/ports] [--json]
```

check.py の `--def` は任意で、与えたときのみ状態検査を行う。Markdown 検査は常に実行され、workdir に存在するファイルだけを対象にする(単独利用の部品でも使える)。

- spec.md: 要件番号・受け入れ基準 ID の連番/欠番/重複(warning)
- tasks.md: `_Requirements:` の前方/後方照合(warning)・`_Depends:` の循環(error)と実在(warning)・タスク固有情報(対象ファイル・検証コマンド)の欠落(warning)・`_Knowledge:` の port 実在(warning)
- 共通: 残存マーカー `[要確認:]`・`UNVERIFIED`(warning)、曖昧語(info。spec.md のみ)

Markdown のパースは正規表現ベースのヒューリスティックのため、warning/info は機械が確信できない指摘であり最終判断は AI/人間が行う(error のみが確実な違反)。

## 4. check.py の重大度

| 重大度     | 意味                                                     | 例                                                                              |
| ---------- | -------------------------------------------------------- | ------------------------------------------------------------------------------- |
| 🔴 error   | 機械的に確実な規約違反。解消するまで先へ進んではならない | state.json のスキーマ違反・定義にない状態・成果物の欠落・凍結違反(凍結後の変更) |
| 🟡 warning | ヒューリスティックな指摘。最終判断は人間/AI が行う       | approvals のキー欠落・要件 ID の欠番・トレーサビリティの未カバー・残存マーカー  |
| 🔵 info    | 参考情報                                                 | 凍結照合の件数                                                                  |

- exit code は error があれば 1、なければ 0。自動化(composition のゲート)では exit code で分岐する。

## 5. 凍結

- 完了状態への到達時、エンジンが中間生成物のハッシュを記録する(凍結)。以後の変更は `check.py` が「凍結違反」として error 検出する。
- 凍結後に判明した差異は中間生成物へ書き戻さず、コードと恒久情報([durable-info.md](./durable-info.md))へ反映する。
