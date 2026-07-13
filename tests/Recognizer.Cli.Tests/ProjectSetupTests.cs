namespace Recognizer.Cli.Tests;

/// <summary>
/// プロジェクト骨格(参照関係と InternalsVisibleTo)が成立していることを確認する暫定テスト。
/// </summary>
// Why not: 振る舞いのテストはタスク 1.2 以降のテスト基盤(CliTestHost)に載せる。
// ここでは「テストが 0 件だとテストプロジェクトとして成立しない」ための最小のスモークテストに留める。
public sealed class ProjectSetupTests
{
    [Fact]
    public void CLI_アセンブリをロードできる()
    {
        // Why not: GetReferencedAssemblies でライブラリ参照を検査しない。Roslyn は未使用の
        // アセンブリ参照をマニフェストに残さないため、暫定 Program.cs の間は偽陰性になる。
        // ProjectReference の成立は、下の FaceDetector 型解決(コンパイル時)で担保する。
        Assert.Equal("Recognizer.Cli", typeof(Program).Assembly.GetName().Name);
    }

    [Fact]
    public void CLI_からライブラリの公開型を参照できる()
    {
        Assert.Equal("Recognizer", typeof(global::Recognizer.FaceDetector).Assembly.GetName().Name);
    }
}
