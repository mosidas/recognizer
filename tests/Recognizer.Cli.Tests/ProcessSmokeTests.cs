using System.Diagnostics;
using System.Text;
using System.Text.Json;

namespace Recognizer.Cli.Tests;

/// <summary>
/// CLI を実プロセスとして起動し、OS の標準エラー(fd 2)そのものを検証する(要件 7.1・7.2・8.2)。
/// </summary>
// Why not インプロセス実行(CliTestHost.RunCliAsync)で代替しない: OpenCV のネイティブ層は画像の読み込みに
// 失敗すると .NET の TextWriter を経由せず fd 2 へ直接警告行を書く。インプロセスのテストが捕捉するのは
// StringWriter であって実 stderr ではないため、警告行の混入を原理的に観測できない。実際、
// Program.SilenceOpenCvNativeLog() の呼び出しを消しても他の全テストはグリーンのままになる(最終検証で判明)。
// 実プロセスを起動して初めて「stderr が 1 行の JSON である」という要件 7.1 の契約を検証できる。
public sealed class ProcessSmokeTests
{
    private const string ValidFaceModel = "face_nchw_standard_f5.onnx";

    // 最頻の実行時エラー(画像不在)で、stderr がそのまま JSON としてパースできること。
    // ここが壊れると、stderr を JSON として読むスクリプトが軒並み動かなくなる。
    [Fact]
    public async Task 実プロセス_画像不在のstderrは1行のJSONのみで警告行を含まない()
    {
        using CliTestHost host = new();

        (int exitCode, string stdout, string stderr) = await RunProcessAsync(
            "detect-face", host.NonExistentPath(".png"), "--model", CliTestHost.FixturePath(ValidFaceModel));

        Assert.Equal(ExitCodes.RuntimeError, exitCode);
        Assert.Empty(stdout);

        AssertSingleLineJson(stderr, expectedCode: "imageLoadFailed");
    }

    // 画像としてデコードできないファイルでも同様(OpenCV は同じ経路で警告を書きうる)。
    [Fact]
    public async Task 実プロセス_デコード不可のstderrは1行のJSONのみ()
    {
        using CliTestHost host = new();

        (int exitCode, string stdout, string stderr) = await RunProcessAsync(
            "detect-face", host.CreateNonImageFile(".png"), "--model", CliTestHost.FixturePath(ValidFaceModel));

        Assert.Equal(ExitCodes.RuntimeError, exitCode);
        Assert.Empty(stdout);

        AssertSingleLineJson(stderr, expectedCode: "imageLoadFailed");
    }

    // 正常系では stderr が完全に空であること(要件 6.1)。ネイティブの情報ログが混ざらないことも含めて実プロセスで固定する。
    [Fact]
    public async Task 実プロセス_正常系はstdoutに1行のJSONを出しstderrは空()
    {
        using CliTestHost host = new();

        (int exitCode, string stdout, string stderr) = await RunProcessAsync(
            "detect-face", host.CreateWhiteImage(), "--model", CliTestHost.FixturePath(ValidFaceModel));

        Assert.Equal(ExitCodes.Success, exitCode);
        Assert.Empty(stderr);

        string body = stdout.TrimEnd('\r', '\n');
        Assert.DoesNotContain('\n', body);

        using JsonDocument document = JsonDocument.Parse(body);
        Assert.True(document.RootElement.TryGetProperty("faces", out _));
    }

    private static void AssertSingleLineJson(string stderr, string expectedCode)
    {
        string body = stderr.TrimEnd('\r', '\n');

        // 警告行が混ざると、この 2 つのどちらかが必ず落ちる。
        Assert.DoesNotContain('\n', body);

        using JsonDocument document = JsonDocument.Parse(body);
        Assert.Equal(expectedCode, document.RootElement.GetProperty("code").GetString());
        Assert.NotEmpty(document.RootElement.GetProperty("error").GetString()!);
    }

    // ProjectReference により、CLI のアセンブリはテストの出力ディレクトリにも配置される。
    // Why not publish 成果物を使わない: publish は数十秒かかり、単体テストの実行時間を大きく損なう。
    // 検証したいのは「ネイティブの警告が実 stderr に混ざらないこと」であり、publish の有無に依存しない。
    private static async Task<(int ExitCode, string Stdout, string Stderr)> RunProcessAsync(params string[] args)
    {
        string cliAssembly = Path.Combine(AppContext.BaseDirectory, "Recognizer.Cli.dll");
        Assert.True(File.Exists(cliAssembly), $"CLI のアセンブリが見つかりません: {cliAssembly}");

        ProcessStartInfo startInfo = new()
        {
            FileName = "dotnet",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
            UseShellExecute = false,
        };

        startInfo.ArgumentList.Add(cliAssembly);
        foreach (string arg in args)
        {
            startInfo.ArgumentList.Add(arg);
        }

        using Process process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("CLI プロセスを起動できませんでした。");

        // Why: 先に非同期で読み始めてから待つ。逆順にするとパイプが埋まった時点で相互に待ち合ってデッドロックする。
        Task<string> stdout = process.StandardOutput.ReadToEndAsync();
        Task<string> stderr = process.StandardError.ReadToEndAsync();

        // Why: タイムアウトを持たせる。CLI がハングしたとき、テストが落ちずに CI ジョブ全体の
        // タイムアウト(20 分)まで固まると、原因の切り分けができない。
        using CancellationTokenSource timeout = new(TimeSpan.FromMinutes(2));
        try
        {
            await process.WaitForExitAsync(timeout.Token);
        }
        catch (OperationCanceledException)
        {
            process.Kill(entireProcessTree: true);
            Assert.Fail("CLI プロセスが 2 分以内に終了しませんでした。");
        }

        return (process.ExitCode, await stdout, await stderr);
    }
}
