# recognizer

YOLO 形式の ONNX モデルで動作する顔検出・顔認証・物体検出のコアライブラリ(C# / .NET 10)。

## 機能

- 顔検出: YOLO-face 系モデル(YOLOv8n-face / YOLOv11-face 等)による顔のバウンディングボックスとランドマークの検出
- 顔認証: 顔埋め込みモデル(ArcFace 等)による 1:1 照合と埋め込み抽出
- 物体検出: YOLOv5/v8/v11 系モデルによる物体検出

モデルの入出力形式(テンソルレイアウト・出力形状)は ONNX メタデータと出力形状から自動判別する。

## 対応プラットフォーム

| プラットフォーム | RID |
| --- | --- |
| Linux (x64) | `linux-x64` |
| Windows (x64) | `win-x64` |
| macOS (Apple Silicon) | `osx-arm64` |

ネイティブ資産は RID 別のランタイムパッケージで解決するため、利用側で RID を指定しない `dotnet build` でもホストの RID 向けに動作する。依存パッケージと RID の対応は `docs/api-spec.md` §2 を参照。

## API 仕様

`docs/api-spec.md` を参照。

## CLI

ライブラリを端末から呼び出す実行ファイル(`src/Recognizer.Cli`)。結果は機械可読な JSON(camelCase・整形なしの 1 行)を stdout に、エラーは JSON を stderr に出力する。人間向けの整形出力・検出結果の描画は行わない。

リポジトリから直接実行する場合は `dotnet run --project src/Recognizer.Cli -- <コマンド> ...`、配布された実行ファイルを使う場合は `recognizer <コマンド> ...`(以下の例はこの形式)。

### コマンドとオプション

| コマンド | 位置引数 | 必須オプション | 任意オプション(既定値) |
| --- | --- | --- | --- |
| `detect-face` | `<image>` | `--model` | `--confidence`(0.7)/ `--nms`(0.5) |
| `detect-object` | `<image>` | `--model` | `--classes`(なし)/ `--confidence`(0.5)/ `--nms`(0.5) |
| `compare-face` | `<image1> <image2>` | `--detector-model` / `--embedding-model` | `--detection-threshold`(0.7)/ `--nms`(0.5) |

- `--confidence` の既定値はコマンドで異なる(`detect-face` は 0.7、`detect-object` は 0.5)。
- 閾値オプション(`--confidence` / `--nms` / `--detection-threshold`)は 0.0 以上 1.0 以下の実数。範囲外や数値でない値は使用法エラー(終了コード 2)となり、モデルはロードされない。
- `--classes` は 1 行 1 クラス名のテキストファイル。省略時はライブラリの既定解決に委ねる(モデル出力が 80 クラスなら COCO 80 クラス名、それ以外は `class_{id}`)。行数がモデルのクラス数と一致しなくてもエラーにはしない。
- `--help` でコマンド・オプションの一覧を表示する(`recognizer --help` / `recognizer detect-face --help`)。

### detect-face

画像から顔を検出し、bbox・信頼度・ランドマークを出力する。

```console
$ recognizer detect-face photo.jpg --model models/yolov8n-face.onnx
{"image":"photo.jpg","faces":[{"bbox":{"x":75,"y":75,"width":50,"height":50},"confidence":0.95,"landmarks":{"leftEye":{"x":90,"y":92.5},"rightEye":{"x":110,"y":92.5},"nose":{"x":100,"y":100},"leftMouth":{"x":90,"y":110},"rightMouth":{"x":110,"y":110}}},{"bbox":{"x":270,"y":270,"width":60,"height":60},"confidence":0.85,"landmarks":{"leftEye":{"x":288,"y":291},"rightEye":{"x":312,"y":291},"nose":{"x":300,"y":300},"leftMouth":{"x":288,"y":312},"rightMouth":{"x":312,"y":312}}},{"bbox":{"x":160,"y":460,"width":80,"height":80},"confidence":0.75,"landmarks":{"leftEye":{"x":184,"y":488},"rightEye":{"x":216,"y":488},"nose":{"x":200,"y":500},"leftMouth":{"x":184,"y":516},"rightMouth":{"x":216,"y":516}}}]}
```

`faces` は信頼度の降順で、各要素は次の構造を持つ。ランドマークを出力しないモデルでは `landmarks` は `null` になる。

```json
{
  "bbox": { "x": 75, "y": 75, "width": 50, "height": 50 },
  "confidence": 0.95,
  "landmarks": {
    "leftEye": { "x": 90, "y": 92.5 },
    "rightEye": { "x": 110, "y": 92.5 },
    "nose": { "x": 100, "y": 100 },
    "leftMouth": { "x": 90, "y": 110 },
    "rightMouth": { "x": 110, "y": 110 }
  }
}
```

顔が 1 件も検出されなくても失敗ではない(空配列・終了コード 0)。

```console
$ recognizer detect-face noface.jpg --model models/yolov8n-face.onnx
{"image":"noface.jpg","faces":[]}
```

### detect-object

画像から物体を検出し、クラス・信頼度・bbox を出力する。`objects` は信頼度の降順。

```console
$ recognizer detect-object photo.jpg --model models/yolov8n.onnx
{"image":"photo.jpg","objects":[{"classId":0,"className":"person","confidence":0.95,"bbox":{"x":75,"y":75,"width":50,"height":50}},{"classId":2,"className":"car","confidence":0.88,"bbox":{"x":260,"y":260,"width":80,"height":80}},{"classId":15,"className":"cat","confidence":0.75,"bbox":{"x":470,"y":170,"width":60,"height":60}}]}
```

独自クラスのモデルでは `--classes` にクラス名ファイル(1 行 1 クラス名)を渡す。

```console
$ recognizer detect-object photo.jpg --model models/custom.onnx --classes classes.txt
{"image":"photo.jpg","objects":[{"classId":0,"className":"dog","confidence":0.9,"bbox":{"x":75,"y":75,"width":50,"height":50}},{"classId":1,"className":"cat","confidence":0.85,"bbox":{"x":75,"y":75,"width":50,"height":50}},{"classId":2,"className":"bird","confidence":0.7,"bbox":{"x":370,"y":370,"width":60,"height":60}}]}
```

検出 0 件も失敗ではない(`{"image":"photo.jpg","objects":[]}`・終了コード 0)。

### compare-face

2 枚の画像から顔を 1 つずつ検出し、埋め込みのコサイン類似度を出力する。**同一人物か否かの判定は行わない**(`similarity` に対する閾値判定は利用者の責務)。

```console
$ recognizer compare-face face1.jpg face2.jpg --detector-model models/yolov8n-face.onnx --embedding-model models/arcface.onnx
{"image1":"face1.jpg","image2":"face2.jpg","status":"Success","similarity":0.8954585,"face1":{"bbox":{"x":220,"y":220,"width":200,"height":200},"confidence":1},"face2":{"bbox":{"x":220,"y":220,"width":200,"height":200},"confidence":0.70521253}}
```

`status` は `Success` / `NoFaceInImage1` / `NoFaceInImage2` のいずれか。顔が見つからなくても失敗ではない(終了コード 0)。

| `status` | `similarity` | `face1` | `face2` |
| --- | --- | --- | --- |
| `Success` | コサイン類似度 | 使用した顔 | 使用した顔 |
| `NoFaceInImage1` | 0 | `null` | `null`(画像 1 で未検出の時点で画像 2 を評価しないため) |
| `NoFaceInImage2` | 0 | 使用した顔 | `null` |

```console
$ recognizer compare-face noface.jpg face2.jpg --detector-model models/yolov8n-face.onnx --embedding-model models/arcface.onnx
{"image1":"noface.jpg","image2":"face2.jpg","status":"NoFaceInImage1","similarity":0,"face1":null,"face2":null}
```

### エラーと終了コード

エラー時は `error`(人間可読なメッセージ)と `code`(機械可読な識別子)を持つ JSON を **stderr** に出力し、stdout には何も出力しない。

```console
$ recognizer detect-face photo.jpg --model models/missing.onnx
{"error":"モデルファイルが見つかりません: models/missing.onnx","code":"modelNotFound"}
$ echo $?
1

$ recognizer detect-face photo.jpg --model models/yolov8n-face.onnx --confidence 1.5
{"error":"--confidence は 0.0 以上 1.0 以下で指定してください(指定値: 1.5)。","code":"optionValueOutOfRange"}
$ echo $?
2
```

終了コードは 3 種。

| 終了コード | 意味 |
| --- | --- |
| 0 | 成功。**検出 0 件・顔未検出を含む**(これらは失敗ではない)。`--help` も 0 |
| 1 | 実行時エラー(引数は正しいが、画像・モデル・クラス名ファイルの読み込みまたは解釈に失敗) |
| 2 | 使用法エラー(引数の書式・組み合わせ・値域が不正で、コマンドを開始できない) |

`code` の一覧。

| `code` | 終了コード | 発生条件 |
| --- | --- | --- |
| `modelNotFound` | 1 | モデルファイルが存在しない |
| `modelLoadFailed` | 1 | モデルのロードに失敗した(ONNX として壊れている等) |
| `unsupportedModelFormat` | 1 | モデルの入出力形式が非対応 |
| `imageLoadFailed` | 1 | 画像が存在しない、または画像としてデコードできない |
| `classesFileNotFound` | 1 | `--classes` のファイルが存在しない |
| `classesFileReadFailed` | 1 | `--classes` のファイルを読み込めない(権限・I/O エラー) |
| `unexpectedError` | 1 | 上記以外の実行時エラー |
| `invalidOptionValue` | 2 | オプションの値を解釈できない(`--confidence abc`、値の欠落) |
| `optionValueOutOfRange` | 2 | 閾値が 0.0〜1.0 の範囲外 |
| `unrecognizedArgument` | 2 | 未知のコマンド・未知のオプション |
| `missingRequiredOption` | 2 | 必須オプションが指定されていない |
| `missingArgument` | 2 | 位置引数が不足している |
| `missingCommand` | 2 | コマンドが指定されていない |
| `invalidUsage` | 2 | 上記以外の使用法エラー |

### スクリプトからの利用

stdout は JSON のみのため、`jq` でそのままパースできる。検出 0 件は終了コード 0 で空配列が返るため、件数は終了コードではなく JSON で判定する。

```bash
# 検出した顔の件数
recognizer detect-face photo.jpg --model models/yolov8n-face.onnx | jq '.faces | length'

# 信頼度 0.9 以上の物体のクラス名
recognizer detect-object photo.jpg --model models/yolov8n.onnx \
  | jq -r '.objects[] | select(.confidence >= 0.9) | .className'

# エラー種別で分岐する(エラー JSON は stderr)
if out=$(recognizer detect-face photo.jpg --model models/yolov8n-face.onnx 2>/tmp/err.json); then
  echo "$out" | jq '.faces'
else
  case "$(jq -r .code /tmp/err.json)" in
    modelNotFound) echo "モデルを配置してください" ;;
    imageLoadFailed) echo "画像を読み込めません" ;;
    *) jq -r .error /tmp/err.json ;;
  esac
fi
```

### 配布(単一実行ファイル)

RID を指定して publish すると、.NET ランタイムを同梱した自己完結の単一実行ファイル `recognizer`(Windows は `recognizer.exe`)が生成される。実行端末に .NET ランタイムを導入する必要はない。

```bash
# linux-x64(win-x64 / osx-arm64 も同様に -r で指定する)
dotnet publish src/Recognizer.Cli/Recognizer.Cli.csproj -c Release -r linux-x64 -o publish-cli
```

| RID | 生成物 |
| --- | --- |
| `linux-x64` | `publish-cli/recognizer` |
| `win-x64` | `publish-cli/recognizer.exe` |
| `osx-arm64` | `publish-cli/recognizer` |

ONNX Runtime / OpenCV のネイティブ資産も実行ファイルに含まれる。実行に必要なのは上表の実行ファイル 1 つだけで、同じディレクトリに出る `.pdb`(デバッグシンボル)や `.lib`(win-x64。リンク用のインポートライブラリ)は配布に含めなくてよい。ONNX モデルは同梱されないため、別途配置する(後述の「モデルファイル」)。

## リポジトリ構成

- `src/Recognizer` — 公開 API を提供する net10.0 クラスライブラリ(内部実装は `Internal/` 配下で `internal`)
- `src/Recognizer.Cli` — ライブラリを端末から呼び出す CLI 実行プロジェクト(単一実行ファイル `recognizer`)
- `tests/Recognizer.Tests` — xUnit テストプロジェクト
- `tests/Recognizer.Cli.Tests` — CLI の xUnit テストプロジェクト
- `tests/Recognizer.Tests/Fixtures` — テスト用のダミー ONNX モデル(生成物をコミット。詳細は同ディレクトリの `README.md`)
- `tools/generate_test_models.py` — fixture 再生成スクリプト(再生成時のみ使用)
- `models/` — 実 ONNX モデルの置き場(`.gitignore` 済みで未追跡)

## 開発

devcontainer で開発する。ビルドとテストはいずれも終了コード 0 で完了する。

```bash
# ビルド
dotnet build

# テスト
dotnet test
```

テストは `Fixtures/` にコミット済みのダミー ONNX モデルで自己完結するため、実行に Python は不要である。

### CI での検証

`.github/workflows/ci.yml` が GitHub Actions で `main` への push と pull request 時に走る。対応プラットフォーム 3 種のマトリクス(`ubuntu-latest` = linux-x64 / `windows-latest` = win-x64 / `macos-15` = osx-arm64)で、各ランナー上の実機で次を実行する。

1. `dotnet build --configuration Release`
2. `dotnet test --configuration Release --no-build`
3. RID を指定した `dotnet publish`(RID 別ネイティブ資産が解決されることの確認)

`fail-fast: false` のため、1 つのプラットフォームが失敗しても他は完走し、全プラットフォームの結果が得られる。devcontainer は linux/amd64 のため Windows / macOS の実機確認はローカルでは行えず、CI が唯一の検証手段である。

### テスト用 fixture の再生成

`Fixtures/*.onnx` を作り直す場合のみ、Python(onnx パッケージ)で再生成する。生成物はコミットされているため、通常の開発では不要である。

```bash
python3 -m venv /tmp/onnx-venv   # venv の場所は任意
/tmp/onnx-venv/bin/pip install onnx
/tmp/onnx-venv/bin/python tools/generate_test_models.py
```

生成される fixture の一覧・期待値・生成ロジックは `tests/Recognizer.Tests/Fixtures/README.md` と `tools/generate_test_models.py` を参照。

## モデルファイル

ONNX モデルはリポジトリに含めない。`models/` に別途配置する。

## ライセンス

MIT
