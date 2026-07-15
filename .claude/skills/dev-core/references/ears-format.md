# EARS 記法

EARS(Easy Approach to Requirements Syntax)は、要件をテスト可能で曖昧さのない構造化された文として記述する記法です。本スキル群では仕様文書 `spec.md` の受け入れ基準(Acceptance Criteria)に用います。

## 5 つの構文パターン

| パターン               | 構文                                                        | 用途                           |
| ---------------------- | ----------------------------------------------------------- | ------------------------------ |
| Ubiquitous(常時)       | The [システム] shall [振る舞い]                             | 常に成り立つ要件               |
| Event-driven(イベント) | When [イベント], the [システム] shall [振る舞い]            | 特定の出来事への応答           |
| State-driven(状態)     | While [状態], the [システム] shall [振る舞い]               | ある状態が継続する間の振る舞い |
| Unwanted(異常系)       | If [望ましくない条件], then the [システム] shall [振る舞い] | エラー・例外処理               |
| Optional(オプション)   | Where [機能が含まれる場合], the [システム] shall [振る舞い] | 特定構成でのみ有効な要件       |

## 日本語での記述

本スキル群は日本語で運用するため、EARS の構造を保ったまま日本語で記述します。原文の構造語(When/While/If/Where/shall)は対応する日本語で表現します。

| 英語                              | 日本語表現                                       |
| --------------------------------- | ------------------------------------------------ |
| The system shall ...              | システムは〜しなければならない                   |
| When ..., the system shall ...    | 〜のとき、システムは〜しなければならない         |
| While ..., the system shall ...   | 〜の間、システムは〜しなければならない           |
| If ..., then the system shall ... | 〜の場合、システムは〜しなければならない         |
| Where ..., the system shall ...   | 〜が含まれる場合、システムは〜しなければならない |

## 記述例

- **常時**: システムは、すべての API レスポンスに一意のリクエスト ID を付与しなければならない。
- **イベント**: ユーザーがログインボタンを押下したとき、システムは入力された認証情報を検証しなければならない。
- **状態**: アップロード処理が進行中の間、システムは進捗率を 1 秒ごとに更新しなければならない。
- **異常系**: 認証情報が無効な場合、システムはエラーメッセージを表示し、ログイン画面に留まらなければならない。
- **オプション**: 二要素認証が有効化されている場合、システムはワンタイムコードの入力を要求しなければならない。

## 品質ルール

- **数値 ID のみ**: 要件見出しは "Requirement 1: ..." のように数値 ID を用いる。アルファベット ID(Requirement A)は使わない。受け入れ基準も `1.1`, `1.2` のように番号付けし、plan / tasks からトレースできるようにする。
- **1 文 1 振る舞い**: 1 つの受け入れ基準には 1 つの検証可能な振る舞いのみを書く。
- **曖昧語の排除**: 「適切に」「高速に」「ユーザーフレンドリーに」等の測定不能な語を避け、閾値・条件を明示する。
- **推測で埋めない**: 不明点は推測で確定せず、生成フェーズでその場で全て質問して解消する。曖昧さを残さない。

## トレーサビリティ

各受け入れ基準の番号(例: `2.3`)は、`tasks.md` の `_Requirements: 2.3_` 注記から参照される。要件番号を変更する際は下流ドキュメントとの整合に注意する。

## 出典

EARS は Rolls-Royce の Alistair Mavin らが考案した記法。曖昧さ・複雑さ・冗長さ等の要件の問題を、限定した構造語と一定の節順で抑える。

- Mavin, Wilkinson, Harwood, Novak「Easy Approach to Requirements Syntax (EARS)」Proc. IEEE 17th International Requirements Engineering Conference (RE'09), 2009, pp.317–322: https://research.manchester.ac.uk/en/publications/easy-approach-to-requirements-syntax-ears/
- 概要: https://en.wikipedia.org/wiki/Easy_Approach_to_Requirements_Syntax
