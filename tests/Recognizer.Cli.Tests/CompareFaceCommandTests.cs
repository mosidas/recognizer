using System.CommandLine;
using System.Text.Json;
using Recognizer.Cli.Commands;
using Recognizer.Cli.Errors;

namespace Recognizer.Cli.Tests;

/// <summary>
/// compare-face コマンドの正常系(要件 5.1〜5.8・8.1・8.3)。3 つの status(Success / NoFaceInImage1 /
/// NoFaceInImage2)はいずれも成功(終了コード 0。要件 7.9)であることを固定する。
/// </summary>
// Why not: 期待値をライブラリの実行結果から作らない。face_inputconf_f5.onnx は「conf = 前処理(/255)後の
// 入力平均」なので、白画像(conf 1.0)は検出・黒画像(conf 0)は未検出と、fixture の定義から独立に決まる
// (Fixtures/README.md ㉑)。埋め込みも入力依存の決定論(同 ⑰)で、同一入力なら同一ベクトル = 類似度 1.0。
public sealed class CompareFaceCommandTests
{
    /// <summary>入力の平均画素値が confidence になる(白画像 → 検出 / 黒画像 → 未検出)。</summary>
    private const string InputConfFaceModel = "face_inputconf_f5.onnx";

    /// <summary>入力依存の埋め込み([mean(R), mean(G), mean(B), 1.0])。</summary>
    private const string EmbeddingModel = "embed_nchw_meanrgb_d4.onnx";

    /// <summary>conf = 153 / 255 ≒ 0.6(実測 0.6009)。既定の検出閾値 0.7 では未検出、0.5 なら検出される画素値。</summary>
    private const byte MidConfidenceLevel = 153;

    // 要件 5.1・5.2・5.3・5.5・6.3・8.1
    [Fact]
    public async Task compare_face_は比較結果を1行のJSONでstdoutに出力し終了コード0で終わる()
    {
        using CliTestHost host = new();
        string image1 = host.CreateWhiteImage();
        string image2 = host.CreateWhiteImage();

        (int exitCode, string stdout, string stderr) = await RunAsync(image1, image2);

        Assert.Equal(ExitCodes.Success, exitCode);

        // 要件 6.1: 成功時の出力は stdout に限る。
        Assert.Empty(stderr);

        // 要件 6.3: 末尾の改行 1 個以外に改行を含まない。
        string trimmed = stdout.TrimEnd('\r', '\n');
        Assert.NotEqual(stdout, trimmed);
        Assert.DoesNotContain('\n', trimmed);

        JsonElement root = ReadJson(stdout);

        // 要件 5.2・5.4: トップレベルのキーはこの 6 個に限る。同一人物判定(match 等)を足していないことを、
        // キー集合の完全一致で固定する(存在検査だけでは余計なキーの追加を見逃す)。
        Assert.Equal(
            ["image1", "image2", "status", "similarity", "face1", "face2"],
            PropertyNames(root));

        Assert.Equal(image1, root.GetProperty("image1").GetString());
        Assert.Equal(image2, root.GetProperty("image2").GetString());

        // 要件 5.3: status は列挙子名そのまま。
        Assert.Equal("Success", root.GetProperty("status").GetString());

        // 同一の白画像 → 埋め込みも同一 → コサイン類似度 1.0(Fixtures/README.md ⑰)。
        // Why not: 完全一致を求めない。類似度は内積と平方根の計算結果で、丸め誤差が乗る。
        Assert.Equal(1.0f, root.GetProperty("similarity").GetSingle(), precision: 4);

        // 要件 5.5: face1 / face2 に使用した顔の bbox と confidence(ランドマークは含めない)。
        foreach (string name in (string[])["face1", "face2"])
        {
            JsonElement face = root.GetProperty(name);
            Assert.Equal(["bbox", "confidence"], PropertyNames(face));

            // 白画像 → conf = 平均画素値 1.0(Fixtures/README.md ㉑)。
            Assert.Equal(1.0f, face.GetProperty("confidence").GetSingle(), precision: 4);

            JsonElement bbox = face.GetProperty("bbox");
            Assert.Equal(["x", "y", "width", "height"], PropertyNames(bbox));
            Assert.True(bbox.GetProperty("width").GetSingle() > 0f);
            Assert.True(bbox.GetProperty("height").GetSingle() > 0f);
        }
    }

    // 要件 5.6・7.9・8.3: 画像 1 が未検出なら similarity 0・face1 / face2 とも null・終了コード 0。
    // Why not: 「画像 2 に顔があるので face2 は出る」と考えない。ライブラリは画像 1 の未検出時点で早期返却し、
    // 画像 2 を評価しない(FaceRecognizer.cs:252)。ここで白画像(顔あり)を画像 2 に置くのは、その早期返却を
    // 行使するため。face2 を出す実装に退行すれば、このケースが落ちる。
    [Fact]
    public async Task 画像1が未検出なら類似度0でface1とface2の両方がnullになる()
    {
        using CliTestHost host = new();
        string image1 = host.CreateBlackImage();
        string image2 = host.CreateWhiteImage();

        (int exitCode, string stdout, string stderr) = await RunAsync(image1, image2);

        Assert.Equal(ExitCodes.Success, exitCode);
        Assert.Empty(stderr);

        JsonElement root = ReadJson(stdout);
        Assert.Equal("NoFaceInImage1", root.GetProperty("status").GetString());
        Assert.Equal(0f, root.GetProperty("similarity").GetSingle());

        // 要件 5.6: 両方 null。キーごと省略もしない。
        Assert.Equal(JsonValueKind.Null, root.GetProperty("face1").ValueKind);
        Assert.Equal(JsonValueKind.Null, root.GetProperty("face2").ValueKind);
    }

