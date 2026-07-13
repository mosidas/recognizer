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

    private static Exception CaptureException(Action action)
    {
        Exception? exception = Record.Exception(action);

        // 実物の例外が出ないなら、マッピングの前提(ライブラリの振る舞い)が崩れている。
        return Assert.IsType<Exception>(exception, exactMatch: false);
    }
}
