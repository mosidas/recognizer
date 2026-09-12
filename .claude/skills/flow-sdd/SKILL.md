---
name: flow-sdd
description: SDD(仕様駆動開発)ワークフロー。依頼を経路判定(ルーティング)し、unit 分解(dev-roadmap)→仕様(dev-spec: 壁打ちで契約と受け入れ基準を確定)→タスク分解(dev-decompose)→実装(dev-implement)の順に、承認ゲート付きの状態機械で駆動する composition。実装完了後は作業ブランチの PR を作成し、CI がグリーンになるまで失敗の修正を追従する(マージは人間に委ねる)。承認ゲートはすべて人間承認とする(自走・自己承認は、部品を直接束ねる拡張ワークフロー側で実現する)。「SDD で進めて」「仕様駆動で開発して」「仕様から実装まで通しで」と頼まれたとき、または中断した SDD 作業を再開するときに使う。
---

# flow-sdd — SDD ワークフロー

部品を状態機械で束ね、依頼を作業単位(unit)へ振り分けて unit 分解 → 仕様 → タスク分解 → 実装まで駆動する composition。各フェーズの生成ロジックは部品の SKILL.md をそのまま実行し、この composition は**ルーティング・状態遷移・承認ゲート・フェーズ間の接続だけ**を担う。自走する工程はサブエージェントへ委譲し、作業内容をこのセッションの文脈から外す(5.0)。

## 1. 契約

- **作業単位(unit)**: 小文字ケバブケースのスラッグ(例: `user-auth`)。ルーティング(2.)で決める。
- **roadmap**: 1 つ以上の unit をまとめる作業のかたまり。名前は小文字ケバブケースのスラッグ(例: `mvp`)とし、ルーティング(2.)で決める。**unit が 1 つでも roadmap を立てる**(構成に経路ごとの特例を作らないため)。
- **roadmap のディレクトリ**: `docs/specs/NNN-<roadmap 名>/`。`NNN` は `docs/specs/` 直下で採番する 3 桁の連番。直下に `roadmap.md` と `state.json` を置く。
- **workdir**: `docs/specs/NNN-<roadmap 名>/NNN-<unit>/`(各部品の既定 workdir をこの値で上書きする)。unit の `NNN` は roadmap のディレクトリ直下で採番するため、**unit の連番は roadmap ごとに閉じる**。どちらの連番もエンジンの `init --root` が付ける(採番規則の正本は `../dev-core/references/static-check.md` 3.1)。連番を持たない既存の workdir もそのまま扱う。unit 名だけではパスが決まらないため、既存 unit の workdir は `scan` で特定する(Step 0 の手順 2)。
- **unit 名は roadmap を跨いで一意とする**。`init` に `--unique-root docs/specs` を渡し、エンジンに重複を拒否させる。連番と違って名前の一意性を閉じない理由は、unit 名が作業ブランチ名でもあり(3.)、roadmap ごとに閉じると `git branch` の一覧と PR の題名で区別できなくなるためである。
- **roadmap.md**: `docs/specs/NNN-<roadmap 名>/roadmap.md`。unit の一覧・順序・依存に加え、未確定項目とスコープ外を記す。構成は 2.1 に定める。
- **状態機械定義**: unit は `./workflow.json`、roadmap は `./roadmap.json`。roadmap と unit は独立した状態機械として進む。状態遷移はすべて dev-core のエンジン経由で行う(`../dev-core/references/static-check.md`。階層を分けた併用は同 3.1.1)。

```
roadmap: initialized → roadmap-generated →(gate: roadmap)→ roadmap-approved
→ frozen(凍結)
差し戻し: roadmap-approved→roadmap-generated

unit: initialized → spec-generated →(gate: spec)→ spec-approved
→ tasks-generated →(gate: tasks)→ tasks-approved
→ implementing → completed(凍結)
差し戻し: tasks-generated→spec-generated / implementing→tasks-generated
```

## 2. ルーティング(入口)

依頼を分析し、次の経路に振り分ける。判断根拠を 1〜2 行で示し、経路の確定は**人間承認**とする。

