---
name: dev-design
description: 設計部品(サブ)。workdir の requirements.md をもとに、責務境界(Boundary Map)・File Structure Plan(変更/新規ファイルと責務)・Requirements Traceability(要件と設計の対応)を定めた design.md を生成する。既存コードベース拡張では Gap 分析を先に行う。要件が固まり実装方針を設計したいときに、単独・ワークフロー内のどちらでも使う。
---

# dev-design — 設計の作成

要件を「どう実現するか」へ翻訳し、`<workdir>/design.md` を生成するサブ部品。要件 ID からのトレーサビリティと、具体的なファイル単位の計画(File Structure Plan)を中核に据える。

## 1. 契約

- **workdir**: 呼び出し時の引数で指定する。既定は `docs/dev/`(composition は上書きする)。
- **入力**: `<workdir>/requirements.md`(推奨)。存在しない場合は停止せず、依頼文と AskUserQuestion で設計対象の要点(実現する振る舞い・制約)を確定してから進む(requirements.md の生成はしない。必要なら dev-requirements を案内する)。
- **出力**: `<workdir>/design.md`。調査・Gap 分析のログは `<workdir>/research.md`(任意)。
- **port**: `docs/dev/ports/` 配下(階層自由)。選択はポートマッピング(`../dev-core/references/ports.md`)に従う(原則・言語・機能別の知識を設計判断の根拠に使う)。

## 2. 参照(必読)

- 記法規約(要件 ID・トレーサビリティ): `../dev-core/references/notation.md`
- 契約による設計・ドメインモデルの完全性: `../dev-core/references/contract-and-domain.md`
- Git 運用規約: `../dev-core/references/git-convention.md`
- ソース駆動の根拠提示: `../dev-core/references/source-driven.md`(外部ライブラリの API を設計に用いるとき)
- 文書ゲート: `../dev-core/templates/doc-gate-prompt.md`
- テンプレート: `./templates/design-template.md`, `./templates/research-template.md`(任意)

## 3. ステップ

### Step 1: コンテキスト読込

- `<workdir>/requirements.md` を読み、全要件 ID を把握する。
- 知識 port をポートマッピング(`../dev-core/references/ports.md`)に従って読み、設計の前提にする(走査は `ports.py --skill dev-design`、条件判定と本文読込はその結果から)。
- workdir が凍結済み(state.json が完了状態)の場合は再生成せず停止し、新しい作業単位を案内する。

### Step 2: 既存コードの調査と Gap 分析(既存コードベース拡張時)

既存システムへの変更を伴う場合、設計に入る前に**要件と既存コードの差分(gap)**を分析する。影響範囲・既存パターン・依存関係の調査は dev-explorer に隔離し、メイン文脈にはダイジェストのみ戻す。調査・分析ログは `research.md` に残す。新規プロジェクトや既存コードに依存しない作業では省略して Step 3 へ進む。

Gap 分析の成果物(判断ではなく情報提供。方針決定は設計本体で行う):

- **現状調査**: 再利用可能な既存資産、支配的なアーキテクチャパターン・規約(命名・レイヤリング・依存方向・テスト配置)、統合面(データモデル・API クライアント・認証機構)。
- **要件 → 資産マップ**: 各要件 ID が必要とする技術要素を既存資産に対応づけ、`Missing` / `Unknown` / `Constraint` でタグ付けする。
- **実装方針の選択肢**: A: 既存拡張 / B: 新規作成 / C: ハイブリッドを、トレードオフ(後方互換・テスト影響・凝集度)と工数規模(S/M/L/XL)・リスク付きで提示する。

### Step 3: 境界優先の設計

