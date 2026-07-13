using System.Text.Json;

namespace Recognizer.Cli.Tests;

/// <summary>
/// detect-object コマンドの正常系(要件 4.1〜4.3・4.5・4.7・4.8・8.1)と、閾値オプションがライブラリへ
/// 渡ることの検証。出力の構造(要件 6.3・6.5)は JSON をパースして確かめる。
/// </summary>
// Why not: 期待値をライブラリの実行結果から作らない。定数出力 fixture の候補は Fixtures/README.md が
// クラススコアまで定めており(⑫ は P0 class0 0.90 / P2 class1 0.85 / P3 class2 0.70 が既定閾値を通り、
// P1 は同一クラス NMS で抑制、P4 0.30 は閾値未満)、期待値をそこから独立に書けるため、実装の写し取りにならない。
public sealed class DetectObjectCommandTests
{
    /// <summary>定数出力・転置 4+C=3(C≠80 のため既定のクラス名は class_{id})。</summary>
    private const string ConstantObjectModel = "object_nchw_transposed_4c3.onnx";

    /// <summary>定数出力・転置 4+C=80(既定のクラス名が COCO 名になる)。</summary>
    private const string Coco80ObjectModel = "object_transposed_coco80.onnx";

    /// <summary>定数出力・標準 5+C=3(信頼度 = objectness × 最大クラススコア = 0.72 / 0.60 / 0.42)。</summary>
    private const string StandardObjectModel = "object_nchw_standard_5c3.onnx";

    // 要件 4.1・4.2・4.3・4.5・4.8・6.3・8.1
    [Fact]
    public async Task detect_object_は検出結果を1行のJSONでstdoutに出力し終了コード0で終わる()
    {
        using CliTestHost host = new();
        string image = host.CreateWhiteImage();

        (int exitCode, string stdout, string stderr) = await CliTestHost.RunCliAsync(
            "detect-object", image, "--model", CliTestHost.FixturePath(ConstantObjectModel));

        Assert.Equal(ExitCodes.Success, exitCode);

        // 要件 6.1: 成功時の出力は stdout に限る。
        Assert.Empty(stderr);

        // 要件 6.3: 末尾の改行 1 個以外に改行を含まない。
        string trimmed = stdout.TrimEnd('\r', '\n');
        Assert.NotEqual(stdout, trimmed);
        Assert.DoesNotContain('\n', trimmed);

        // 要件 6.5: image は位置引数の文字列をそのまま出力する。
        JsonElement root = ReadJson(stdout);
        Assert.Equal(image, root.GetProperty("image").GetString());

        // 要件 4.2: トップレベルの配列は objects。
        JsonElement objects = root.GetProperty("objects");
        Assert.Equal(JsonValueKind.Array, objects.ValueKind);

        // 要件 4.8: ライブラリの返却順(信頼度降順)のまま。並べ替えれば順序が変わって落ちる。
        Assert.Equal([0.90f, 0.85f, 0.70f], Confidences(objects));

        // 要件 4.3: classId は候補の argmax(P0=class0 / P2=class1 / P3=class2)。
        Assert.Equal([0, 1, 2], ClassIds(objects));

        // 要件 4.5: --classes 省略時、C=3(≠80)のモデルではライブラリ既定の class_{id} に解決される。
        Assert.Equal(["class_0", "class_1", "class_2"], ClassNames(objects));

        // 要件 4.3: 各要素は bbox(x / y / width / height)を持つ。
        JsonElement bbox = objects[0].GetProperty("bbox");
        Assert.Equal(JsonValueKind.Number, bbox.GetProperty("x").ValueKind);
        Assert.Equal(JsonValueKind.Number, bbox.GetProperty("y").ValueKind);
        Assert.Equal(JsonValueKind.Number, bbox.GetProperty("width").ValueKind);
        Assert.Equal(JsonValueKind.Number, bbox.GetProperty("height").ValueKind);
        Assert.True(bbox.GetProperty("width").GetSingle() > 0f);
    }

    // 要件 4.5: --classes 省略時、80 クラスのモデルではライブラリが COCO 80 クラス名に解決する。
    // Fixtures/README.md ⑭: R0 person(0.95)/ R1 car(0.88)/ R2 cat(0.75)、ClassId は coco.yaml 順で 0 / 2 / 15。
    [Fact]
    public async Task classesを省略すると80クラスのモデルではCOCO名に解決される()
    {
        using CliTestHost host = new();
        string image = host.CreateWhiteImage();

        (int exitCode, string stdout, string stderr) = await CliTestHost.RunCliAsync(
            "detect-object", image, "--model", CliTestHost.FixturePath(Coco80ObjectModel));

        Assert.Equal(ExitCodes.Success, exitCode);
        Assert.Empty(stderr);

        JsonElement objects = ReadJson(stdout).GetProperty("objects");
        Assert.Equal([0.95f, 0.88f, 0.75f], Confidences(objects));
        Assert.Equal([0, 2, 15], ClassIds(objects));
        Assert.Equal(["person", "car", "cat"], ClassNames(objects));
    }