| 経路 | 状況                                                   | アクション                                                                                                                |
| ---- | ------------------------------------------------------ | ------------------------------------------------------------------------------------------------------------------------- |
| A    | 既存 unit の責務範囲内で拡張できる                     | `scan` で該当 unit の workdir を特定し、`status` で状態を確認して該当フェーズから再開する。凍結済み(completed)なら新しい unit(経路 C)に切り替える |
| B    | 作業単位化が不要(バグ修正・設定変更・軽微で境界が明確) | dev-implement を直接使う(軽量タスク定義)。状態機械は使わない                                                              |
| C    | 新規の作業 1 単位が妥当                                | roadmap 名と unit 名を決め、Step R で roadmap を立ててから Step 0 へ進む                                                   |
| D    | 大きく、複数 unit へ分解すべき                         | roadmap 名を決め、Step R で unit 一覧・順序・依存を生成し、ユーザー承認後に各 unit を順に Step 0 から駆動する              |
| E    | 混合(既存更新 + 新規 + 軽微が混在)                     | roadmap で全体を整理し、各項目を A〜C に割り当てる                                                                        |

- 経路判定に既存コードベースの把握が要る場合は dev-explorer に隔離し、ダイジェストのみ受け取る。
- **経路の判定はこの composition が行い、unit への分解は `../dev-roadmap/SKILL.md` が行う**。経路判定はワークフローの駆動の仕方を決める判断(composition の責務)、unit 分解は成果物 `roadmap.md` の生成(部品の責務)である。規模の目安・切り方・未確定の判定基準は dev-roadmap にあり、ここで再定義しない。
- 経路 C も roadmap を立てる。unit が 1 つでも `roadmap.md` を生成し、unit 一覧に 1 行だけを書く。roadmap のディレクトリだけを作って `roadmap.md` を置かない形にすると、ディレクトリ名の出所が文書に無くなり、凍結の機構が `roadmap.md` の有無で分岐する。経路 C の承認は経路判定のゲートに含め、roadmap 単独の承認ゲートは置かない(2.1)。

### 2.1. roadmap.md の構成と凍結

`docs/specs/NNN-<roadmap 名>/roadmap.md` は次の 3 節を持つ。

| 節         | 内容                                                                     |
| ---------- | ------------------------------------------------------------------------ |
| unit 一覧  | 分解した作業単位の名前・順序・依存                                       |
| 未確定     | まだ unit に切れない項目と、切れない理由                                 |
| スコープ外 | 目的の外と判断した項目と、その判断理由                                   |

- **未確定に置くかの判定基準**は「いま答えを出せるか」ではなく「**いま問いを正確に述べられるか**」とする。問いを述べられるものは unit にする(答えを出す作業が調査であれば、調査を目的とする unit にする)。問いを述べられないものを未確定に残す。
- 未確定の項目は、問いを述べられるようになった時点で unit 一覧へ移す。移動はユーザーの承認を経て行い、移した項目は未確定の節から消す。この編集は `roadmap-approved → roadmap-generated` の差し戻しで行い、編集後に `approve roadmap` で再承認する(凍結前に限る)。
- スコープ外の項目は理由とともに残す(削除すると同じ検討が再燃する)。
- 承認ゲートは `roadmap` の 1 つとし、経路 D・E では roadmap 提示後にユーザーの明示承認を取る。経路 C(unit 1 つ)では経路判定の承認をこのゲートの承認として扱い、追加の停止を挟まない。

**凍結の順序**: 配下の全 unit が `completed` に達したら、次の順に行う。順序を入れ替えない。

1. 未確定・スコープ外の項目のうち以後も参照する必要があるものを、恒久情報の配置規約(`../dev-core/references/durable-info.md`)に従って ADR・README へ移す。移す案をユーザーに提示し、承認を得てから移す。
2. 移動に伴う `roadmap.md` の編集を終える。
3. `<engine> set-state --def ./roadmap.json --workdir <roadmap のディレクトリ> frozen` で凍結する。エンジンが `roadmap.md` の sha256 を `state.json` の `frozen` に記録し、以後の変更を `check.py` が error として検出する。