    // 要件 5.7・7.9・8.3: 画像 2 のみ未検出なら similarity 0・face1 は出力・face2 は null・終了コード 0。
    [Fact]
    public async Task 画像2のみ未検出なら類似度0でface1は出力されface2はnullになる()
    {
        using CliTestHost host = new();
        string image1 = host.CreateWhiteImage();
        string image2 = host.CreateBlackImage();

        (int exitCode, string stdout, string stderr) = await RunAsync(image1, image2);

        Assert.Equal(ExitCodes.Success, exitCode);
        Assert.Empty(stderr);

        JsonElement root = ReadJson(stdout);
        Assert.Equal("NoFaceInImage2", root.GetProperty("status").GetString());
        Assert.Equal(0f, root.GetProperty("similarity").GetSingle());

        JsonElement face1 = root.GetProperty("face1");
        Assert.Equal(["bbox", "confidence"], PropertyNames(face1));
        Assert.Equal(1.0f, face1.GetProperty("confidence").GetSingle(), precision: 4);

        Assert.Equal(JsonValueKind.Null, root.GetProperty("face2").ValueKind);
    }

    // 要件 6.5: image1 / image2 は位置引数の文字列をそのまま出力する(絶対パスへ正規化しない)。
    // Why not: 正規形の絶対パスで確かめるだけでは足りない。Path.GetFullPath を挟んでも出力が変わらず、
    // 退行を検出できない。"dir/./file.png" は正当に開けるが正規化すると "/./" が畳まれる。
    [Fact]
    public async Task image1とimage2は位置引数の文字列をそのまま出力する()
    {
        using CliTestHost host = new();
        string image1 = Unnormalize(host.CreateWhiteImage());
        string image2 = Unnormalize(host.CreateWhiteImage());

        Assert.NotEqual(image1, Path.GetFullPath(image1));

        (int exitCode, string stdout, string stderr) = await RunAsync(image1, image2);

        Assert.Equal(ExitCodes.Success, exitCode);
        Assert.Empty(stderr);

        JsonElement root = ReadJson(stdout);
        Assert.Equal(image1, root.GetProperty("image1").GetString());
        Assert.Equal(image2, root.GetProperty("image2").GetString());
    }

    // 要件 2.3: --detection-threshold の既定値は 0.7 で、指定値はライブラリまで届く。
    // 画素値 153 の一様画像は conf = 153 / 255 ≒ 0.6(実測 0.6009) になり(Fixtures/README.md ㉑)、ライブラリの閾値フィルタは
    // conf < threshold を除外する。したがって既定 0.7 では未検出(NoFaceInImage1)、0.5 を明示すれば検出
    // (Success)と挙動が割れる。既定値を 0.5 に取り違える退行は、この 2 ケースの前者が落ちて検出される。
    [Theory]
    [InlineData(new string[] { }, "NoFaceInImage1")]
    [InlineData(new[] { "--detection-threshold", "0.5" }, "Success")]
    public async Task detection_thresholdの既定値は0_7で指定値はライブラリに渡る(string[] options, string expectedStatus)
    {
        using CliTestHost host = new();
        string image1 = host.CreateGrayImage(MidConfidenceLevel);
        string image2 = host.CreateWhiteImage();

        (int exitCode, string stdout, string stderr) = await RunAsync(image1, image2, options);

        Assert.Equal(ExitCodes.Success, exitCode);
        Assert.Empty(stderr);
        Assert.Equal(expectedStatus, ReadJson(stdout).GetProperty("status").GetString());
    }

    private static Task<(int ExitCode, string Stdout, string Stderr)> RunAsync(
        string image1,
        string image2,
        params string[] options)
        => CliTestHost.RunCliAsync([
            "compare-face",
            image1,
            image2,
            "--detector-model",
            CliTestHost.FixturePath(InputConfFaceModel),
            "--embedding-model",
            CliTestHost.FixturePath(EmbeddingModel),
            .. options,
        ]);

    private static string Unnormalize(string path)
        => Path.Combine(Path.GetDirectoryName(path)!, ".", Path.GetFileName(path));

    private static string[] PropertyNames(JsonElement element)
        => [.. element.EnumerateObject().Select(property => property.Name)];

    // Why not: CliJson で逆シリアライズしない。キー名(image1 / image2 / status / similarity / face1 / face2)は
    // 機械可読な契約(要件 5.2)であり、シリアライズ設定を共有すると命名の退行を素通しする。
    private static JsonElement ReadJson(string stdout)
    {
        using JsonDocument document = JsonDocument.Parse(stdout);

        return document.RootElement.Clone();
    }

    // 要件 2.3: --nms の既定値は 3 コマンドすべてで 0.5。
    // Why not 出力で検証しない: compare-face は最高信頼度の顔だけを使うため、NMS 閾値を変えても top-1 は変わらず
    // 出力に差が出ない(detect-face / detect-object は件数が変わるので出力で固定できる)。既定値の取り違えを
    // 検出する手段がパース結果しかないため、ここだけ構造を直接見る。
    [Fact]
    public void Nmsの既定値は0_5()
    {
        UsageErrorCollector collector = new();

        RootCommand root = [];
        root.Add(CompareFaceCommand.Create(collector));

        ParseResult parseResult = root.Parse(
            ["compare-face", "a.png", "b.png", "--detector-model", "d.onnx", "--embedding-model", "e.onnx"]);

        Assert.Empty(parseResult.Errors);
        Assert.Equal(0.5f, parseResult.GetValue<float>("--nms"));
        Assert.Equal(0.7f, parseResult.GetValue<float>("--detection-threshold"));
    }
}
