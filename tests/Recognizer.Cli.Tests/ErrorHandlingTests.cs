using System.CommandLine;
using System.Globalization;
using System.Text.Json;
using Recognizer.Cli.Commands;
using Recognizer.Cli.Errors;
using Recognizer.Cli.Output;

namespace Recognizer.Cli.Tests;

/// <summary>
/// CLI 全体の制御フロー(design §4・§6・§8)の外形検証と、実行時エラーの例外 → code マッピング(design §8.1)・
/// 終了コード(design §8.3)の検証。
/// 要件 7.1(error / code を持つ JSON)・7.2(エラー時は stdout に出さない)・7.3(3 種の終了コード)・
/// 7.7(例外種別と発生箇所から code が一意)。
/// </summary>
// Why not: 例外をモック/自作の代用型で作らない。マッピングの目的は「ライブラリが実際に投げる型」を
// 正しい code に振り分けることであり、代用型では型の想定がずれても気づけない(research §3)。
public sealed class ErrorHandlingTests
{
    private const string ValidFaceModel = "face_nchw_standard_f5.onnx";
    private const string UnsupportedFaceModel = "face_unsupported_f7.onnx";

    // 要件 7.3: 成功 / 実行時エラー / 使用法エラーは互いに異なり、エラーは非 0。
    [Fact]
    public void ExitCodes_成功と2種のエラーが互いに異なる()
    {
        Assert.Equal(0, ExitCodes.Success);
        Assert.NotEqual(ExitCodes.Success, ExitCodes.RuntimeError);
        Assert.NotEqual(ExitCodes.Success, ExitCodes.UsageError);
        Assert.NotEqual(ExitCodes.RuntimeError, ExitCodes.UsageError);
    }

    // 要件 2.7・6.1: --help は Errors 0 件で HelpAction が動き(research §7.2)、終了コード 0 で終わる。
    // 使用法エラーの経路に入らないため、stderr は汚れない。
    [Fact]
    public async Task RunAsync_ヘルプは終了コード0でstderrに何も出力しない()
    {
        (int exitCode, string stdout, string stderr) = await CliTestHost.RunCliAsync("--help");

        Assert.Equal(ExitCodes.Success, exitCode);
        Assert.Empty(stderr);
        Assert.NotEmpty(stdout);
    }

    // design §8.2 順 3: 未知のコマンド。要件 2.5・7.2・7.3。
    [Fact]
    public async Task RunAsync_未知のコマンドはunrecognizedArgumentで終了コード2()
    {
        (int exitCode, string stdout, string stderr) = await CliTestHost.RunCliAsync("nosuch");

        Assert.Equal(ExitCodes.UsageError, exitCode);
        Assert.Empty(stdout);

        ErrorOutput error = ReadErrorJson(stderr);
        Assert.Equal(ErrorCodes.UnrecognizedArgument, error.Code);
        Assert.Contains("nosuch", error.Error, StringComparison.Ordinal);
    }

    // 要件 7.1・7.2: エラー JSON は error / code を持つ 1 行の JSON で stderr にのみ出る。
    // Why not: フレームワーク既定のパースエラー出力(英語メッセージ + ヘルプ)に委ねない。InvokeAsync を呼ばず
    // CLI 自身が JSON を書くことで、stdout を汚さず JSON 契約を保つ(design §8.2・research §7.2)。
    // 未知のコマンド・未知のオプション(いずれも design §8.2 順 3)。出力契約は使用法エラー共通。
    //
    // コマンド未指定(順 7)を RunAsync の外形で覆えるのは、RootCommand にコマンドが 1 つ以上登録されてから
    // (タスク 4.1 以降)。実測: コマンドが 0 個の RootCommand に引数なしで Parse すると Errors は 0 件・
    // Action は null になり(サブコマンドが 1 つでもあれば "Required command was not provided." の
    // ParseError が 1 件立つ)、使用法エラーの経路に入らない。ここに Errors 0 件用の分岐を足すと、
    // コマンド登録後は到達不能な死にコードになるため足さない(design §4 は Errors > 0 のみを経路の条件とする)。
    // 分類そのものは Classify_使用法エラーはdesignの分類表どおりのcodeになる(順 7)が覆っている。
    public static TheoryData<string[]> UsageErrorArgs =>
    [
        ["nosuch"],
        ["--nosuch-option"],
    ];