恒久情報への移動を凍結より先に置く理由は、移動が `roadmap.md` の編集を伴うためである。先に凍結すると、移動のたびに凍結違反になる。凍結は「移し終えて参照専用になった」という状態を記録する操作であり、移動を禁じる操作ではない。

## 3. ブランチ運用

- 新規 unit の開始時(Step 0 の手順 4 で `init` を実行する直前)、git 管理下であれば現在のブランチから作業ブランチ `<unit>` を作成・切替する(`git switch -c <unit>`)。ブランチ名は unit 名とし、workdir の連番も roadmap 名も含めない。unit 名は roadmap を跨いで一意のため(1.)、この命名でブランチが衝突しない。既に同名ブランチがあれば切替のみ。git 管理下でなければスキップする。
- unit の完了後、作業ブランチの PR 作成と CI 追従を Step 4 で行う。main 等への統合(マージ)は人間に委ねる。flow-sdd は自動マージしない。
- 経路 B(unit 化しない軽微変更)は現在のブランチで作業する。

## 4. 承認ゲート

すべての承認ゲート(ルーティング・roadmap・spec・tasks)は**人間承認**とする。承認は明示的に取り、沈黙・「お任せ」を承認と見なさない。実装(タスクループ・最終検証)は部品の設計どおり自走するが、次の状況では停止して人間に報告する。

- `_Blocked:` の発生・最終検証 NO-GO・上流への差し戻しが必要な状況。
- roadmap の未確定項目を unit 一覧へ移すとき(2.1)。
- 実装完了後の知識 port への教訓の昇格と、凍結文書との乖離の恒久情報への反映(`../dev-implement/SKILL.md` 15.)。本番デプロイ等の不可逆操作はこのワークフローでは扱わない(dev-release とユーザーの明示承認に委ねる)。

> 承認の自動化(自己承認・自走)はこの composition の要件から除外する。必要な場合は、部品を直接束ねる拡張ワークフロー(Layer 3)として実現する(flow 同士は参照しない)。

## 5. 実行手順

以下、`<engine>` = `../dev-core/scripts/state.py`(絶対パスに解決)。`--def` は unit の操作では `./workflow.json`、roadmap の操作では `./roadmap.json` を指す。unit の `--workdir` は `init` が作った `docs/specs/NNN-<roadmap 名>/NNN-<unit>/`、roadmap の `--workdir` は `docs/specs/NNN-<roadmap 名>/`。

### 5.0. 工程の委譲

**自走し、かつ作業内容がこのセッションの文脈を圧迫する工程をサブエージェントへ委譲する**。ユーザーとの対話を伴う工程と、1 パスで終わって文脈が積もらない工程は、このセッションが自分で実行する。委譲の目的は、工程の作業内容(調査の本文・タスクごとの実装レポート・検証の出力)をこのセッションの文脈から外し、判断に使う情報だけを残すことである。

| 工程 | 実行 | 理由 |
| ---- | ---- | ---- |
| Step R: roadmap の作成 | `dev-roadmap-planner` へ委譲(提示と承認はこのセッション) | 分解の材料(企画書・既存コードベース)の読み込みを隔離する |
| Step 1: 仕様フェーズ | このセッションが実行 | dev-spec の Step 2(質問駆動)と Step 3(契約の壁打ち)がユーザーとの往復で成り立つため、委譲できない |
| Step 2: タスク分解フェーズ | このセッションが実行 | 1 パスで終わり、生成物と機械検査の出力が圧縮に至らない。内蔵ゲートの `QUESTIONS` にその場で応じられる(D-030) |
| Step 3: 実装フェーズ | `dev-implement-runner` へ委譲 | 自走する工程であり、文脈の消費が最も大きい |

