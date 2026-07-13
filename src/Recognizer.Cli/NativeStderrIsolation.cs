using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace Recognizer.Cli;

/// <summary>
/// プロセスの fd 2 を <c>/dev/null</c> に向け、CLI 自身のエラー出力だけを退避した実 stderr へ通す。
/// これにより、ネイティブ層(OpenCV / ONNX Runtime 等)が fd 2 へ何を書いても観測される stderr は
/// CLI が書く 1 行の JSON だけになる(要件 6.1・7.1)。
/// </summary>
internal static class NativeStderrIsolation
{
    private const int StandardErrorFd = 2;

    /// <summary>
    /// stderr を隔離する。成功したときだけ <c>true</c> を返し、<see cref="Console.Error"/> は
    /// 退避した実 stderr を指す writer に差し替わる。
    /// </summary>
    // Why not 失敗を例外にしない: 隔離はログ衛生の改善であって CLI の本務ではない。dup が失敗する環境
    // (fd の枯渇・stderr が閉じられている等)や Unix 以外で例外を投げると、CLI 本来の仕事まで巻き添えで
    // 死ぬ。false を返して呼び出し側に従来の抑止へフォールバックさせる。
    public static bool TryIsolate()
    {
        // Why not Windows で試さない: dup / dup2 は libc の API であり、Windows には無い。呼び出し側は
        // 従来の Cv2.SetLogLevel による抑止に退避する(既存の緑を保つ)。
        if (!OperatingSystem.IsLinux() && !OperatingSystem.IsMacOS())
        {
            return false;
        }

        try
        {
            return TryRedirectStandardErrorFd();
        }
        catch (DllNotFoundException)
        {
            return false;
        }
        catch (EntryPointNotFoundException)
        {
            return false;
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static bool TryRedirectStandardErrorFd()
    {
        // Why not open(2) を P/Invoke しない: 文字列引数のマーシャリングを伴う P/Invoke はプロジェクトに
        // AllowUnsafeBlocks を要求する。File.OpenHandle が返す SafeFileHandle は Unix では fd そのものなので、
        // そのまま int として dup2 に渡せる(devcontainer / linux-x64 で実測)。
        using SafeFileHandle devNull = File.OpenHandle("/dev/null", FileMode.Open, FileAccess.Write);

        int duplicated = Dup(StandardErrorFd);
        if (duplicated < 0)
        {
            return false;
        }

        // Why ownsHandle: true: 以降の失敗経路でも .NET が退避 fd を閉じてくれるため、close(2) の
        // P/Invoke を増やさずに fd のリークを防げる。
        SafeFileHandle savedStandardError = new((IntPtr)duplicated, ownsHandle: true);

        // Why writer を dup2 より先に作る: 失敗しうる準備をすべて済ませてから、後戻りできない dup2 を最後に撃つ。
        // 逆順(dup2 → writer 構築)にすると、writer の構築が失敗したときに fd 2 を /dev/null に向けたまま
        // false を返すことになり、Console.Error は元の fd 2 束縛(= もはや /dev/null)のままになる。その結果
        // CLI のエラー JSON が誰にも届かず、終了コードだけが返って stderr が空になる ―― 要件 7.1 を「静かに」
        // 破る、今まさに直しているバグと同じ型の failure mode を自分で作り込むことになる。
        StreamWriter? writer = TryCreateStandardErrorWriter(savedStandardError);
        if (writer is null)
        {
            savedStandardError.Dispose();

            return false;
        }

        if (Dup2((int)devNull.DangerousGetHandle(), StandardErrorFd) < 0)
        {
            // 退避 fd は writer(FileStream → SafeFileHandle)の Dispose で閉じられる。fd 2 は無傷。
            writer.Dispose();

            return false;
        }

        Console.SetError(writer);

        return true;
    }

    // Why BOM を出さない: stderr の 1 行はそのまま JSON としてパースされる契約(要件 7.1)であり、
    // 先頭に BOM が混じるとパーサが壊れる。
    // Why AutoFlush: CLI はエラー JSON を書いた直後にプロセスを終了しうるため、明示的な Flush に頼らない。
    private static StreamWriter? TryCreateStandardErrorWriter(SafeFileHandle savedStandardError)
    {
        try
        {
            return new StreamWriter(
                new FileStream(savedStandardError, FileAccess.Write),
                new UTF8Encoding(encoderShouldEmitUTF8Identifier: false))
            {
                AutoFlush = true,
            };
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
        catch (ArgumentException)
        {
            return null;
        }
    }

    // Why not LibraryImport: 引数が int だけでも生成コードが unsafe を要求し(SYSLIB1062)、CLI に
    // AllowUnsafeBlocks を足すことになる。blittable な int しか渡さないここでは DllImport で足りる。
    [DllImport("libc", EntryPoint = "dup")]
    private static extern int Dup(int fd);

    [DllImport("libc", EntryPoint = "dup2")]
    private static extern int Dup2(int oldFd, int newFd);
}