    [Theory]
    [MemberData(nameof(UsageErrorArgs))]
    public async Task RunAsync_使用法エラーはstdoutを汚さず1行のJSONをstderrに書く(string[] args)
    {
        (int exitCode, string stdout, string stderr) = await CliTestHost.RunCliAsync(args);

        Assert.Equal(ExitCodes.UsageError, exitCode);
        Assert.Empty(stdout);

        // 末尾の改行 1 個は許容し、それ以外に改行を含まない(要件 6.3 と同じ 1 行契約)。
        string trimmed = stderr.TrimEnd('\r', '\n');
        Assert.NotEqual(stderr, trimmed);
        Assert.DoesNotContain('\n', trimmed);

        ErrorOutput error = ReadErrorJson(stderr);
        Assert.NotEmpty(error.Error);
        Assert.NotEmpty(error.Code);
    }

    // design §6 CliApplication の事前条件: args / output / error は非 null。
    [Fact]
    public async Task RunAsync_引数がnullなら事前条件違反()
    {
        using StringWriter writer = new();

        _ = await Assert.ThrowsAsync<ArgumentNullException>(
            () => CliApplication.RunAsync(null!, writer, writer, CancellationToken.None));
        _ = await Assert.ThrowsAsync<ArgumentNullException>(
            () => CliApplication.RunAsync([], null!, writer, CancellationToken.None));
        _ = await Assert.ThrowsAsync<ArgumentNullException>(
            () => CliApplication.RunAsync([], writer, null!, CancellationToken.None));
    }

    // §8.1 順 1: CLI 自身が投げる例外は、保持する code をそのまま使う。
    [Theory]
    [InlineData(ErrorCodes.ClassesFileNotFound)]
    [InlineData(ErrorCodes.ClassesFileReadFailed)]
    public void Map_CliRuntimeExceptionは保持するcodeをそのまま返す(string code)
    {
        CliRuntimeException exception = new("クラス名ファイルが見つかりません: classes.txt", code);

        ErrorOutput output = RuntimeErrorMapper.Map(exception);

        Assert.Equal(code, output.Code);
        Assert.Equal("クラス名ファイルが見つかりません: classes.txt", output.Error);
    }

    // §8.1: CliRuntimeException を IOException 系から派生させると、順 2 の FileNotFoundException 判定に
    // 吸われて modelNotFound と誤判定される。派生関係そのものを契約として固定する。
    [Fact]
    public void CliRuntimeException_IOException系から派生していない()
    {
        CliRuntimeException exception = new("読み込みに失敗しました。", ErrorCodes.ClassesFileReadFailed);

        Assert.IsNotAssignableFrom<IOException>(exception);
        Assert.IsNotAssignableFrom<ArgumentException>(exception);
    }

    // §8.1 順 2: モデルファイル不在(FaceDetector のコンストラクタが FileNotFoundException を投げる)。要件 7.5。
    [Fact]
    public void Map_モデルファイル不在はModelNotFound()
    {
        using CliTestHost host = new();
        string missingModel = host.NonExistentPath(".onnx");

        Exception exception = CaptureException(() => new FaceDetector(missingModel));
        ErrorOutput output = RuntimeErrorMapper.Map(exception);

        Assert.Equal(ErrorCodes.ModelNotFound, output.Code);
        Assert.Contains(missingModel, output.Error, StringComparison.Ordinal);
    }

    // §8.1 順 3: 壊れた ONNX のロード失敗(ライブラリは OnnxRuntimeException を包まず透過する)。要件 7.5。
    [Fact]
    public void Map_壊れたモデルのロード失敗はModelLoadFailed()
    {
        using CliTestHost host = new();
        string brokenModel = host.CreateNonImageFile(".onnx");

        Exception exception = CaptureException(() => new FaceDetector(brokenModel));
        ErrorOutput output = RuntimeErrorMapper.Map(exception);

        Assert.Equal(ErrorCodes.ModelLoadFailed, output.Code);
        Assert.NotEmpty(output.Error);
    }