- **起動プロンプトは最小限にする**。渡すのは workdir・作業単位名・実行する SKILL.md のパスだけとする。工程の手順・生成規則・判定基準はファイルにあり、プロンプトへ転記しない(指示とデータの分離は `../dev-core/references/orchestration-patterns.md`)。仕様・タスクの内容も転記せず、委譲先が workdir から読む。
- **委譲先はユーザーに質問できない**。`AskUserQuestion` はすべてのサブエージェントから除かれるため、部品の SKILL.md が質問を指示する箇所(内蔵ゲートの `QUESTIONS` 等)で委譲先は停止し、構造化結果の `OPEN_QUESTIONS` に論点を挙げて返す。このセッションがユーザーに諮り、回答を添えて再委譲する。
- **状態遷移は委譲先が行わない**(6.)。委譲の前後でこのセッションが `set-state`・`approve` を実行する。
- 委譲先は更にサブエージェントを起動する(`dev-implement-runner` → dev-implementer 等)。委譲先は部品の SKILL.md が指示する相手だけを起動し、自分の判断で委譲先を決めない。
- 委譲先が返す構造化結果の項目は各エージェント定義が定める。このセッションはその項目だけを読んで次の分岐を決め、作業内容の本文を展開しない。

### Step R: roadmap の作成(新規の作業のかたまりを始めるとき)

経路 C・D・E で新しい作業のかたまりを始めるときに 1 度だけ実行する。既存の roadmap に unit を足す場合は実行せず、Step 0 へ進む。

1. `<engine> init --def ./roadmap.json --root docs/specs --unit <roadmap 名>` で roadmap のディレクトリを作る。エンジンが連番を採番し、そのパスを `workdir:` 行に出力する。
2. `dev-roadmap-planner` を 1 体起動し、`../dev-roadmap/SKILL.md` の手順で `roadmap.md` を生成させる。渡すのは workdir・依頼内容・参照する要求文書のパス・実行する SKILL.md のパスだけとする(5.0)。
3. 返却の `STATUS` で分岐する。`NEEDS_DECISION` なら `OPEN_QUESTIONS` をユーザーに諮り、回答を添えて再委譲する。`ROADMAP_READY` なら次へ進む。
4. `<engine> set-state --def ./roadmap.json --workdir <roadmap のディレクトリ> roadmap-generated`。
5. 経路 D・E では roadmap を提示してユーザーの明示承認を待つ。経路 C では経路判定の承認をこのゲートの承認として扱い、停止しない(2.1)。
6. `<engine> approve --def ./roadmap.json --workdir <roadmap のディレクトリ> roadmap`。`roadmap.md` と state.json をコミットする(例: `docs(<roadmap 名>): roadmap を作成`)。

### Step 0: セッション開始手順(再開判定と初期化)

セッションは前回の記憶を持たずに始まる。現在地は記憶からではなく**ファイルから再導出**する。新規・再開のどちらでも次の順に実行し、順序を入れ替えない。

1. **位置の確認**: リポジトリのルートを確認する(`git rev-parse --show-toplevel`)。git 管理下であれば現在のブランチも確認する(`git branch --show-current`)。git 管理下でなければその旨を記録し、以降で git・`gh` を使う部分(手順 2 の `git log`、手順 4 の `gh pr view`)を省く。手順 3 は git に依存しないため実行する。
2. **workdir の特定と状態・履歴の読み込み**: `<engine> scan --def ./workflow.json --def ./roadmap.json --root docs/specs` で既存の roadmap と workdir を名前・状態とともに一覧し、対象 unit の workdir を特定する(workdir 名は連番を含み、パスは roadmap のディレクトリを挟むため、unit 名からパスを組み立てない)。出力の各行は `workflow` を持ち、`roadmap` の行が roadmap のディレクトリ、`sdd` の行が unit の workdir を指す。特定できたら `<engine> status --def ./workflow.json --workdir <特定した workdir>` で状態・承認・凍結を読む。あわせて `git log --oneline -10` で直近のコミットを読み、状態と履歴が食い違わないかを確認する。食い違いとは、状態が示すフェーズより後の成果物がコミットされている(例: `spec-generated` なのに tasks.md の生成コミットがある)、または状態が示す成果物のコミットが履歴に無いことをいう。食い違う場合は停止して報告する。一覧に対象の unit が無ければ新規の作業単位として扱う。
3. **開始時の検証**: workdir に `tasks.md` が実在すれば、そのタスク固有情報が挙げる検証コマンドを実行し、グリーンであることを確認する。前セッションが残した破損をここで検出する。失敗した場合は、その修復を最初の作業として扱い、新しいフェーズへ進まない。`tasks.md` が無ければ実行対象が無いため省く(通常は `initialized`・`spec-generated`・`spec-approved` が該当する)。状態が `tasks-generated` 以降なのに `tasks.md` が無い場合は、状態とファイルの食い違いとして停止し報告する。
4. **次の作業の選択**: 状態に対応するフェーズから続行する。`completed` なら凍結済みであることを伝えたうえで、作業ブランチの PR と CI の状態を確認し(`gh pr view`)、PR 未作成または CI 未グリーンなら Step 4 を実行してから停止する。同じ roadmap に未完了の unit があれば次の unit を案内する。新規なら、ブランチ作成(3.)の後に `<engine> init --def ./workflow.json --root <roadmap のディレクトリ> --unit <unit> --unique-root docs/specs` で初期化する。エンジンが roadmap のディレクトリ直下で連番を採番して workdir を作り、そのパスを `workdir:` 行に出力する。以降の `--workdir` にはこのパスを使う。`--unique-root` を付けるため、他の roadmap に同名の unit があれば `init` が拒否する(1.)。

