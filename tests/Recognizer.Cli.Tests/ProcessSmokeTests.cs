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
// fd 2 の隔離(Program.ConfigureNativeStderr)を外しても他の全テストはグリーンのままになる。
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

    // 画像としてデコードできないファイルでも同様。
    // 注: devcontainer / linux-x64 では OpenCV はこのケースで警告を書かない(ファイル自体は開けるため)。
    // 一方 CI の ubuntu-latest では汚染が観測された。環境差があるので、汚染の実在に依存する検証は
    // 画像不在ケース(下のカナリア)が担い、このテストは契約「stderr は 1 行の JSON」だけを固定する。
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

    // 画像不在のテストが空虚でないことを保証するカナリア。隔離を素通しさせると、OpenCV ネイティブが fd 2 へ書く
    // 警告行が実 stderr に現れ、stderr は「1 行の JSON」でなくなる。つまり画像不在のテストは、実在する汚染源の
    // 上で隔離を検証している(実測: 隔離を無効化すると devcontainer で落ちるのは画像不在の 1 件。デコード不可と
    // 正常系はここでは OpenCV が fd 2 に書かないため、汚染がなくても緑になる)。
    // このテストが落ちるときは、汚染源が消えたか passthrough が壊れたかのどちらかであり、いずれの場合も
    // 画像不在のテストは「隔離が壊れても気づけない」状態に陥っている。
    [Fact]
    public async Task 実プロセス_passthrough指定時はネイティブの警告行が実stderrに現れる()
    {
        // Why linux 限定: macOS / Windows の OpenCV が同じ警告を fd 2 へ書くかは環境依存であり、そこに依存させると
        // 本質と無関係な失敗を生む。xunit 2.x には動的スキップ(Assert.Skip)が無いため early return でガードする。
        if (!OperatingSystem.IsLinux())
        {
            return;
        }

        using CliTestHost host = new();

        (int exitCode, string stdout, string stderr) = await RunProcessAsync(
            new Dictionary<string, string> { [Program.NativeStderrPassthroughVariable] = "1" },
            ["detect-face", host.NonExistentPath(".png"), "--model", CliTestHost.FixturePath(ValidFaceModel)]);

        Assert.Equal(ExitCodes.RuntimeError, exitCode);
        Assert.Empty(stdout);

        // Why not 警告の文言("[ WARN:...] findDecoder ...")に依存しない: 検証したいのは「ネイティブが実 stderr を
        // 汚しうる」という事実であって OpenCV のログ書式ではない。stderr が 2 行以上になることがその事実を示す。
        string[] lines = stderr.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        Assert.True(lines.Length > 1, $"ネイティブの警告行が観測されませんでした。stderr: {stderr}");

        // 汚染があっても CLI 自身のエラー JSON は最後の行として出続ける(passthrough は隔離をやめるだけ)。
        using JsonDocument document = JsonDocument.Parse(lines[^1].TrimEnd('\r'));
        Assert.Equal("imageLoadFailed", document.RootElement.GetProperty("code").GetString());
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
    private static Task<(int ExitCode, string Stdout, string Stderr)> RunProcessAsync(params string[] args)
        => RunProcessAsync(environment: null, args);

    private static async Task<(int ExitCode, string Stdout, string Stderr)> RunProcessAsync(
        IReadOnlyDictionary<string, string>? environment,
        string[] args)
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

        // Why 明示的に除去する: 子プロセスは親(テストランナー)の環境を継承するため、開発者や CI の環境に
        // RECOGNIZER_NATIVE_STDERR=1 が export されていると、隔離が素通しになって契約テストが理由不明に落ちる。
        // 環境非依存にすることがこの修正の趣旨なので、テスト自身も環境に依存させない。カナリアは下で明示的に
        // 与え直すため影響を受けない。
        _ = startInfo.Environment.Remove(Program.NativeStderrPassthroughVariable);

        foreach ((string name, string value) in environment ?? new Dictionary<string, string>())
        {
            startInfo.Environment[name] = value;
        }

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
