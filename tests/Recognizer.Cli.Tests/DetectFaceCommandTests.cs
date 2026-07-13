using System.Text.Json;

namespace Recognizer.Cli.Tests;

/// <summary>
/// detect-face コマンドの正常系(要件 3.1〜3.7・8.1・8.3)と、閾値オプションがライブラリへ渡ることの検証。
/// 出力の構造(要件 6.3・6.5)は JSON をパースして確かめる。
/// </summary>
// Why not: 期待値をライブラリの実行結果から作らない。定数出力 fixture の候補は Fixtures/README.md が
// 「(cx, cy, w, h, conf)」まで定めており(A 0.95 / B 0.85 / D 0.75、A' 0.90 は NMS 抑制、C 0.60・E 0.50 は
// 既定閾値 0.7 未満)、期待値をそこから独立に書けるため、実装の写し取りにならない(research §4)。
public sealed class DetectFaceCommandTests
{
    /// <summary>定数出力・ランドマーク無し(Landmarks=null)。</summary>
    private const string ConstantFaceModel = "face_nchw_transposed_f5.onnx";

    /// <summary>定数出力・ランドマーク付き(F=20)。</summary>
    private const string LandmarkFaceModel = "face_nchw_transposed_f20.onnx";

    /// <summary>入力の平均画素値が confidence になる(白画像 → 検出 / 黒画像 → 未検出)。</summary>
    private const string InputConfFaceModel = "face_inputconf_f5.onnx";

    // 要件 3.1・3.2・3.3・3.7・6.3・8.1
    [Fact]
    public async Task detect_face_は検出結果を1行のJSONでstdoutに出力し終了コード0で終わる()
    {
        using CliTestHost host = new();
        string image = host.CreateWhiteImage();

        (int exitCode, string stdout, string stderr) = await CliTestHost.RunCliAsync(
            "detect-face", image, "--model", CliTestHost.FixturePath(ConstantFaceModel));

        Assert.Equal(ExitCodes.Success, exitCode);

        // 要件 6.1: 成功時の出力は stdout に限る。
        Assert.Empty(stderr);

        // 要件 6.3: 末尾の改行 1 個以外に改行を含まない。
        string trimmed = stdout.TrimEnd('\r', '\n');
        Assert.NotEqual(stdout, trimmed);
        Assert.DoesNotContain('\n', trimmed);

        JsonElement root = ReadJson(stdout);
        Assert.Equal(image, root.GetProperty("image").GetString());

        JsonElement faces = root.GetProperty("faces");
        Assert.Equal(JsonValueKind.Array, faces.ValueKind);

        // 要件 3.7: ライブラリの返却順(信頼度降順)のまま。並べ替えれば順序が変わって落ちる。
        Assert.Equal([0.95f, 0.85f, 0.75f], Confidences(faces));

        // 要件 3.3: bbox / confidence / landmarks を持つ。
        JsonElement first = faces[0];
        JsonElement bbox = first.GetProperty("bbox");
        Assert.Equal(JsonValueKind.Number, bbox.GetProperty("x").ValueKind);
        Assert.Equal(JsonValueKind.Number, bbox.GetProperty("y").ValueKind);
        Assert.Equal(JsonValueKind.Number, bbox.GetProperty("width").ValueKind);
        Assert.Equal(JsonValueKind.Number, bbox.GetProperty("height").ValueKind);
        Assert.True(bbox.GetProperty("width").GetSingle() > 0f);

        // 要件 3.5: ランドマークを出力しないモデルでは null(キーごと省略しない)。
        Assert.Equal(JsonValueKind.Null, first.GetProperty("landmarks").ValueKind);
    }

    // 要件 6.5: image は位置引数の文字列をそのまま出力する(絶対パスへ正規化しない)。
    // Why not: 引数と出力が等しいことを絶対パスで確かめるだけでは足りない。CliTestHost が渡すのは既に
    // 正規形の絶対パスであり、Path.GetFullPath を挟んでも出力は変わらず、退行を検出できない。
    // "dir/./file.png" は正当に開けるが正規化すると "/./" が畳まれるため、正規化の有無を観測できる。
    [Fact]
    public async Task image_は位置引数の文字列をそのまま出力する()
    {
        using CliTestHost host = new();
        string image = host.CreateWhiteImage();
        string unnormalized = Path.Combine(Path.GetDirectoryName(image)!, ".", Path.GetFileName(image));

        // このテストが意味を持つ前提(正規化すると文字列が変わること)を明示する。
        Assert.NotEqual(unnormalized, Path.GetFullPath(unnormalized));

        (int exitCode, string stdout, string stderr) = await CliTestHost.RunCliAsync(
            "detect-face", unnormalized, "--model", CliTestHost.FixturePath(ConstantFaceModel));

        Assert.Equal(ExitCodes.Success, exitCode);
        Assert.Empty(stderr);
        Assert.Equal(unnormalized, ReadJson(stdout).GetProperty("image").GetString());
    }

