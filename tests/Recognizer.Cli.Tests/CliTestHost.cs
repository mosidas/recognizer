using OpenCvSharp;

namespace Recognizer.Cli.Tests;

/// <summary>
/// CLI テストの共通基盤。Fixture ONNX のパス解決と、テスト入力(画像・クラス名ファイル等)の
/// 一時ファイル生成を担い、Dispose で生成物を後始末する。
/// </summary>
// Why not: パス組み立てに区切り文字を書かず Path.Combine / Path.GetTempPath に統一する。
// linux-x64 / win-x64 / osx-arm64 の 3 プラットフォームで同じテストを通すため(要件 8.5)。
public sealed class CliTestHost : IDisposable
{
    // Why not: 一時ファイルを個別に追跡しない。ホスト 1 インスタンスにつき 1 ディレクトリへ隔離すれば、
    // 後始末はディレクトリ削除 1 回で済み、テストの並行実行でも名前が衝突しない。
    private readonly string _workDirectory =
        Path.Combine(Path.GetTempPath(), $"recognizer_cli_{Guid.NewGuid():N}");

    private bool _disposed;

    /// <summary>fixture ONNX の出力ディレクトリ上のパスを返す。</summary>
    public static string FixturePath(string fileName)
        => Path.Combine(AppContext.BaseDirectory, "Fixtures", fileName);

    /// <summary>白画像(640x640)の一時 PNG を生成し、そのパスを返す。</summary>
    // face_inputconf_f5.onnx は入力の平均画素値を confidence にするため、白画像は「顔あり」を作る(research §4)。
    public string CreateWhiteImage() => CreateImage(Scalar.All(255));

    /// <summary>黒画像(640x640)の一時 PNG を生成し、そのパスを返す。</summary>
    // 同 fixture において黒画像は confidence 0 = 「顔なし」を作る(research §4)。
    public string CreateBlackImage() => CreateImage(Scalar.All(0));

    /// <summary>画像として読み込めない一時ファイルを生成し、そのパスを返す。</summary>
    // 拡張子を呼び出し側が選べるようにしてある(壊れた ONNX を `.onnx` で作るなど)。
    public string CreateNonImageFile(string extension = ".png")
        => CreateFile(extension, "これは画像でも ONNX でもないテキストです。");

    /// <summary>クラス名ファイル(1 行 1 クラス)の一時ファイルを生成し、そのパスを返す。</summary>
    public string CreateClassNamesFile(params string[] classNames)
        => CreateFile(".txt", string.Join(Environment.NewLine, classNames) + Environment.NewLine);

    /// <summary>この一時ディレクトリ配下の、まだ存在しないパスを返す(ファイル不在の異常系用)。</summary>
    public string NonExistentPath(string extension)
        => Path.Combine(EnsureWorkDirectory(), $"missing_{Guid.NewGuid():N}{extension}");

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        // Why not: 削除失敗をテスト失敗にしない。一時ファイルの残留は検証対象の振る舞いではない。
        try
        {
            if (Directory.Exists(_workDirectory))
            {
                Directory.Delete(_workDirectory, recursive: true);
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private string CreateImage(Scalar color)
    {
        string path = Path.Combine(EnsureWorkDirectory(), $"image_{Guid.NewGuid():N}.png");
        using Mat image = new(640, 640, MatType.CV_8UC3, color);
        _ = Cv2.ImWrite(path, image);

        return path;
    }

    private string CreateFile(string extension, string content)
    {
        string path = Path.Combine(EnsureWorkDirectory(), $"file_{Guid.NewGuid():N}{extension}");
        File.WriteAllText(path, content);

        return path;
    }

    private string EnsureWorkDirectory()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _ = Directory.CreateDirectory(_workDirectory);

        return _workDirectory;
    }
}