    // 要件 4.7・7.9・8.3: 物体 0 件は空配列 + 終了コード 0(失敗として扱わない)。
    // ⑫ の候補の最大信頼度は 0.90 のため、--confidence 0.99 で全件が閾値未満になる。
    [Fact]
    public async Task 物体が1件も検出されなければ空配列と終了コード0で終わる()
    {
        using CliTestHost host = new();
        string image = host.CreateWhiteImage();

        (int exitCode, string stdout, string stderr) = await CliTestHost.RunCliAsync(
            "detect-object",
            image,
            "--model",
            CliTestHost.FixturePath(ConstantObjectModel),
            "--confidence",
            "0.99");

        Assert.Equal(ExitCodes.Success, exitCode);
        Assert.Empty(stderr);

        JsonElement root = ReadJson(stdout);
        Assert.Equal(image, root.GetProperty("image").GetString());

        JsonElement objects = root.GetProperty("objects");
        Assert.Equal(JsonValueKind.Array, objects.ValueKind);
        Assert.Equal(0, objects.GetArrayLength());
    }

    // 要件 2.3: --confidence の既定値は 0.5(detect-face の 0.7 ではない)。
    // Why not: ⑫(0.90 / 0.85 / 0.70)で既定値を判定しない。誤って 0.7 だった場合、0.70 の候補が閾値と一致し、
    // 通過可否が比較演算子(> か >=)に依存するため、既定値の取り違えを確実に検出できない。
    // ⑬ の合成信頼度は 0.72 / 0.60 / 0.42 で、既定 0.5 なら 2 件・誤って 0.7 なら 1 件と件数が明確に割れる。
    public static TheoryData<string[], float[]> ConfidenceCases => new()
    {
        { [], [0.72f, 0.60f] },
        { ["--confidence", "0.7"], [0.72f] },
        { ["--confidence", "0.3"], [0.72f, 0.60f, 0.42f] },
    };

    [Theory]
    [MemberData(nameof(ConfidenceCases))]
    public async Task confidenceの既定値は0_5で指定値はライブラリに渡る(string[] options, float[] expected)
    {
        using CliTestHost host = new();
        string image = host.CreateWhiteImage();

        (int exitCode, string stdout, string stderr) = await CliTestHost.RunCliAsync(
            ["detect-object", image, "--model", CliTestHost.FixturePath(StandardObjectModel), .. options]);

        Assert.Equal(ExitCodes.Success, exitCode);
        Assert.Empty(stderr);

        // Why not: 信頼度を完全一致で比較しない。⑬ の信頼度は objectness × 最大クラススコアの積であり、
        // float の丸めで 0.72f と厳密には一致しない(⑫・⑭ の生値とは事情が異なる)。
        AssertConfidences(expected, Confidences(ReadJson(stdout).GetProperty("objects")));
    }

    // 要件 2.3: --nms の既定値は 0.5 で、指定値はライブラリまで届く。⑫ の P0(class0, 0.90)と P1(class0, 0.80)は
    // IoU 0.68 のため、既定 0.5 では P1 が同一クラス NMS で抑制されるが、1.0 では抑制されず 4 件になる。
    [Fact]
    public async Task nmsの指定値はライブラリに渡る()
    {
        using CliTestHost host = new();
        string image = host.CreateWhiteImage();

        (int exitCode, string stdout, string stderr) = await CliTestHost.RunCliAsync(
            "detect-object",
            image,
            "--model",
            CliTestHost.FixturePath(ConstantObjectModel),
            "--nms",
            "1.0");

        Assert.Equal(ExitCodes.Success, exitCode);
        Assert.Empty(stderr);
        Assert.Equal([0.90f, 0.85f, 0.80f, 0.70f], Confidences(ReadJson(stdout).GetProperty("objects")));
    }

    private static float[] Confidences(JsonElement objects)
        => [.. objects.EnumerateArray().Select(detected => detected.GetProperty("confidence").GetSingle())];

    private static int[] ClassIds(JsonElement objects)
        => [.. objects.EnumerateArray().Select(detected => detected.GetProperty("classId").GetInt32())];

    private static string[] ClassNames(JsonElement objects)
        => [.. objects.EnumerateArray().Select(detected => detected.GetProperty("className").GetString()!)];

    private static void AssertConfidences(float[] expected, float[] actual)
    {
        Assert.Equal(expected.Length, actual.Length);

        foreach ((float expectedValue, float actualValue) in expected.Zip(actual))
        {
            Assert.Equal(expectedValue, actualValue, precision: 3);
        }
    }

    // Why not: CliJson で逆シリアライズしない。キー名(image / objects / classId / className / confidence / bbox)は
    // 機械可読な契約(要件 4.2・4.3)であり、シリアライズ設定を共有すると命名の退行を素通しする。
    private static JsonElement ReadJson(string stdout)
    {
        using JsonDocument document = JsonDocument.Parse(stdout);

        return document.RootElement.Clone();
    }
}
