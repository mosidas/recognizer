using OpenCvSharp;

namespace Recognizer.Cli.Tests;

/// <summary>
/// CLI テストの基盤(Fixtures のリンク参照と CliTestHost のヘルパー)が成立していることの検証。
/// </summary>
public sealed class TestInfrastructureTests
{
    // design §9.3 が CLI テストで使うと定めた Fixture。すべて既存の tests/Recognizer.Tests/Fixtures にある。
    public static TheoryData<string> UsedFixtures =>
    [
        "face_nchw_transposed_f5.onnx",
        "face_nchw_transposed_f20.onnx",
        "face_inputconf_f5.onnx",
        "face_unsupported_f7.onnx",
        "object_nchw_transposed_4c3.onnx",
        "object_transposed_coco80.onnx",
        "embed_nchw_meanrgb_d4.onnx",
    ];

    // 要件 8.4: 既存ダミー ONNX がリンク参照で出力ディレクトリへ供給されている
    [Theory]
    [MemberData(nameof(UsedFixtures))]
    public void Fixture_が出力ディレクトリに配置される(string fileName)
    {
        string path = CliTestHost.FixturePath(fileName);

        Assert.True(File.Exists(path), $"Fixture が出力ディレクトリに存在しない: {path}");
    }

    // 要件 8.4: ONNX を本プロジェクト配下へ複製していない(リンク参照で共有する)
    [Fact]
    public void Fixture_をテストプロジェクトに複製していない()
    {
        string projectDirectory = FindProjectDirectory();
        string[] copies =
        [
            .. Directory
                .EnumerateFiles(projectDirectory, "*.onnx", SearchOption.AllDirectories)
                .Where(path => !IsUnderBuildOutput(projectDirectory, path)),
        ];

        Assert.Empty(copies);
    }

    // 要件 8.4: 供給された Fixture が実際にライブラリで動く(白画像 → 検出あり)
    [Fact]
    public async Task Fixture_白画像で顔が検出される()
    {
        using CliTestHost host = new();
        using global::Recognizer.FaceDetector detector = new(CliTestHost.FixturePath("face_inputconf_f5.onnx"));

        IReadOnlyList<global::Recognizer.FaceDetection> faces = await detector.DetectAsync(host.CreateWhiteImage());

        Assert.NotEmpty(faces);
    }

    // 要件 8.4: 同 Fixture が黒画像では未検出になる(検出 0 件・顔未検出ケースを作れる)
    [Fact]
    public async Task Fixture_黒画像では顔が検出されない()
    {
        using CliTestHost host = new();
        using global::Recognizer.FaceDetector detector = new(CliTestHost.FixturePath("face_inputconf_f5.onnx"));

        IReadOnlyList<global::Recognizer.FaceDetection> faces = await detector.DetectAsync(host.CreateBlackImage());

        Assert.Empty(faces);
    }

    // 要件 8.5: 一時ファイルのパスは区切り文字をハードコードせず、実行中のプラットフォームの一時領域に作る
    [Fact]
    public void CliTestHost_一時ファイルをプラットフォーム既定の一時領域に作る()
    {
        using CliTestHost host = new();

        string imagePath = host.CreateWhiteImage();

        Assert.StartsWith(Path.GetTempPath(), imagePath, StringComparison.Ordinal);
        Assert.Equal(imagePath, Path.GetFullPath(imagePath));
    }

    [Fact]
    public void CliTestHost_非画像ファイルは画像として読み込めない()
    {
        using CliTestHost host = new();

        string path = host.CreateNonImageFile();

        Assert.True(File.Exists(path));
        using Mat image = Cv2.ImRead(path, ImreadModes.Color);
        Assert.True(image.Empty());
    }

    [Fact]
    public void CliTestHost_クラス名ファイルを行単位で書き出す()
    {
        using CliTestHost host = new();

        string path = host.CreateClassNamesFile("cat", "dog", "bird");

        Assert.Equal(["cat", "dog", "bird"], File.ReadAllLines(path).Where(line => line.Length > 0));
    }

    [Fact]
    public void CliTestHost_存在しないパスを返す()
    {
        using CliTestHost host = new();

        string path = host.NonExistentPath(".onnx");

        Assert.False(File.Exists(path));
    }

    // 要件 8.5: 生成した一時ファイルは Dispose で後始末される(CI の作業領域を汚さない)
    [Fact]
    public void CliTestHost_Disposeで一時ファイルを削除する()
    {
        string imagePath;
        string classNamesPath;
        using (CliTestHost host = new())
        {
            imagePath = host.CreateBlackImage();
            classNamesPath = host.CreateClassNamesFile("person");
            Assert.True(File.Exists(imagePath));
            Assert.True(File.Exists(classNamesPath));
        }

        Assert.False(File.Exists(imagePath));
        Assert.False(File.Exists(classNamesPath));
    }

    // テストプロジェクトのソースディレクトリ(csproj のある場所)を、出力ディレクトリから遡って探す。
    private static string FindProjectDirectory()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Recognizer.Cli.Tests.csproj")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Recognizer.Cli.Tests.csproj を含むディレクトリが見つからない。");
    }

    // bin / obj 配下はビルド生成物(リンク参照のコピー先)なので複製の判定から除く。
    private static bool IsUnderBuildOutput(string projectDirectory, string path)
    {
        string relative = Path.GetRelativePath(projectDirectory, path);
        string? top = relative.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)[0];

        return top is "bin" or "obj";
    }
}
