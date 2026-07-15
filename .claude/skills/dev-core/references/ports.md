# port 規約(ポートマッピング)

port(利用側プロジェクトによる拡張点)の配置・宣言・読み込みの正本。全部品はこの規約に従って port を選択する。

## 1. 配置と識別

- port の共通ルートは利用側プロジェクトの `docs/dev/ports/` とする。**ルート以下のフォルダ構成は自由**(利用側が整理しやすい階層を作ってよい。例: `ports/lang/typescript.md`、`ports/domain/billing.md`、`ports/knowledge/principles.md`)。
- すべての port ファイルは frontmatter(2.)を持ち、**識別は配置パスではなく frontmatter の `name`** で行う。`name` はツリー内で一意にしなければならない(ファイル名と一致させることを推奨する)。
- port の 2 種類(差し替え port = 部品のデフォルト手順・データを置き換える / 知識 port = 作業内容に応じて注入する専門知識)は**概念上の区分**であり、仕組み(frontmatter・走査・選択)は共通。部品は差し替え port を `name` で参照する(例: dev-spec は `name: hearing-questions` の port があればヒアリングを差し替える)。

## 2. frontmatter

各 port ファイルは、先頭の frontmatter で「自分をどのスキルに、いつ注入すべきか」を宣言する。port の追加・削除・移動が 1 ファイルで完結し、スキル側の変更を要しない(開放閉鎖)。

```yaml
---
name: frontend-design
description: 視覚設計の方向づけ
inject:
  - dev-implement
  - dev-spec
condition: GUI を持つ作業のとき
---
```

| キー          | 意味                                                                                                 |
| ------------- | ---------------------------------------------------------------------------------------------------- |
| `name`        | 識別子(ツリー内で一意。`_Knowledge:` 注記・部品からの参照はこの名前で行う)                           |
| `description` | 1 行の要約                                                                                           |
| `inject`      | 注入先スキル名のリスト(`dev-*` / `flow-*` / `ext-*`)。ここに載っていないスキルはこの port を読まない |
| `condition`   | `常時` = inject 先の起動時に必ず注入 / 自然言語の条件 = スキルが作業内容と照合して該当時のみ注入     |

- `name`・`inject`・`condition` は必須とする。欠落は走査スクリプトが警告する。`condition` が無い port は注入しない(常時注入したい場合は `condition: 常時` と明示する)。
- frontmatter は YAML のサブセット(スカラーと、ブロック形式の文字列リスト)で書く。flow 形式(`inject: [a, b]`)は解釈されない(走査スクリプトが警告する)。閉じの `---` を忘れると frontmatter なしと見なされる(同じく警告する)。

## 3. 読み込み手順(スキル共通)

1. **走査スクリプトを実行する**(`../scripts/ports.py`。read-only、Python 3 標準ライブラリのみ):

```sh
python3 <skills>/dev-core/scripts/ports.py --skill <自スキル名> --root docs/dev/ports
```

自スキルが `inject` に含まれる port の一覧(name・パス・condition・description)、frontmatter なしファイルの一覧(自律選択の対象)、警告(name 重複・inject 欠落)が返る。port ルートが存在しなければ「port なしで進む」と返る。frontmatter の解析・照合という機械的処理をスクリプトに委ね、AI は条件判定(意味判断)だけを担う(決定論的補完)。

2. **選択**(優先順):
   1. `_Knowledge: <name 列>_` 注記(tasks.md 等)にある port は**最優先で注入**する(例外的な明示上書き。通常の選択は 2〜4 に任せ、注記は特定 port を固定したいときだけ書く)。注記の name が走査結果に存在しない場合は黙って無視せず、誤り(改名・削除)としてユーザーに確認する
   2. `inject` に自スキル名があり `condition: 常時` → 注入する
   3. `inject` に自スキル名があり条件付き → 作業内容(要求・設計・タスクの記載)と照合し、該当すれば注入する
   4. frontmatter が無いファイル → 作業内容と照合して自律選択する(後方互換。新規の port には frontmatter を必ず付ける)
   - `inject` に自スキル名が無いファイルは、注記で明示されない限り読まない。
3. **記録**: 選択した port(と条件判定の根拠)を、スキルが定める場所(dev-implement なら Implementation Notes)に記録する(再現性の担保)。

## 4. 規律

- port に置くのは、**利用側がプロジェクト・案件ごとに編集・差し替えする拡張点**のみ。配布物として固定の方法論・手順はスキル側(references・templates)がデフォルトとして持ち、port はその差し替え・追加に使う(スキル・拡張が単独で使う固定知識を port へ外出ししない)。
- `name` の重複を作らない(重複すると `_Knowledge:` 注記・部品からの参照が一意に解決できない)。
- port は簡潔に保つ(注入のたびにコンテキストを消費する)。inject 先は本当に使うスキルに絞る。
- `condition` は判定可能な形で書く(「GUI を持つ作業のとき」「置換・廃止・移行を含む作業のとき」)。曖昧な条件(「必要なとき」)を書かない。
- 同名の観点・規約が dev-core のデフォルト(観点カタログ等)と port の両方にある場合、port を優先する。
- port の正本は利用側プロジェクトにあり、Git 管理・PR レビューの対象にする(Docs-as-Code)。
