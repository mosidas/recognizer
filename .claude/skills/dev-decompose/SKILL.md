---
name: dev-decompose
description: タスク分解部品(サブ)。workdir の requirements.md と design.md をもとに、実装タスク一覧(tasks.md)を生成する。各タスクはタスク固有情報(要件 ID・境界・依存・並行可否・対象ファイル・検証コマンド)を持ち、仕様の詳細は上流への参照で解決する(転記しない)。設計が固まり実装に入る前に、単独・ワークフロー内のどちらでも使う。
---

# dev-decompose — 実装タスクの分解

設計を実行可能なタスクへ分解し、`<workdir>/tasks.md` を生成するサブ部品。tasks.md は dev-implement が逐次消化する作業リストになる。**仕様の詳細は tasks.md へ転記せず、requirements.md / design.md への参照(要件 ID・節)で解決する**(参照による自己完結)。

## 1. 契約

- **workdir**: 呼び出し時の引数で指定する。既定は `docs/dev/`(composition は上書きする)。
- **入力**: `<workdir>/requirements.md`(必須)と `<workdir>/design.md`(推奨)。requirements.md が無い場合: 軽微な変更なら dev-implement(軽量タスク定義)を案内する。分解が必要な規模なら、AskUserQuestion で受け入れ基準の要点を確定してから進む(確定内容は requirements.md として保存する)。
- **出力**: `<workdir>/tasks.md`。
- **port**: `docs/dev/ports/` 配下(階層自由)。選択はポートマッピング(`../dev-core/references/ports.md`)に従う(inject に dev-decompose を含むもの。例: 原則)。

## 2. 参照(必読)

- 記法規約(注記・マーカー): `../dev-core/references/notation.md`
- Git 運用規約: `../dev-core/references/git-convention.md`
- 文書ゲート: `../dev-core/templates/doc-gate-prompt.md`
- テンプレート: `./templates/tasks-template.md`

## 3. ステップ

### Step 1: コンテキスト読込

- `requirements.md`(全要件 ID)と `design.md`(File Structure Plan・Boundary Map・Traceability)を読む。
- 知識 port をポートマッピング(`../dev-core/references/ports.md`。走査は `ports.py --skill dev-decompose`)に従って読む。
- workdir が凍結済み(state.json が完了状態)の場合は再生成せず停止する。

### Step 2: タスク分解

design.md の File Structure Plan と Boundary Map を起点に、タスクを階層的に分解する。

- **垂直スライス優先**: 「DB 層を全部 → API 層を全部」のような水平分解ではなく、1 つの機能パスを縦に貫く単位で割る(各スライスが可能な限り end-to-end で動作・検証できる)。共通基盤の水平タスクは、それを使う最初の垂直スライスに含めるか、明示的な先行依存にする。
- メインタスク `- [ ] 1. <説明>`、サブタスク `- [ ] 1.1 <説明>`。テストを後回しにするタスクは `- [ ]*`。
- 各サブタスクに**タスク固有情報**を付ける(仕様の転記はしない):
  - `_Requirements: 1.2, 2.1_`(カバーする要件 ID。実装者はこの ID で requirements.md を読む)
  - `_Boundary: <Component>_`(所有コンポーネント)
  - `_Depends: 1.1_`(境界を越える依存)・`(P)`(境界が独立し並行可)
  - `_Knowledge: <name>_`(知識 port の**明示上書き**。既定では書かない — 実装時の選択は dev-implement の port マッピングが行う。ユーザーが特定 port の注入を固定したいと明示した場合、または条件判定が自明でないと分解時の対話で確定した場合のみ書く)
  - 対象ファイル(File Structure Plan から。新規/変更とテストファイル)
  - 設計参照(design.md の該当節。例: `design.md §6 AuthService`)
  - 検証コマンド(ビルド/テスト/リントの具体コマンド)
