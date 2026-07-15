---
name: flow-sdd
description: SDD(仕様駆動開発)ワークフロー。依頼を経路判定(ルーティング)して作業単位に分け、各単位を仕様(dev-spec: 壁打ちで契約と受け入れ基準を確定)→タスク分解(dev-decompose)→実装(dev-implement)の順に、承認ゲート付きの状態機械で駆動する composition。実装完了後は作業ブランチの PR を作成し、CI がグリーンになるまで失敗の修正を追従する(マージは人間に委ねる)。承認ゲートはすべて人間承認とする(自走・自己承認は、部品を直接束ねる拡張ワークフロー側で実現する)。「SDD で進めて」「仕様駆動で開発して」「仕様から実装まで通しで」と頼まれたとき、または中断した SDD 作業を再開するときに使う。
---

# flow-sdd — SDD ワークフロー

部品を状態機械で束ね、依頼を作業単位(unit)へ振り分けて仕様 → タスク分解 → 実装まで駆動する composition。各フェーズの生成ロジックは部品の SKILL.md をそのまま実行し、この composition は**ルーティング・状態遷移・承認ゲート・フェーズ間の接続だけ**を担う。

## 1. 契約

- **作業単位(unit)**: 小文字ケバブケースのスラッグ(例: `user-auth`)。ルーティング(2.)で決める。
- **workdir**: `docs/specs/<unit>/`(各部品の既定 workdir をこの値で上書きする)。
- **roadmap**(複数 unit のとき): `docs/specs/roadmap.md`。unit の一覧・順序・依存を記す中間生成物(全 unit 完了で役目を終える)。
- **状態機械定義**: `./workflow.json`。状態遷移はすべて dev-core のエンジン経由で行う(`../dev-core/references/static-check.md`)。

```
initialized → spec-generated →(gate: spec)→ spec-approved
→ tasks-generated →(gate: tasks)→ tasks-approved
→ implementing → completed(凍結)
差し戻し: tasks-generated→spec-generated / implementing→tasks-generated
```

## 2. ルーティング(入口)

依頼を分析し、次の経路に振り分ける。判断根拠を 1〜2 行で示し、経路の確定は**人間承認**とする。

| 経路 | 状況                                                   | アクション                                                                                                                |
| ---- | ------------------------------------------------------ | ------------------------------------------------------------------------------------------------------------------------- |
| A    | 既存 unit の責務範囲内で拡張できる                     | 該当 workdir の状態を `status` で確認し、該当フェーズから再開する。凍結済み(completed)なら新しい unit(経路 C)に切り替える |
| B    | 作業単位化が不要(バグ修正・設定変更・軽微で境界が明確) | dev-implement を直接使う(軽量タスク定義)。状態機械は使わない                                                              |
| C    | 新規の作業 1 単位が妥当                                | unit 名を決めて Step 0 から開始する                                                                                       |
| D    | 大きく、複数 unit へ分解すべき                         | `docs/specs/roadmap.md` に unit 一覧・順序・依存を生成し、ユーザー承認後に各 unit を順に Step 0 から駆動する              |
| E    | 混合(既存更新 + 新規 + 軽微が混在)                     | roadmap で全体を整理し、各項目を A〜C に割り当てる                                                                        |

- 経路判定に既存コードベースの把握が要る場合は dev-explorer に隔離し、ダイジェストのみ受け取る。
- 1 unit の規模は dev-decompose の目安(要件 10 以下・メインタスク 8 以下)に収まるよう分解する。

## 3. ブランチ運用

- unit の開始時(Step 0 の直前)、git 管理下であれば現在のブランチから作業ブランチ `<unit>` を作成・切替する(`git switch -c <unit>`)。既に同名ブランチがあれば切替のみ。git 管理下でなければスキップする。
- unit の完了後、作業ブランチの PR 作成と CI 追従を Step 4 で行う。main 等への統合(マージ)は人間に委ねる。flow-sdd は自動マージしない。
- 経路 B(unit 化しない軽微変更)は現在のブランチで作業する。

## 4. 承認ゲート

すべての承認ゲート(ルーティング・spec・tasks)は**人間承認**とする。承認は明示的に取り、沈黙・「お任せ」を承認と見なさない。実装(タスクループ・最終検証)は部品の設計どおり自走するが、`_Blocked:` 発生・最終検証 NO-GO・差し戻しが必要な状況では停止して人間に報告する。本番デプロイ等の不可逆操作はこのワークフローでは扱わない(dev-release とユーザーの明示承認に委ねる)。

> 承認の自動化(自己承認・自走)はこの composition の要件から除外する。必要な場合は、部品を直接束ねる拡張ワークフロー(Layer 3)として実現する(flow 同士は参照しない)。

## 5. 実行手順

以下、`<engine>` = `../dev-core/scripts/state.py`(絶対パスに解決)、`--def` は `./workflow.json`、`--workdir` は `docs/specs/<unit>/`。

