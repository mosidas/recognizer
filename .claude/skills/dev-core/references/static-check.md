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
state.py init      --def <workflow.json> --root <dir> --unit <name> [--unique-root <dir>]
state.py init      --def <workflow.json> --workdir <dir> [--unit <name>]
state.py set-state --def <workflow.json> --workdir <dir> <state>
state.py approve   --def <workflow.json> --workdir <dir> <gate>
state.py show      --workdir <dir>
state.py status    --def <workflow.json> --workdir <dir> [--json]
state.py scan      --def <workflow.json> [--def <other.json> ...] --root <dir> [--json]
check.py           --workdir <dir> [--def <workflow.json>] [--ports-root docs/dev/ports]
                   [--repo-root .] [--max-file-lines 600] [--json]
```

### 3.1. workdir の採番(state.py init)

`init` は workdir の決め方を 2 通り持つ。どちらか一方を指定する(同時指定は拒否する)。

| 指定                            | 動作                                                                    |
| ------------------------------- | ------------------------------------------------------------------------- |
| `--root <ルート> --unit <名前>` | ルート直下に `NNN-<unit>` のディレクトリを採番して作る                  |
| `--workdir <パス>`              | 指定のパスをそのまま使う(採番しない)                                  |

中間生成物は作業単位ごとに増え、完了状態への到達で凍結されて以後は参照専用になる。この累積の順序を名前から読み取れるよう、ルート直下に連番を付ける。採番の規則は次のとおりとする。

- 番号はルート直下で `NNN-` から始まるディレクトリの最大番号 + 1 とし、3 桁の 0 埋めで表す(最初は `001`)。
- 連番を持たないディレクトリは数に入れない。採番の導入前に作った workdir をルート直下にそのまま置ける。
- 欠番が生じても番号を振り直さない。桁は 3 桁以上を受け付けるため、999 を超えても採番が続く。
- 同じ unit の workdir が検査範囲に既にあるとき(連番の有無を問わない)、`init` は拒否する。番号違いの重複を作らず、既存の workdir で再開させるためである。検査範囲は既定で `--root` の直下とし、`--unique-root <ルート>` を与えるとその配下の全階層になる。
- `state.json` の `unit` には連番を含まない unit 名を記録する。作成した workdir のパスは `init` が `workdir:` 行に出力する。既存 workdir のパスは `scan` の一覧で特定する(unit 名からパスを組み立てない)。

採番はルート直下で閉じるため、ルートを入れ子にすると採番の範囲も入れ子になる。flow-sdd はこの性質を使い、`docs/specs/NNN-<roadmap 名>/NNN-<unit>/` の 2 階層で unit の連番を roadmap ごとに閉じる(`flow-sdd/SKILL.md` 1.)。このとき `--root` は roadmap のディレクトリを指し、`--unique-root docs/specs` で unit 名の一意性だけを specs 全体に広げる。

### 3.1.1. 階層を分けた複数ワークフローの併用

1 つのルートの下で階層ごとに別のワークフローを動かせる。エンジンは `state.json` の位置と `workflow` の値だけで対象を判定し、階層の深さを前提にしない。

- `init` の `--def` を階層ごとに変える。上位の階層と下位の階層はそれぞれ独立した状態機械として進む。
- `scan` の `--def` は複数指定でき、指定した全定義の workdir を 1 回の走査で集約する。出力の各行は `workflow` を持ち、どの定義に属する workdir かが分かる。どの定義とも `workflow` 名が一致しない `state.json` は `others` に別掲する。
- `check.py` は `--workdir` を直接受け取り、凍結の照合は `state.json` と同じディレクトリで閉じるため、階層を増やしても変更が要らない。

### 3.2. check.py の検査対象

check.py の `--def` は任意で、与えたときのみ状態検査を行う。Markdown 検査は常に実行され、workdir に存在するファイルだけを対象にする(単独利用の部品でも使える)。

- spec.md: 要件番号・受け入れ基準 ID の連番/欠番/重複(warning)
- tasks.md: `_Requirements:` の前方/後方照合(warning)・`_Depends:` の循環(error)と実在(warning)・タスク固有情報(対象ファイル・検証コマンド)の欠落(warning)・`_Knowledge:` の port 実在(warning)
- 対象ファイルの行数: tasks.md の「対象ファイル」に挙がった**既存ファイル**が閾値(`--max-file-lines`。既定 600 行)を超える(warning)。パスは `--repo-root` を基点に解決し、未存在のファイル(新規作成予定)は対象外。基点が解決できない場合とパスが基点の外を指す場合は、検査できなかったことを warning にする(「超過なし」と区別する)
- 共通: 残存マーカー `[要確認:]`・`UNVERIFIED`(warning)、曖昧語(info。spec.md のみ)、ツールのマークアップ混入 = 行全体が閉じタグ 1 個で構成される行(error。コードフェンス内は除外。インラインコードでの引用は行全体がタグにならないため対象外)

行数の閾値は言語・ファイル種別によって妥当な値が異なるため、`--max-file-lines` で調整する。この指摘は「行数そのものが違反」を意味せず、責務の分割を検討する起点である(分割の要否は structure 観点の意味判断が決める。[review-perspectives.md](./review-perspectives.md) §2.2)。

Markdown のパースは正規表現ベースのヒューリスティックのため、warning/info は機械が確信できない指摘であり最終判断は AI/人間が行う(error のみが確実な違反)。

## 4. check.py の重大度

| 重大度     | 意味                                                     | 例                                                                              |
| ---------- | -------------------------------------------------------- | ------------------------------------------------------------------------------- |
| 🔴 error   | 機械的に確実な規約違反。解消するまで先へ進んではならない | state.json のスキーマ違反・定義にない状態・成果物の欠落・凍結違反(凍結後の変更)・ツールのマークアップ混入 |
| 🟡 warning | ヒューリスティックな指摘。最終判断は人間/AI が行う       | approvals のキー欠落・要件 ID の欠番・トレーサビリティの未カバー・残存マーカー・対象ファイルの行数超過 |
| 🔵 info    | 参考情報                                                 | 凍結照合の件数                                                                  |

- exit code は error があれば 1、なければ 0。自動化(composition のゲート)では exit code で分岐する。

## 5. 凍結

- 完了状態への到達時、エンジンが中間生成物のハッシュを記録する(凍結)。以後の変更は `check.py` が「凍結違反」として error 検出する。
- 凍結後に判明した差異は中間生成物へ書き戻さず、コードと恒久情報([durable-info.md](./durable-info.md))へ反映する。
