using System.Xml.Linq;

namespace Recognizer.Tests;

/// <summary>
/// ライブラリ構成(非機能要件 face-detection 5.1〜5.4 / object-detection 6.1〜6.5)の契約テスト。
/// FaceDetector 個別のインスタンス契約ではなく、アセンブリ/プロジェクトレベルの
/// 横断的な契約(公開型集合・依存パッケージ・コンソール不使用)を検証するため独立ファイルに置く。
/// </summary>
public sealed class PublicApiTests
{
    // 公開してよい型(face-detection 要件 5.1 / object-detection 要件 6.1 / face-recognition 要件 7.1)。名前空間込みで厳密比較する。
    private static readonly string[] AllowedExportedTypeNames =
    [
        "Recognizer.FaceDetector",
        "Recognizer.FaceDetection",
        "Recognizer.FaceLandmarks",
        "Recognizer.ObjectDetector",
        "Recognizer.ObjectDetection",
        // face-recognition 7.1: 顔認証 unit で追加する公開型。
        "Recognizer.FaceRecognizer",
        "Recognizer.FaceComparisonStatus",
        "Recognizer.FaceComparisonResult",
        "Recognizer.FaceEmbeddingResult",
    ];

    // 依存を許可するパッケージ(要件 5.4)。OS 向け runtime パッケージを含む。
    private static readonly string[] AllowedPackageReferences =
    [
        "Microsoft.ML.OnnxRuntime",
        "OpenCvSharp4",
        "OpenCvSharp4.official.runtime.linux-x64",
    ];

    // face-detection 5.1 / object-detection 6.1 / face-recognition 7.1: 公開型は Recognizer 名前空間の 9 型に厳密に限定される。
    [Fact]
    public void ExportedTypes_公開型は許可された9型のみ()
    {
        Type[] exported = typeof(FaceDetector).Assembly.GetExportedTypes();

        string[] actual = [.. exported.Select(t => t.FullName!).OrderBy(n => n, StringComparer.Ordinal)];
        string[] expected = [.. AllowedExportedTypeNames.OrderBy(n => n, StringComparer.Ordinal)];

        Assert.Equal(expected, actual);
    }

    // 5.1: すべての公開型は名前空間 Recognizer に属する(Recognizer.Internal 等の混入がない)。
    [Fact]
    public void ExportedTypes_名前空間はRecognizerのみ()
    {
        Type[] exported = typeof(FaceDetector).Assembly.GetExportedTypes();

        Assert.All(exported, t => Assert.Equal("Recognizer", t.Namespace));
    }

    // 5.2 / object-detection 6.2: 内部実装(前処理・テンソル変換・NMS・出力パース・クラス名解決等)は公開されない。
    // 5.1 / 6.1 の集合比較で担保されるが、internal 化の意図を型名の観点から明示する。
    [Fact]
    public void ExportedTypes_内部実装型は非公開()
    {
        string[] internalTypeSimpleNames =
        [
            "NonMaxSuppression",
            "Letterbox",
            "LetterboxParams",
            "Preprocessor",
            "FaceOutputParser",
            "ModelIntrospector",
            "ImageDecoder",
            "DetectionModelSpec",
            "TensorLayout",
            // object-detection 6.2: 物体検出の出力パース・クラス名解決の内部実装。
            "ObjectOutputParser",
            "ObjectCandidate",
            "CocoClassNames",
            "ObjectOutputSpec",
            // face-recognition 7.3: 顔切り出し・埋め込み前処理・埋め込みモデル仕様は内部実装として非公開。
            "FaceCropper",
            "EmbeddingPreprocessor",
            "EmbeddingModelSpec",
        ];

        Type[] exported = typeof(FaceDetector).Assembly.GetExportedTypes();
        HashSet<string> exportedSimpleNames = [.. exported.Select(t => t.Name)];

        foreach (string name in internalTypeSimpleNames)
        {
            Assert.DoesNotContain(name, exportedSimpleNames);
        }
    }

    // 5.3: ライブラリはコンソール出力をしない。
    // 手段: リポジトリルート(Recognizer.sln)を上方向に探索して src/Recognizer のソースを走査する。
    // なぜソース走査か: Console.Write 等の呼び出しは IL 上 System.Console への MemberRef として残るが、
    // 未呼び出しの型参照(using 等)と区別しづらく、リフレクションでは「実際に出力するか」を判定できない。
    // ソースの "Console." 出現有無なら禁止事項(コンソール出力の記述そのもの)を直接検証でき、
    // tasks.md の grep 検証コマンドと同一のセマンティクスで CI・他環境でも安定する。
    // なぜルート探索起点か: テスト実行ディレクトリ(bin/...)からの相対パス依存を避けるため。
    [Fact]
    public void Source_Console出力を含まない()
    {
        string sourceDir = Path.Combine(FindRepositoryRoot(), "src", "Recognizer");

        string[] offending =
        [
            .. EnumerateSourceFiles(sourceDir)
                .Where(path => File.ReadAllText(path).Contains("Console.", StringComparison.Ordinal))
        ];

        Assert.Empty(offending);
    }

    // 5.4: 依存パッケージは許可された 3 パッケージ(OnnxRuntime / OpenCvSharp4 / OS runtime)のみ。
    // csproj を XML として読み、リポジトリルート起点でパスを解決してパス依存を排除する。
    [Fact]
    public void Csproj_依存パッケージは許可された3つのみ()
    {
        string csprojPath = Path.Combine(FindRepositoryRoot(), "src", "Recognizer", "Recognizer.csproj");
        XDocument doc = XDocument.Load(csprojPath);

        string[] actual =
        [
            .. doc.Descendants("PackageReference")
                .Select(e => e.Attribute("Include")?.Value ?? string.Empty)
                .OrderBy(n => n, StringComparer.Ordinal)
        ];
        string[] expected = [.. AllowedPackageReferences.OrderBy(n => n, StringComparer.Ordinal)];

        Assert.Equal(expected, actual);
    }

    // .git や Recognizer.sln が置かれたリポジトリルートを上方向探索で見つける。
    private static string FindRepositoryRoot()
    {
        DirectoryInfo? dir = new(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "Recognizer.sln")))
            {
                return dir.FullName;
            }

            dir = dir.Parent;
        }

        throw new InvalidOperationException("Recognizer.sln を含むリポジトリルートを特定できなかった。");
    }

    // ビルド生成物(bin/obj)を除いた C# ソースを列挙する。
    private static IEnumerable<string> EnumerateSourceFiles(string root)
        => Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories)
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                && !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal));
}