コンテキストの圧縮(compaction)が起きた後も、同じ 1〜4 を実行して現在地を再導出する。要約に残す情報は、workdir のパス・現在のフェーズ・直近の失敗の内容と原因とする(最後の 1 つはファイルに残らないため)。再開の成否をこの要約に依存させない。

### Step 1: 仕様フェーズ

1. `../dev-spec/SKILL.md` の手順を workdir 上書きで実行する(質問駆動の確定 → 契約の壁打ち → EARS 受け入れ基準 → 内蔵ゲート → 保存・コミット)。
2. `<engine> set-state spec-generated`。
3. ユーザーに spec.md のレビューを依頼し、**明示的な承認を待つ**(沈黙・「お任せ」を承認と見なさない)。修正要望があれば同状態のまま反映して再提示する。
4. 承認を得たら `<engine> approve spec`。state.json の更新は `chore(<unit>): spec 承認に伴い state.json を更新` としてコミットする。

### Step 2: タスク分解フェーズ

1. `../dev-decompose/SKILL.md` の手順を workdir 上書きで実行する(File Structure Plan → タスクへの分解 → 内蔵ゲート → 機械検査 → 保存・コミット)。内蔵ゲートが `QUESTIONS` を挙げたらユーザーに諮り、回答を得てから続ける。
2. `<engine> set-state tasks-generated`。
3. tasks.md を提示してユーザーの明示承認を待つ。修正要望があれば同状態のまま反映して再提示する。
4. 承認を得たら `<engine> approve tasks`。
5. 仕様の不備で分解できない場合は、2. 以降へ進まず `<engine> set-state spec-generated` で差し戻し、仕様フェーズからやり直す。

### Step 3: 実装フェーズ

1. `<engine> set-state implementing`。
2. `dev-implement-runner` を 1 体起動し、`../dev-implement/SKILL.md` の手順を自律モードで実行させる(タスクごとに implementer → reviewer → 検証 → コミット、最終検証パネルまで)。渡すのは workdir・作業単位名・実行する SKILL.md のパスだけとする(5.0)。
3. 返却の `STATUS` で分岐する。
   - `GO`: `<engine> set-state completed`(エンジンが中間生成物を凍結する)。`check.py` で error が無いことを確認し、state.json 更新をコミットして unit の完了を報告し、Step 4 へ進む。
   - `TASK_DEFECT`: `<engine> set-state tasks-generated` で差し戻す。
   - `NO_GO` / `UNVERIFIED` / `BLOCKED`: 状態を進めず、返却の `FINDINGS`・`UNVERIFIED`・`BLOCKED_REASON` をユーザーに報告して停止する(4.)。
4. 返却の `LESSONS` が「なし」でなければ、知識 port への昇格の可否をユーザーに諮る(4.)。
5. 返却の `DRIFT` が「なし」でなければ、恒久情報への反映(反映先と記述案)の可否をユーザーに諮り、承認を得た分を反映してコミットする。反映先はテスト・doc コメント・ADR・README・用語集であり、凍結済みの中間生成物は直さない(`../dev-core/references/durable-info.md` 3.)。