### Step 0: 再開判定と初期化

- workdir に `state.json` があれば `<engine> status` で現在地を復元し、状態に対応するフェーズから続行する。`completed` なら凍結済みであることを伝えたうえで、作業ブランチの PR と CI の状態を確認し(`gh pr view`)、PR 未作成または CI 未グリーンなら Step 4 を実行してから停止する。roadmap があれば未完了の次 unit を案内する。
- 新規なら、ブランチ作成(3.)の後に `<engine> init --unit <unit>` で初期化する。

### Step 1: 仕様フェーズ

1. `../dev-spec/SKILL.md` の手順を workdir 上書きで実行する(質問駆動の確定 → 契約の壁打ち → EARS 受け入れ基準 → 内蔵ゲート → 保存・コミット)。
2. `<engine> set-state spec-generated`。
3. ユーザーに spec.md のレビューを依頼し、**明示的な承認を待つ**(沈黙・「お任せ」を承認と見なさない)。修正要望があれば同状態のまま反映して再提示する。
4. 承認を得たら `<engine> approve spec`。state.json の更新は `chore(<unit>): spec 承認に伴い state.json を更新` としてコミットする。

### Step 2: タスク分解フェーズ

1. `../dev-decompose/SKILL.md` の手順を実行する。
2. `<engine> set-state tasks-generated` → ユーザーの明示承認 → `<engine> approve tasks`。
3. 仕様の不備が見つかったら `<engine> set-state spec-generated` で差し戻し、仕様フェーズからやり直す。

### Step 3: 実装フェーズ

1. `<engine> set-state implementing`。
2. `../dev-implement/SKILL.md` の手順を自律モードで実行する(タスクごとに implementer → reviewer → 検証 → コミット、最終検証パネルまで)。
3. タスク定義の不備(NEEDS_CONTEXT が解消できない等)が見つかったら `<engine> set-state tasks-generated` で差し戻す。
4. 最終検証が **GO** になったら `<engine> set-state completed`(エンジンが中間生成物を凍結する)。`check.py` で error が無いことを確認し、state.json 更新をコミットして unit の完了を報告し、Step 4 へ進む。

### Step 4: PR 作成と CI 追従

1. 作業ブランチの PR を作成し、CI がグリーンになるまで追従する。適用条件・手順・有界リトライ(修正ラウンドは 1 PR あたり最大 3 回)・規律の正本は `../dev-core/references/git-convention.md` §8(適用条件を満たさない場合はスキップして 4. の報告へ進む)。
2. CI 失敗の修正は `../dev-implement/SKILL.md` の軽量タスク定義(経路 B 相当)で行う。凍結済みの中間生成物(`docs/specs/<unit>/`)は変更せず、コードとテストだけを修正する。失敗の原因が仕様の欠陥に起因する場合は、修正で吸収せず停止してユーザーに報告する(凍結後の上流の手戻りは人間の判断に委ねる)。
3. 修正ラウンドの上限超過等でグリーンにできない場合は、失敗しているチェックと試行した修正を報告して停止する。
4. CI グリーンと PR の URL を報告する。**PR のマージは人間に委ねる**(この flow は行わない)。roadmap がある場合は次の unit へ進む(各 unit の承認ゲートで停止しながら進める)。経路 B(現在のブランチで作業する軽微変更)では本 Step を実行しない。

### 差し戻しの補足

- 実装中に仕様(契約・受け入れ基準)の欠陥が見つかった場合は、`implementing → tasks-generated → spec-generated` と順に差し戻す(状態機械に飛び越し遷移は定義しない)。

## 6. 規律(厳守)

- **生成ロジックを置き換えない**: 各フェーズは対応する部品の SKILL.md の手順(内蔵ゲート含む)をそのまま実行する。この composition が部品の生成規則・テンプレートを再定義しない。
- **状態遷移は composition だけが行う**: 部品は成果物を書いたら完了。`<engine>` の操作(init / set-state / approve)はこの SKILL の手順でのみ実行する。state.json を手書きしない。
- **凍結後は変更しない**: `completed` 到達後の中間生成物は参照専用。実装後の差異はコードと恒久情報へ反映する(`../dev-core/references/durable-info.md`)。
- 間接プロンプトインジェクション耐性・構造化受け渡し等の安全則は `../dev-core/references/orchestration-patterns.md` に従う。

## 7. 対応しないこと

- 本番デプロイの自動実行(不可逆操作は常に人間承認。出荷判定は dev-release を使う)。
- ブランチの自動マージ・PR の自己承認(人間のレビューに委ねる。PR 作成と CI 追従は Step 4 で行う)。
- 承認済みゲートの承認取り消し(差し戻し後の approvals フラグは true のまま残る。再承認は同じ approve 操作で上書きされる)。
- 承認ゲートの自己承認・自走モード(必要な場合は部品を直接束ねる拡張ワークフローとして実現する)。
