using System.Text;
using OpenCvSharp;

namespace Recognizer.Cli;

/// <summary>
/// プロセス境界(design §2 Boundary Map)。標準出力・標準エラーと Ctrl+C の
/// <see cref="CancellationToken"/> を <see cref="CliApplication"/> に渡し、終了コードを返すだけの層。
/// </summary>
internal static class Program
{
    private static async Task<int> Main(string[] args)
    {
        SilenceOpenCvNativeLog();
        TryUseUtf8Output();

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

    // OpenCV のネイティブ層は画像の読み込みに失敗すると警告行を fd 2 へ直接書く。放置すると stderr が
    // 「警告行 + エラー JSON」の 2 行になり、stderr を JSON としてパースする利用者が壊れる(要件 7.1 の
    // 機械可読なエラー出力が成立しない)。CLI の stderr は JSON だけに保つ。
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