- **Boundary Map**: どのコンポーネントが何を所有するかを先に定める。各コンポーネントの inbound/outbound/external 依存とインターフェース(契約)を定義し、依存方向を単方向に保つ。レイヤ構成は知識 port の原則(あれば)に従い、ビジネスロジックの集約先の層を明示する(貧血ドメインモデルを避ける。`contract-and-domain.md` §2.2)。
- **契約とドメインモデルの完全性**(`../dev-core/references/contract-and-domain.md`): 各インターフェース契約に事前条件・事後条件を明記する。ドメインモデルには不変条件を列挙し、生成時に強制する設計(完全コンストラクタ・型による表現不能化)を示す。完全性と純粋性が衝突する判断は design.md に記録する。
- データモデル、エラーハンドリング、テスト戦略を設計する。
  - **エラーハンドリング**: 異常系ごとの分類・ステータスは既存のエラー型の実コードを確認してから採用する。意味が合わなければ専用の型を新設し、推測で既存を流用しない。
  - **テスト戦略**: モックはシステム境界(外部 API・非決定性)のみに限定する設計にする。時刻依存テストは時刻を固定して決定論化する。横断的責務の所在は単一の層に定める。
- **脅威モデル(セキュリティ関連時)**: 認証・認可・外部入力・機微データを扱う設計では、信頼境界を明示し、境界ごとに STRIDE で脅威を洗い出して対策を設計とエラーハンドリングに反映する。要件に無い異常系が見つかれば requirements.md への差し戻しを提案する(勝手に要件を追加しない)。

### Step 4: File Structure Plan

変更・新規作成するファイルを具体パスで列挙し、各ファイルの責務を 1 行で示す。これがタスク分解(dev-decompose)の土台になる。既存の置換・廃止を伴う場合は**削除対象**も明示し、「使用ゼロを確認してから削除する」手順を設計に含める。

### Step 5: Requirements Traceability

全要件 ID が設計のどの要素でカバーされるかを表で対応付ける。**未カバーの要件があってはならない**。要件に対応しない設計要素(過剰設計)も指摘する。要件内容は requirements.md から逐条転記する(推測で補わない = 名目被覆を避ける)。「既存実装で担保済み」と記す行は、実コードの根拠を備考に添える。

### Step 6: 内蔵レビューゲート

`../dev-core/templates/doc-gate-prompt.md` で dev-reviewer を独立文脈で 1 体起動し、文書ゲート系 3 観点で判定させる。種別固有チェックとして以下を渡す。

- 全要件 ID がトレーサビリティ表に存在するか(未カバー禁止)。過剰設計がないか。
- File Structure Plan のパスがプロジェクト既存の構成規約に沿うか。
- 循環依存・責務の重複がないか。
- 知識 port の原則(あれば)に反していないか。
- 上流(要件)で禁止・非許容とされた事項を、設計の都合で覆していないか(覆す必要は要件側への変更提案として差し戻す)。
- 「担保済み」主張に根拠(実コードの参照)が添えられているか。

`REJECTED` なら最大 2 回自己修復し、`QUESTIONS` は `AskUserQuestion` で解消する。

### Step 7: 保存とコミット

- `design.md`(必要に応じ `research.md`)を保存する。`check.py --workdir <workdir>` で機械検査する(トレーサビリティ表の前方/後方照合・残存マーカー。state.json があれば `--def` も付ける)。warning は Step 5/6 に戻して解消する。
- git-convention.md に従い commit & push する(例: `docs(<unit>): 設計(File Structure Plan/Traceability)を生成`)。

### Step 8: 停止

- design.md を提示し、ユーザーのレビューを待って停止する。状態遷移はこの部品では行わない。
- 次の部品として dev-decompose(タスク分解)を案内する。

## 4. 注意

- 設計は要件に対する HOW。要件にない機能を勝手に足さない。必要な追加は要件へ差し戻す。
- 型安全性を損なう設計(`any` 等)を避ける(知識 port の原則があればそれに従う)。
- 外部ライブラリ/フレームワーク固有の API を設計の根拠にするときは、使用バージョンの公式情報を出典として確認する(`source-driven.md`)。確認できない事項は `UNVERIFIED` と明示する。
