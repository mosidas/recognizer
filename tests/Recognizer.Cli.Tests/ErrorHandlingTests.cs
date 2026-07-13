using System.CommandLine;
using System.Globalization;
using Recognizer.Cli.Commands;
using Recognizer.Cli.Errors;
using Recognizer.Cli.Output;

namespace Recognizer.Cli.Tests;

/// <summary>
/// 実行時エラーの例外 → code マッピング(design §8.1)と終了コード(design §8.3)の検証。
/// 要件 7.1(error / code を持つ JSON)・7.3(3 種の終了コード)・7.7(例外種別と発生箇所から code が一意)。
/// </summary>
// Why not: 例外をモック/自作の代用型で作らない。マッピングの目的は「ライブラリが実際に投げる型」を
// 正しい code に振り分けることであり、代用型では型の想定がずれても気づけない(research §3)。
// CliApplication は未実装(タスク 3.4)のため、ここでは Map 単体を実物の例外で検証する。
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

    // CliApplication は未実装(タスク 3.4)のため、コマンドをテスト内で直接組み立てて Parse する(research §7.2 と同形)。
    private static RootCommand BuildConfidenceRoot(UsageErrorCollector collector, out Option<float> confidence)
    {
        confidence = ThresholdOption.Create("--confidence", 0.7f, collector);

        Command detectFace = new("detect-face");
        detectFace.Add(confidence);

        RootCommand root = new();
        root.Add(detectFace);
        return root;
    }

    private static Exception CaptureException(Action action)
    {
        Exception? exception = Record.Exception(action);

        // 実物の例外が出ないなら、マッピングの前提(ライブラリの振る舞い)が崩れている。
        return Assert.IsType<Exception>(exception, exactMatch: false);
    }
}