    // §8.1 順 4: 非対応のモデル形式(ModelIntrospector が NotSupportedException を投げる)。要件 7.5。
    [Fact]
    public void Map_非対応モデル形式はUnsupportedModelFormat()
    {
        Exception exception = CaptureException(
            () => new FaceDetector(CliTestHost.FixturePath(UnsupportedFaceModel)));
        ErrorOutput output = RuntimeErrorMapper.Map(exception);

        Assert.Equal(ErrorCodes.UnsupportedModelFormat, output.Code);
        Assert.NotEmpty(output.Error);
    }

    // §8.1 順 5: 画像のデコード失敗(ImageDecoder が ArgumentException を投げる)。要件 7.4。
    // 閾値の範囲外も同じ ArgumentException だが、CLI が事前検証するためここには到達しない(前提 P1)。
    [Fact]
    public void Map_画像のデコード失敗はImageLoadFailed()
    {
        using CliTestHost host = new();
        using FaceDetector detector = new(CliTestHost.FixturePath(ValidFaceModel));
        string nonImage = host.CreateNonImageFile(".png");

        // DetectAsync はデコードを同期的に行うため、await を待たずに送出される。
        Exception exception = CaptureException(() => _ = detector.DetectAsync(nonImage));
        ErrorOutput output = RuntimeErrorMapper.Map(exception);

        Assert.Equal(ErrorCodes.ImageLoadFailed, output.Code);
        Assert.NotEmpty(output.Error);
    }

    // §8.1 順 5: 画像ファイル不在も ImageDecoder の ArgumentException(ImRead は空 Mat を返す)。要件 7.4。
    [Fact]
    public void Map_画像ファイル不在はImageLoadFailed()
    {
        using CliTestHost host = new();
        using FaceDetector detector = new(CliTestHost.FixturePath(ValidFaceModel));
        string missingImage = host.NonExistentPath(".png");

        Exception exception = CaptureException(() => _ = detector.DetectAsync(missingImage));
        ErrorOutput output = RuntimeErrorMapper.Map(exception);

        Assert.Equal(ErrorCodes.ImageLoadFailed, output.Code);
        Assert.NotEmpty(output.Error);
    }

    // §8.1 順 6: 表に無い実行時例外(破棄済みインスタンスの利用 → ObjectDisposedException)は unexpectedError。
    [Fact]
    public void Map_表にない例外はUnexpectedError()
    {
        FaceDetector detector = new(CliTestHost.FixturePath(ValidFaceModel));
        detector.Dispose();

        using CliTestHost host = new();
        Exception exception = CaptureException(() => _ = detector.DetectAsync(host.CreateWhiteImage()));
        ErrorOutput output = RuntimeErrorMapper.Map(exception);

        Assert.Equal(ErrorCodes.UnexpectedError, output.Code);
        Assert.NotEmpty(output.Error);
    }

    // 要件 7.1: どの実行時エラーでも error は空でない日本語メッセージになる(JSON の error フィールドの契約)。
    [Fact]
    public void Map_未知の例外でもerrorとcodeが必ず埋まる()
    {
        ErrorOutput output = RuntimeErrorMapper.Map(new InvalidOperationException("想定外"));

        Assert.Equal(ErrorCodes.UnexpectedError, output.Code);
        Assert.Contains("想定外", output.Error, StringComparison.Ordinal);
    }

    // code は機械可読な契約(要件 7.7・7.8)であり、値そのものを固定する。
    // 定数同士の突き合わせでは、定数の値を書き換えても検出できない。
    [Fact]
    public void ErrorCodes_値はdesignの語彙と一致する()
    {
        Assert.Equal("modelNotFound", ErrorCodes.ModelNotFound);
        Assert.Equal("modelLoadFailed", ErrorCodes.ModelLoadFailed);
        Assert.Equal("unsupportedModelFormat", ErrorCodes.UnsupportedModelFormat);
        Assert.Equal("imageLoadFailed", ErrorCodes.ImageLoadFailed);
        Assert.Equal("classesFileNotFound", ErrorCodes.ClassesFileNotFound);
        Assert.Equal("classesFileReadFailed", ErrorCodes.ClassesFileReadFailed);
        Assert.Equal("unexpectedError", ErrorCodes.UnexpectedError);
        Assert.Equal("invalidOptionValue", ErrorCodes.InvalidOptionValue);
        Assert.Equal("optionValueOutOfRange", ErrorCodes.OptionValueOutOfRange);
        Assert.Equal("unrecognizedArgument", ErrorCodes.UnrecognizedArgument);
        Assert.Equal("missingRequiredOption", ErrorCodes.MissingRequiredOption);
        Assert.Equal("missingArgument", ErrorCodes.MissingArgument);
        Assert.Equal("missingCommand", ErrorCodes.MissingCommand);
        Assert.Equal("invalidUsage", ErrorCodes.InvalidUsage);
    }