### Step 4: PR 作成と CI 追従

1. 作業ブランチの PR を作成し、CI がグリーンになるまで追従する。適用条件・手順・有界リトライ(修正ラウンドは 1 PR あたり最大 3 回)・規律の正本は `../dev-core/references/git-convention.md` §9(適用条件を満たさない場合はスキップして 4. の報告へ進む)。
2. CI 失敗の修正は `../dev-implement/SKILL.md` の軽量タスク定義(経路 B 相当)で行う。凍結済みの中間生成物(`docs/specs/NNN-<roadmap 名>/NNN-<unit>/`)は変更せず、コードとテストだけを修正する。失敗の原因が仕様の欠陥に起因する場合は、修正で吸収せず停止してユーザーに報告する(凍結後の上流の手戻りは人間の判断に委ねる)。
3. 修正ラウンドの上限超過等でグリーンにできない場合は、失敗しているチェックと試行した修正を報告して停止する。
4. CI グリーンと PR の URL を報告する。**PR のマージは人間に委ねる**(この flow は行わない)。同じ roadmap に未完了の unit があれば次の unit へ進む(各 unit の承認ゲートで停止しながら進める)。経路 B(現在のブランチで作業する軽微変更)では本 Step を実行しない。
5. roadmap の全 unit が `completed` に達したら、2.1 の「凍結の順序」に従って恒久情報への移動・`roadmap.md` の編集・`frozen` への遷移をこの順に行う。凍結後の roadmap は参照専用になる。

### 差し戻しの補足

- 実装中に仕様(契約・受け入れ基準)の欠陥が見つかった場合は、`implementing → tasks-generated → spec-generated` と順に差し戻す(状態機械に飛び越し遷移は定義しない)。

## 6. 規律(厳守)

- **生成ロジックを置き換えない**: 各フェーズは対応する部品の SKILL.md の手順(内蔵ゲート含む)をそのまま実行する。委譲する工程(5.0)でも、実行するのは部品の SKILL.md であり、委譲先のエージェント定義は手順を持たない。この composition が部品の生成規則・テンプレートを再定義しない。
- **状態遷移は composition だけが行う**: 部品は成果物を書いたら完了。`<engine>` の操作(init / set-state / approve)は**このセッション**の手順でのみ実行する。工程を委譲しても状態遷移は委譲先へ渡さない(委譲先は成果物を書いて構造化結果を返すところまでを担う)。state.json を手書きしない。roadmap の状態遷移も同じ扱いとする。
- **凍結後は変更しない**: unit は `completed`、roadmap は `frozen` に到達した後の中間生成物が参照専用になる。実装後の差異はコードと恒久情報へ反映する(`../dev-core/references/durable-info.md`)。
- 間接プロンプトインジェクション耐性・構造化受け渡し等の安全則は `../dev-core/references/orchestration-patterns.md` に従う。

## 7. 対応しないこと

- 本番デプロイの自動実行(不可逆操作は常に人間承認。出荷判定は dev-release を使う)。
- ブランチの自動マージ・PR の自己承認(人間のレビューに委ねる。PR 作成と CI 追従は Step 4 で行う)。
- 承認済みゲートの承認取り消し(差し戻し後の approvals フラグは true のまま残る。再承認は同じ approve 操作で上書きされる)。
- 承認ゲートの自己承認・自走モード(必要な場合は部品を直接束ねる拡張ワークフローとして実現する)。
- **非対話実行(`claude -p` 等の headless)**。承認ゲートは人間の応答を待つため、非対話では停止する。自走が要る場合は拡張ワークフロー(Layer 3)で実現し、その拡張が反復上限・権限の限定・コスト監視を定める。
- **ターン終了をブロックする hook(Stop hook)による自走**。承認ゲートに到達したターンの終了は、ユーザーの応答を待つための正しい停止であり、検証未達での停止と区別できない。
- **ワークフロー全体の反復上限**。部品が持つ有界リトライ(`../dev-implement/SKILL.md` 10.、`../dev-core/references/git-convention.md` 9.3)で個々のループは止まり、フェーズ間の進行は承認ゲートが止める。全体の上限は自走する拡張ワークフローが定める。
