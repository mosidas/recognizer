# CLAUDE.md

## リポジトリ概要

YOLO 形式の ONNX モデルで動作する顔検出・顔認証・物体検出のコアライブラリ(C# / .NET 10)。ライブラリを利用する CLI(`src/Recognizer.Cli`。publish 後の実行ファイル名は `recognizer`)を同梱する。

- 公開 API 仕様: `docs/api-spec.md`(この仕様を正とする)
- SDD 成果物: `docs/specs/<unit>/`(flow-sdd が生成)

## 言語規約

- 会話・ドキュメント・コード内コメント・PR/Issue 本文・コミットメッセージは日本語で記述しなければならない(MUST)。
- 技術用語(API・ONNX・NMS 等の業界標準語)は英語のまま使用してよい。
- ライブラリ・関数・変数・ファイルパスなどの識別子は当該エコシステムの慣習に従う(通常は英語)。
- 仕様書・テスト項目・ログメッセージ・エラーメッセージの自然言語部分は日本語にする。

## 開発コマンド

```bash
# ビルド
dotnet build

# テスト
dotnet test
```

## 技術スタック

- .NET 10 / C#(クラスライブラリ)
- Microsoft.ML.OnnxRuntime(CPU 推論)
- OpenCvSharp4(画像処理)
- xUnit(テスト)

## .NET コーディング規約

- nullable reference types を有効にする(`<Nullable>enable</Nullable>`)
- データ型には `record` を優先し、init プロパティでイミュータブルにする
- 継承が不要なクラスには `sealed` をつける
- プライベートフィールドは `_` プレフィックス + lowerCamelCase
- 予期されるエラー(顔未検出等)は結果型で表現し、例外は引数不正・モデルロード失敗等の真に例外的な状況に限る
- すべての `async` メソッドに `CancellationToken` を通し、ライブラリコードでは `ConfigureAwait(false)` を使用する
- `async void` を使わない
- ガード句で早期リターンする
- YAGNI: 将来の拡張性のためだけにコードを複雑化しない。過度な抽象化を避ける
- ライブラリはコンソール出力(`Console.WriteLine`)をしない
- パブリッククラスは同名ファイルに 1 クラスずつ記述する

## コードコメント

- why / why not を書く。コードを読めば分かることは書かない
- ドキュメントコメントは簡潔に記述する

## 注意事項

- ONNX モデルファイルはリポジトリに含めない(`models/*.onnx` は gitignore 済み)
- devcontainer は linux/amd64(Apple Silicon では Rosetta エミュレーション)で動作する。本リポジトリが検証対象とする RID は linux-x64 / win-x64 / osx-arm64 の 3 つであり、linux-arm64 は対象外のため