    // 要件 2.6 / design §8.2 順 1: 数値として解釈できない値は invalidOptionValue として収集される。
    // Why not Option.Validators を使わない: バリデータ内で値を取得すると、変換不能な値で Parse() 自体が
    // InvalidOperationException を投げ、CLI が JSON を出す前にクラッシュする(research §7.2)。
    // このテストは「Parse() が例外を投げない」ことを明示的に固定し、Validators への退行を検出する。
    [Fact]
    public void Threshold_数値として解釈できない値はInvalidOptionValue()
    {
        UsageErrorCollector collector = new();
        RootCommand root = BuildConfidenceRoot(collector, out _);

        ParseResult? parseResult = null;
        Exception? exception = Record.Exception(
            () => parseResult = root.Parse(["detect-face", "--confidence", "abc"]));

        Assert.Null(exception);
        Assert.Single(parseResult!.Errors);
        ErrorOutput recorded = Assert.Single(collector.Errors);
        Assert.Equal(ErrorCodes.InvalidOptionValue, recorded.Code);
        Assert.Contains("abc", recorded.Error, StringComparison.Ordinal);
        Assert.Contains("--confidence", recorded.Error, StringComparison.Ordinal);
    }

    // 要件 2.6 / design §8.2 順 2: 0.0〜1.0 の範囲外は optionValueOutOfRange。
    // NaN / Infinity は float.TryParse が解析に成功する(research §8)。値域判定を v < 0f || v > 1f と書くと
    // NaN はあらゆる比較が false になるため素通りし、ライブラリまで到達してしまう(要件 2.6 違反)。
    [Theory]
    [InlineData("1.5")]
    [InlineData("-0.1")]
    [InlineData("NaN")]
    [InlineData("Infinity")]
    [InlineData("-Infinity")]
    public void Threshold_値域外はOptionValueOutOfRange(string value)
    {
        UsageErrorCollector collector = new();
        RootCommand root = BuildConfidenceRoot(collector, out _);

        ParseResult parseResult = root.Parse(["detect-face", "--confidence", value]);

        Assert.Single(parseResult.Errors);
        ErrorOutput recorded = Assert.Single(collector.Errors);
        Assert.Equal(ErrorCodes.OptionValueOutOfRange, recorded.Code);
        Assert.Contains(value, recorded.Error, StringComparison.Ordinal);
    }

    // design §8.2 順 5(実測): 値なしのオプションでは CustomParser が呼ばれない。この分岐の分類は
    // UsageErrorClassifier(タスク 3.3)の担当であり、ここでは collector が空のままであることだけを固定する。
    [Fact]
    public void Threshold_値なしのオプションはCustomParserを呼ばない()
    {
        UsageErrorCollector collector = new();
        RootCommand root = BuildConfidenceRoot(collector, out _);

        ParseResult parseResult = root.Parse(["detect-face", "--confidence"]);

        Assert.Single(parseResult.Errors);
        Assert.Empty(collector.Errors);
    }

    // 要件 2.6: 値域内の閾値はそのままパースされ、使用法エラーにならない。
    [Fact]
    public void Threshold_正常値はパースされる()
    {
        UsageErrorCollector collector = new();
        RootCommand root = BuildConfidenceRoot(collector, out Option<float> confidence);

        ParseResult parseResult = root.Parse(["detect-face", "--confidence", "0.25"]);

        Assert.Empty(parseResult.Errors);
        Assert.Empty(collector.Errors);
        Assert.Equal(0.25f, parseResult.GetValue(confidence));
    }