    // 要件 3.4: ランドマークを出力するモデルでは 5 点(x / y)を出力する。
    [Fact]
    public async Task ランドマークを出力するモデルでは5点を出力する()
    {
        using CliTestHost host = new();
        string image = host.CreateWhiteImage();

        (int exitCode, string stdout, string stderr) = await CliTestHost.RunCliAsync(
            "detect-face", image, "--model", CliTestHost.FixturePath(LandmarkFaceModel));

        Assert.Equal(ExitCodes.Success, exitCode);
        Assert.Empty(stderr);

        JsonElement faces = ReadJson(stdout).GetProperty("faces");
        Assert.Equal([0.95f, 0.85f, 0.75f], Confidences(faces));

        foreach (JsonElement face in faces.EnumerateArray())
        {
            JsonElement landmarks = face.GetProperty("landmarks");
            Assert.Equal(JsonValueKind.Object, landmarks.ValueKind);

            foreach (string name in (string[])["leftEye", "rightEye", "nose", "leftMouth", "rightMouth"])
            {
                JsonElement point = landmarks.GetProperty(name);
                Assert.Equal(JsonValueKind.Number, point.GetProperty("x").ValueKind);
                Assert.Equal(JsonValueKind.Number, point.GetProperty("y").ValueKind);
            }
        }
    }

    // 要件 3.6・7.9・8.3: 顔 0 件は空配列 + 終了コード 0(失敗として扱わない)。
    [Fact]
    public async Task 顔が1件も検出されなければ空配列と終了コード0で終わる()
    {
        using CliTestHost host = new();
        string blackImage = host.CreateBlackImage();

        (int exitCode, string stdout, string stderr) = await CliTestHost.RunCliAsync(
            "detect-face", blackImage, "--model", CliTestHost.FixturePath(InputConfFaceModel));

        Assert.Equal(ExitCodes.Success, exitCode);
        Assert.Empty(stderr);

        JsonElement root = ReadJson(stdout);
        Assert.Equal(blackImage, root.GetProperty("image").GetString());

        JsonElement faces = root.GetProperty("faces");
        Assert.Equal(JsonValueKind.Array, faces.ValueKind);
        Assert.Equal(0, faces.GetArrayLength());
    }

    // 要件 2.3・2.6: --confidence の既定値は 0.7 で、指定値はライブラリまで届く。
    // 定数出力 fixture の候補 conf は 0.95 / 0.90(NMS 抑制)/ 0.85 / 0.75 / 0.60 / 0.50 なので、
    // 通過件数が閾値ごとに変わる。既定値が 0.5 なら省略時に 5 件、0.9 なら 1 件になり、このケースが落ちる。
    public static TheoryData<string[], float[]> ConfidenceCases => new()
    {
        { [], [0.95f, 0.85f, 0.75f] },
        { ["--confidence", "0.55"], [0.95f, 0.85f, 0.75f, 0.6f] },
        { ["--confidence", "0.9"], [0.95f] },
    };

    [Theory]
    [MemberData(nameof(ConfidenceCases))]
    public async Task confidenceの既定値は0_7で指定値はライブラリに渡る(string[] options, float[] expected)
    {
        using CliTestHost host = new();
        string image = host.CreateWhiteImage();

        (int exitCode, string stdout, string stderr) = await CliTestHost.RunCliAsync(
            ["detect-face", image, "--model", CliTestHost.FixturePath(ConstantFaceModel), .. options]);

        Assert.Equal(ExitCodes.Success, exitCode);
        Assert.Empty(stderr);
        Assert.Equal(expected, Confidences(ReadJson(stdout).GetProperty("faces")));
    }

    // 要件 2.3: --nms の既定値は 0.5 で、指定値はライブラリまで届く。候補 A(0.95)と A'(0.90)は IoU 0.68 のため、
    // 既定 0.5 では A' が抑制されるが、1.0 では抑制されず 4 件になる(Fixtures/README.md)。
    [Fact]
    public async Task nmsの指定値はライブラリに渡る()
    {
        using CliTestHost host = new();
        string image = host.CreateWhiteImage();

        (int exitCode, string stdout, string stderr) = await CliTestHost.RunCliAsync(
            "detect-face", image, "--model", CliTestHost.FixturePath(ConstantFaceModel), "--nms", "1.0");

        Assert.Equal(ExitCodes.Success, exitCode);
        Assert.Empty(stderr);
        Assert.Equal([0.95f, 0.9f, 0.85f, 0.75f], Confidences(ReadJson(stdout).GetProperty("faces")));
    }

    private static float[] Confidences(JsonElement faces)
        => [.. faces.EnumerateArray().Select(face => face.GetProperty("confidence").GetSingle())];

    // Why not: CliJson で逆シリアライズしない。キー名(image / faces / bbox / confidence / landmarks)は
    // 機械可読な契約(要件 3.2・3.3)であり、シリアライズ設定を共有すると命名の退行を素通しする。
    private static JsonElement ReadJson(string stdout)
    {
        using JsonDocument document = JsonDocument.Parse(stdout);

        return document.RootElement.Clone();
    }
}
