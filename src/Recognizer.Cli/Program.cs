using System.Text;
using OpenCvSharp;

namespace Recognizer.Cli;

/// <summary>
/// プロセス境界(design §2 Boundary Map)。標準出力・標準エラーと Ctrl+C の
/// <see cref="CancellationToken"/> を <see cref="CliApplication"/> に渡し、終了コードを返すだけの層。
/// </summary>
internal static class Program
{
    /// <summary>
    /// 診断用の環境変数。<c>1</c> を指定したときだけ、ネイティブ層の stderr を隔離も抑止もせずそのまま流す。
    /// </summary>
    internal const string NativeStderrPassthroughVariable = "RECOGNIZER_NATIVE_STDERR";

    private static async Task<int> Main(string[] args)
    {
        // Why この順序: Console.OutputEncoding の setter は、キャッシュ済みの Console.Out / Console.Error を
        // 破棄して新しいエンコーディングで作り直す(未リダイレクトの Console.Error が setter 前後で別インスタンスに
        // なることを実測)。隔離を先に行うと、Console.SetError で差し込んだ writer が生き残るかどうかが
        // 「リダイレクト済みの writer は破棄しない」という .NET 内部の実装詳細に依存する(.NET 10 は実測では
        // 保護したが、公開された契約ではない)。UTF-8 設定 → 隔離の順なら、その実装詳細に依存せず順序だけで
        // 正しさが決まる。
        TryUseUtf8Output();
        ConfigureNativeStderr();

        using CancellationTokenSource cancellation = new();

        void OnCancelKeyPress(object? sender, ConsoleCancelEventArgs e)
        {
            // 既定の即時終了を抑止し、実行中の処理に協調的なキャンセルの機会を与える。
            e.Cancel = true;
            cancellation.Cancel();
        }

        Console.CancelKeyPress += OnCancelKeyPress;
        try
        {
            return await CliApplication
                .RunAsync(args, Console.Out, Console.Error, cancellation.Token)
                .ConfigureAwait(false);
        }
        finally
        {
            Console.CancelKeyPress -= OnCancelKeyPress;
        }
    }

    // ネイティブ層(OpenCV・ONNX Runtime)は .NET の TextWriter を経由せず fd 2 へ直接書く。放置すると
    // stderr が「警告行 + エラー JSON」の 2 行になり、stderr を JSON としてパースする利用者が壊れる
    // (要件 6.1 の「正常時 stderr は空」・要件 7.1 の機械可読なエラー出力が成立しない)。
    private static void ConfigureNativeStderr()
    {
        // Why passthrough の逃げ道が要る: fd 2 を /dev/null へ向けると、ネイティブ由来の障害(モデルのロード
        // 失敗・OpenCV のデコード警告)の診断情報が完全に失われ、運用で原因を追えなくなる。この環境変数は
        // その逃げ道であり、同時に「汚染が実在すること」をテストが検証する手段でもある(カナリアテスト)。
        if (Environment.GetEnvironmentVariable(NativeStderrPassthroughVariable) == "1")
        {
            return;
        }

        // Why not 隔離できた環境で Cv2.SetLogLevel(SILENT) を併用しない: 隔離は「どのネイティブが何を書いても
        // 観測 stderr を汚さない」上位互換であり、OpenCV 固有の抑止は冗長。さらに抑止を残すと、実プロセスの
        // スモークテストがネイティブの fd 2 汚染を踏まなくなり、隔離が壊れてもテストが気づけない(テストが
        // 空虚になる)。devcontainer で汚染を踏むのは画像不在のケース(OpenCV がファイルを開けず警告を書く)で、
        // ここが空虚化を防ぐ要になっている。抑止だけに頼っていた頃は、CI の ubuntu-latest でのみ汚染が漏れていた。
        if (NativeStderrIsolation.TryIsolate())
        {
            return;
        }

        SilenceOpenCvNativeLog();
    }

    // 隔離を持てないプラットフォーム(Windows)と隔離に失敗した環境のフォールバック。fd 2 そのものは
    // 汚れうるが、実際に観測されている汚染源である OpenCV の警告だけは止まる。
    // Why not 環境変数 OPENCV_LOG_LEVEL=SILENT を設定しない: .NET の Environment.SetEnvironmentVariable は
    // Unix のネイティブ getenv に伝播せず、プロセス内から設定しても効かない(実測)。
    // Why not 失敗を致命にしない: この呼び出しは OpenCV のネイティブ資産をロードする。ロードできない環境で
    // 例外を素通しすると、OpenCV を必要としない経路(--help・使用法エラー)まで英語のスタックトレースで
    // 死に、要件 7.1・7.2 を破る。ログ抑止は品質改善であって CLI の本務ではない。
    private static void SilenceOpenCvNativeLog()
    {
        try
        {
            _ = Cv2.SetLogLevel(LogLevel.SILENT);
        }
        catch (DllNotFoundException)
        {
        }
        catch (TypeInitializationException)
        {
        }
    }

    // 出力 JSON には日本語のエラーメッセージ(要件 7.1)が生の非 ASCII として乗るため、コンソールの
    // コードページが UTF-8 でない環境(既定の Windows コンソール)では文字化けする。
    // Why not: 失敗を致命として扱わない。出力がリダイレクトされている場合など、環境によっては
    // エンコーディングの差し替え自体が失敗しうるが、その場合でも CLI 本来の仕事は続行できる。
    private static void TryUseUtf8Output()
    {
        try
        {
            Console.OutputEncoding = Encoding.UTF8;
        }
        catch (IOException)
        {
        }
        catch (PlatformNotSupportedException)
        {
        }
    }
}