    // 要件 2.3: オプションを省略すると既定値が使われる(CustomParser は呼ばれない)。
    [Fact]
    public void Threshold_省略時は既定値が使われる()
    {
        UsageErrorCollector collector = new();
        RootCommand root = BuildConfidenceRoot(collector, out Option<float> confidence);

        ParseResult parseResult = root.Parse(["detect-face"]);

        Assert.Empty(parseResult.Errors);
        Assert.Empty(collector.Errors);
        Assert.Equal(0.7f, parseResult.GetValue(confidence));
    }

    // design §6 ThresholdOption の事前条件: 既定値自身が 0.0〜1.0 に収まっていること。
    [Theory]
    [InlineData(1.5f)]
    [InlineData(-0.1f)]
    [InlineData(float.NaN)]
    public void Threshold_値域外の既定値は事前条件違反(float defaultValue)
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => ThresholdOption.Create("--confidence", defaultValue, new UsageErrorCollector()));
    }

    // design §6 CliApplication: collector は実行単位ごとに新規生成する(可変状態を実行間で共有しない)。
    [Fact]
    public void UsageErrorCollector_インスタンスごとに記録が独立する()
    {
        UsageErrorCollector first = new();
        UsageErrorCollector second = new();

        _ = BuildConfidenceRoot(first, out _).Parse(["detect-face", "--confidence", "abc"]);

        Assert.Single(first.Errors);
        Assert.Empty(second.Errors);
    }

    // 小数点がカンマのカルチャでも "0.25" を 0.25 と解釈する(InvariantCulture の固定。design §6)。
    // Why not: CurrentCulture で TryParse すると de-DE では "0.25" が解釈不能に転び、正当な入力が
    // 使用法エラーになる。実行環境が invariant のため、カルチャを差し替えないとこの退行は観測できない。
    [Fact]
    public void Threshold_小数点がカンマのカルチャでも不変形式で解釈する()
    {
        CultureInfo original = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("de-DE");

            UsageErrorCollector collector = new();
            RootCommand root = BuildConfidenceRoot(collector, out Option<float> confidence);

            ParseResult parseResult = root.Parse(["detect-face", "--confidence", "0.25"]);

            Assert.Empty(parseResult.Errors);
            Assert.Empty(collector.Errors);
            Assert.Equal(0.25f, parseResult.GetValue(confidence));
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }
    }

    // design §8.2 の 8 行を上から順に 1 ケース以上で覆う(要件 2.4・2.5・7.8: code が一意に決まること)。
    [Theory]
    // 順 1: 解釈不能な値(collector の記録が最優先)
    [InlineData(ErrorCodes.InvalidOptionValue, new[] { "detect-face", "a.jpg", "--model", "m.onnx", "--confidence", "abc" })]
    // 順 2: 値域外
    [InlineData(ErrorCodes.OptionValueOutOfRange, new[] { "detect-face", "a.jpg", "--model", "m.onnx", "--confidence", "1.5" })]
    // 順 3: 位置引数の過剰 / 未知オプション / 未知コマンド(いずれも UnmatchedTokens に載る)
    [InlineData(ErrorCodes.UnrecognizedArgument, new[] { "detect-face", "a.jpg", "b.jpg", "--model", "m.onnx" })]
    [InlineData(ErrorCodes.UnrecognizedArgument, new[] { "detect-face", "a.jpg", "--model", "m.onnx", "--nosuch" })]
    [InlineData(ErrorCodes.UnrecognizedArgument, new[] { "nosuch" })]
    // 順 4: 必須オプションの欠落と、必須オプションを値なしで書いた場合
    [InlineData(ErrorCodes.MissingRequiredOption, new[] { "detect-face", "a.jpg" })]
    [InlineData(ErrorCodes.MissingRequiredOption, new[] { "detect-face", "a.jpg", "--model" })]
    // 順 5: 必須でないオプションの値欠落(CustomParser は呼ばれない = collector は空)
    [InlineData(ErrorCodes.InvalidOptionValue, new[] { "detect-face", "a.jpg", "--model", "m.onnx", "--confidence" })]
    // 順 6: 位置引数の不足
    [InlineData(ErrorCodes.MissingArgument, new[] { "detect-face", "--model", "m.onnx" })]
    // 順 7: コマンド未指定
    [InlineData(ErrorCodes.MissingCommand, new string[0])]
    // 順 8: 上記のいずれにも当たらない使用法エラー(同一オプションの重複指定)
    [InlineData(ErrorCodes.InvalidUsage, new[] { "detect-face", "a.jpg", "--model", "m.onnx", "--confidence", "0.5", "--confidence", "abc" })]
    public void Classify_使用法エラーはdesignの分類表どおりのcodeになる(string expectedCode, string[] args)
    {
        UsageErrorCollector collector = new();
        RootCommand root = BuildFullRoot(collector);

        ParseResult parseResult = root.Parse(args);
        Assert.NotEmpty(parseResult.Errors);

        ErrorOutput output = UsageErrorClassifier.Classify(parseResult, collector);

        Assert.Equal(expectedCode, output.Code);
        Assert.NotEmpty(output.Error);
    }

    // design §8.2 順 4 の回帰テスト: 述語から Option.Required を落とすと、必須でない --confidence の値欠落が
    // missingRequiredOption に誤分類される(設計レビューで検出された欠陥)。構造が必須オプションの欠落と
    // 同一(OptionResult + トークン 0 件)であるため、Required で区別しなければ区別がつかない。
    [Theory]
    [InlineData("--confidence")]  // 値なし指定
    [InlineData("--confidence=")] // 等号の後が空(実測でも CustomParser は呼ばれず、トークン 0 件になる)
    public void Classify_必須でないオプションの値欠落はMissingRequiredOptionにならない(string token)
    {
        UsageErrorCollector collector = new();
        RootCommand root = BuildFullRoot(collector);

        ParseResult parseResult = root.Parse(["detect-face", "a.jpg", "--model", "m.onnx", token]);

        Assert.Empty(collector.Errors);
        ErrorOutput output = UsageErrorClassifier.Classify(parseResult, collector);

        Assert.NotEqual(ErrorCodes.MissingRequiredOption, output.Code);
        Assert.Equal(ErrorCodes.InvalidOptionValue, output.Code);
        Assert.Contains("--confidence", output.Error, StringComparison.Ordinal);
    }

    // 要件 7.8: 複数種別が同時に起きても code は一意に決まる(先に一致した行を採用する)。
    [Theory]
    // 必須オプション欠落(順 4)+ 位置引数不足(順 6)→ 順 4
    [InlineData(ErrorCodes.MissingRequiredOption, new[] { "detect-face" })]
    // 未知コマンドは UnmatchedTokens(順 3)と RootCommand(順 7)の両方に一致する → 順 3
    [InlineData(ErrorCodes.UnrecognizedArgument, new[] { "nosuch", "a.jpg" })]
    // 値の解釈不能(順 1)+ 未知オプション(順 3)→ 順 1
    [InlineData(ErrorCodes.InvalidOptionValue, new[] { "detect-face", "a.jpg", "--model", "m.onnx", "--confidence", "abc", "--nosuch" })]
    public void Classify_複数種別が同時に起きてもcodeは一意に決まる(string expectedCode, string[] args)
    {
        UsageErrorCollector collector = new();
        RootCommand root = BuildFullRoot(collector);

        ParseResult parseResult = root.Parse(args);

        Assert.Equal(expectedCode, UsageErrorClassifier.Classify(parseResult, collector).Code);
    }

    // design §8.2 順 1 が順 2 より先: 解釈不能と値域外が同時に記録された場合は invalidOptionValue。
    [Fact]
    public void Classify_解釈不能と値域外が同時なら解釈不能を採用する()
    {
        UsageErrorCollector collector = new();
        RootCommand root = BuildFullRoot(collector);

        // --nms が値域外(順 2)、--confidence が解釈不能(順 1)。記録順は値域外が先。
        ParseResult parseResult = root.Parse(
            ["detect-face", "a.jpg", "--model", "m.onnx", "--nms", "1.5", "--confidence", "abc"]);

        Assert.Equal(2, collector.Errors.Count);
        ErrorOutput output = UsageErrorClassifier.Classify(parseResult, collector);

        Assert.Equal(ErrorCodes.InvalidOptionValue, output.Code);
        Assert.Contains("abc", output.Error, StringComparison.Ordinal);
    }

    // 順 1・2 のメッセージは CustomParser が作った日本語(指定値を含む)をそのまま使う(重複生成をしない)。
    [Fact]
    public void Classify_値エラーのメッセージはcollectorの記録をそのまま返す()
    {
        UsageErrorCollector collector = new();
        RootCommand root = BuildFullRoot(collector);

        ParseResult parseResult = root.Parse(["detect-face", "a.jpg", "--model", "m.onnx", "--confidence", "1.5"]);

        ErrorOutput recorded = Assert.Single(collector.Errors);
        Assert.Equal(recorded, UsageErrorClassifier.Classify(parseResult, collector));
    }

    // 要件 7.1: どの使用法エラーでも、日本語メッセージが当該のオプション名 / 引数名 / コマンド名を含む。
    // Why not フレームワークの英語メッセージ(ParseError.Message)を使わない: CLAUDE.md の日本語規約に反し、
    // 文字列一致で分類すると System.CommandLine の文言変更で壊れる(design §8.2)。
    [Theory]
    [InlineData(new[] { "detect-face", "a.jpg" }, "--model")]
    [InlineData(new[] { "detect-face", "--model", "m.onnx" }, "image")]
    [InlineData(new[] { "detect-face", "a.jpg", "b.jpg", "--model", "m.onnx" }, "b.jpg")]
    [InlineData(new string[0], "detect-face")]
    public void Classify_メッセージは対象の識別子を含む日本語になる(string[] args, string expectedFragment)
    {
        UsageErrorCollector collector = new();
        RootCommand root = BuildFullRoot(collector);

        ErrorOutput output = UsageErrorClassifier.Classify(root.Parse(args), collector);

        Assert.Contains(expectedFragment, output.Error, StringComparison.Ordinal);
        Assert.DoesNotContain("Required", output.Error, StringComparison.Ordinal);
        Assert.DoesNotContain("Unrecognized", output.Error, StringComparison.Ordinal);
    }

    // Why not: CliApplication の RootCommand を使わない。3 コマンドの登録はタスク 4.1 以降であり、
    // 分類の全 8 行を試すには位置引数・必須オプション・任意オプション・複数サブコマンドが揃った木が要る
    // (research §7.2 と同形の木をテスト内で組み立てる)。コマンド登録後は §8.2 の各行が実コマンドでも成立する。
    private static RootCommand BuildFullRoot(UsageErrorCollector collector)
    {
        Command detectFace = new("detect-face")
        {
            new Argument<string>("image"),
            new Option<string>("--model") { Required = true },
            ThresholdOption.Create("--confidence", 0.7f, collector),
            ThresholdOption.Create("--nms", 0.5f, collector),
        };

        // compare-face は「コマンド未指定」の案内メッセージが複数コマンドを列挙することの検証にのみ使う。
        return new RootCommand { detectFace, new Command("compare-face") };
    }

    // ThresholdOption 単体の検証用。閾値オプションだけを持つ最小の木で足りる(同上の理由で実コマンドを使わない)。
    private static RootCommand BuildConfidenceRoot(UsageErrorCollector collector, out Option<float> confidence)
    {
        confidence = ThresholdOption.Create("--confidence", 0.7f, collector);

        Command detectFace = new("detect-face");
        detectFace.Add(confidence);

        RootCommand root = new();
        root.Add(detectFace);
        return root;
    }

    // stderr に書かれた 1 行 JSON を読む。
    // Why not: 逆シリアライズ(CliJson 経由)に頼らない。キー名 error / code は機械可読な契約(要件 7.1)であり、
    // シリアライズ設定を共有すると命名の退行(PascalCase 化など)をテストが素通しする。
    private static ErrorOutput ReadErrorJson(string stderr)
    {
        using JsonDocument document = JsonDocument.Parse(stderr);
        JsonElement root = document.RootElement;

        return new ErrorOutput(
            root.GetProperty("error").GetString()!,
            root.GetProperty("code").GetString()!);
    }

    private static Exception CaptureException(Action action)
    {
        Exception? exception = Record.Exception(action);

        // 実物の例外が出ないなら、マッピングの前提(ライブラリの振る舞い)が崩れている。
        return Assert.IsType<Exception>(exception, exactMatch: false);
    }
}