- **削除・廃止を伴うタスク**は、DB レコードだけでなく紐づく関連リソース(ストレージ・キャッシュ・外部サービス上のデータ・監査ログ等)のクリーンアップを説明に明示する(消し忘れの防止)。
- **ドキュメント反映を独立タスクにする**: 実装に伴って更新が要るドキュメント(README・CHANGELOG・API ドキュメント等)は独立サブタスクとして明示する(暗黙の巻き込みは反映漏れを生む)。

### Step 3: TDD を意識した順序付け

- 原則テストファースト。テスト作成 → 実装の順、または各タスク内で RED→GREEN を想定する。
- **分岐の網羅**: 条件によってエラー伝播や挙動が分岐する箇所は、各分岐に個別のテストタスクを割り当てる(片側のみにしない)。
- **リスク先行・契約先行**: 不確実性の高いタスク(外部連携・新技術・性能未知)を依存順の許す範囲で前に置く。並行(P)で開発するタスク群は、共有する契約(型・インターフェース)を確定するタスクを先行依存に置く。
- `_Depends:` に循環がないかを確認する。

### Step 4: 網羅性チェック

- すべての要件 ID が、いずれかのタスクの `_Requirements:_` でカバーされているか。
- File Structure Plan の全ファイルが、いずれかのタスクで作成/変更されるか。
- 漏れがあれば補い、過剰なタスク(設計にない作業)を除く。

### Step 5: 内蔵レビューゲート

`../dev-core/templates/doc-gate-prompt.md` で dev-reviewer を独立文脈で 1 体起動し、文書ゲート系 3 観点で判定させる。種別固有チェックとして以下を渡す(成果物横断の整合性検証)。

- **前方トレース**: 全要件 ID がタスクにカバーされているか(未カバー禁止)。
- **後方トレース**: 各タスクの `_Requirements:_`・設計参照が requirements.md / design.md に実在するか(dangling 禁止)。要件・設計にない過剰タスクがないか。
- `_Depends:` の循環がないか。`(P)` のタスク同士が共有ファイルを触っていないか。
- 各サブタスクにタスク固有情報(要件 ID・境界・対象ファイル・検証コマンド)が揃っているか。
- 1 タスクの規模がテスト込みで自己完結する範囲(目安: 数百行以内)か。

`REJECTED` なら最大 2 回自己修復し、`QUESTIONS` は `AskUserQuestion` で解消する。

### Step 6: 保存とコミット

- `./templates/tasks-template.md` に従って `tasks.md` を保存する。末尾に空の `## Implementation Notes` セクションを設ける(dev-implement が学習を追記する領域)。
- `check.py --workdir <workdir>` で機械検査する(要件カバレッジの前方/後方・`_Depends:` 循環・タスク固有情報・`_Knowledge:` の実在。state.json があれば `--def` も付ける)。`error` は解消、warning は Step 2〜4 に戻して補う。Step 5 の内蔵ゲートは機械検査で判定できない意味検証に集中する。git-convention.md に従い commit & push する(例: `docs(<unit>): 実装タスクを分解`)。

### Step 7: 停止

- tasks.md を提示し、ユーザーのレビューを待って停止する。状態遷移はこの部品では行わない。
- 次の部品として dev-implement(実装)を案内する。

## 4. 注意

- 1 タスクは 1 つの明確な成果に対応させる。大きすぎるタスクはサブタスクに分割する。
- **規模上限の目安**: 1 作業単位の要件数は目安 10 以下、メインタスク数は目安 8 以下。超える場合は作業単位の分割を提案する(コンテキスト崩壊・レビュー不能を避ける)。
- **リファクタと機能追加は別タスク**にする(混在は変更の意図とレビューを曖昧にする)。
- `(P)` は境界が真に独立している場合のみ付ける。共有ファイルを触るタスク同士には付けない。
- **テスト方針と実行環境の整合**: E2E 等のテスト手段は実行環境・CI と整合させ、環境非対応の手段を必須化しない。要件・設計が環境と矛盾する場合は分解前に上流へ差し戻す。
