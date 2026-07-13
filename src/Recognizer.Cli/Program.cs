using System.Text;

namespace Recognizer.Cli;

/// <summary>
/// プロセス境界(design §2 Boundary Map)。標準出力・標準エラーと Ctrl+C の
/// <see cref="CancellationToken"/> を <see cref="CliApplication"/> に渡し、終了コードを返すだけの層。
/// </summary>
internal static class Program
{
    private static async Task<int> Main(string[] args)
    {
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
