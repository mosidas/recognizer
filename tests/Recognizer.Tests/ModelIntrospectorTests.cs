using System.Linq;
using Microsoft.ML.OnnxRuntime;
using Recognizer.Internal;

namespace Recognizer.Tests;

/// <summary>
/// ModelIntrospector の形式判別テスト。テスト名末尾の (a)〜(f) は design.md §6 の判別規則番号に対応する。
/// </summary>
public sealed class ModelIntrospectorTests
{
    private static string FixturePath(string fileName)
        => Path.Combine(AppContext.BaseDirectory, "Fixtures", fileName);

    private static InferenceSession OpenFixture(string fileName)
        => new(FixturePath(fileName));

    // 規則 (a)(b): NCHW・静的 640x640 入力の判別と入出力名の取得(要件 2.1)
    [Fact]
    public void Introspect_NCHW入力640と入出力名を取得する_規則ab()
    {
        using InferenceSession session = OpenFixture("face_nchw_transposed_f5.onnx");

        DetectionModelSpec spec = ModelIntrospector.Introspect(session);

        Assert.Equal(TensorLayout.Nchw, spec.Layout);
        Assert.Equal(640, spec.InputWidth);
        Assert.Equal(640, spec.InputHeight);
        Assert.Equal("images", spec.InputName);
        Assert.Equal("output", spec.OutputName);
    }

    // 規則 (a): チャネル軸が末尾にある NHWC の判別(要件 2.1)
    [Fact]
    public void Introspect_NHWC入力を判別する_規則a()
    {
        using InferenceSession session = OpenFixture("face_nhwc_transposed_f5.onnx");

        DetectionModelSpec spec = ModelIntrospector.Introspect(session);

        Assert.Equal(TensorLayout.Nhwc, spec.Layout);
        Assert.Equal(640, spec.InputWidth);
        Assert.Equal(640, spec.InputHeight);
    }

    // 規則 (c): H/W が動的軸(-1)なら 640x640 を既定にする(要件 2.2)
    [Fact]
    public void Introspect_動的軸入力は640既定を用いる_規則c()
    {
        using InferenceSession session = OpenFixture("face_dynamic_input_f5.onnx");

        DetectionModelSpec spec = ModelIntrospector.Introspect(session);

        Assert.Equal(TensorLayout.Nchw, spec.Layout);
        Assert.Equal(640, spec.InputWidth);
        Assert.Equal(640, spec.InputHeight);
    }

    // 規則 (d): 転置 [1,F,N] の F=5 判別(要件 2.3)
    [Fact]
    public void ClassifyOutput_転置形式F5を判別する_規則d()
    {
        OutputSpec spec = ModelIntrospector.ClassifyOutput([1, 5, 6]);

        Assert.Equal(OutputFormat.Transposed, spec.Format);
        Assert.Equal(5, spec.FeatureCount);
        Assert.Equal(6, spec.CandidateCount);
    }

    // 規則 (d): 転置 [1,F,N] の F=20 判別(要件 2.3)
    [Fact]
    public void ClassifyOutput_転置形式F20を判別する_規則d()
    {
        OutputSpec spec = ModelIntrospector.ClassifyOutput([1, 20, 6]);

        Assert.Equal(OutputFormat.Transposed, spec.Format);
        Assert.Equal(20, spec.FeatureCount);
        Assert.Equal(6, spec.CandidateCount);
    }

    // 規則 (d): 標準 [1,N,F] の F=5 判別(要件 2.3)
    [Fact]
    public void ClassifyOutput_標準形式F5を判別する_規則d()
    {
        OutputSpec spec = ModelIntrospector.ClassifyOutput([1, 6, 5]);

        Assert.Equal(OutputFormat.Standard, spec.Format);
        Assert.Equal(5, spec.FeatureCount);
        Assert.Equal(6, spec.CandidateCount);
    }

    // 規則 (d): d1/d2 双方が {5,20} に一致する場合は転置を優先する(要件 2.3)
    [Fact]
    public void ClassifyOutput_両次元一致は転置を優先する_規則d()
    {
        OutputSpec spec = ModelIntrospector.ClassifyOutput([1, 5, 5]);

        Assert.Equal(OutputFormat.Transposed, spec.Format);
        Assert.Equal(5, spec.FeatureCount);
    }

    // 規則 (d): fixture の実出力形状から形式と F を判別する(要件 2.3)
    // 内部 enum OutputFormat を public シグネチャに露出しないよう期待形式は bool で受ける。
    [Theory]
    [InlineData("face_nchw_transposed_f5.onnx", true, 5)]
    [InlineData("face_nchw_transposed_f20.onnx", true, 20)]
    [InlineData("face_nchw_standard_f5.onnx", false, 5)]
    public void ClassifyOutput_Fixture出力形状を判別する_規則d(string file, bool expectedTransposed, int expectedFeature)
    {
        using InferenceSession session = OpenFixture(file);
        int[] shape = session.OutputMetadata["output"].Dimensions;

        OutputSpec spec = ModelIntrospector.ClassifyOutput(shape);

        Assert.Equal(expectedTransposed ? OutputFormat.Transposed : OutputFormat.Standard, spec.Format);
        Assert.Equal(expectedFeature, spec.FeatureCount);
    }

    // 規則 (d): F が {5,20} 以外なら NotSupportedException(要件 2.6)
    [Fact]
    public void ClassifyOutput_F7は非対応で例外_規則d()
    {
        _ = Assert.Throws<NotSupportedException>(() => ModelIntrospector.ClassifyOutput([1, 7, 6]));
    }

    // 規則 (f): 非対応形式(F=7)のモデルは構築時に早期検出して NotSupportedException(要件 2.6)
    [Fact]
    public void Introspect_非対応F7モデルは例外_規則f()
    {
        using InferenceSession session = OpenFixture("face_unsupported_f7.onnx");

        _ = Assert.Throws<NotSupportedException>(() => ModelIntrospector.Introspect(session));
    }

    // 規則 (f): 出力が複数(YOLOv3 系)はスコープ外で非対応(要件 2.6)
    [Fact]
    public void Introspect_複数出力は非対応_規則f()
    {
        using InferenceSession session = OpenFixture("face_multi_output.onnx");

        _ = Assert.Throws<NotSupportedException>(() => ModelIntrospector.Introspect(session));
    }

    // 規則 (a): 入力が複数はレイアウト判別不能で非対応(要件 2.6)
    [Fact]
    public void Introspect_複数入力は非対応_規則a()
    {
        using InferenceSession session = OpenFixture("face_multi_input.onnx");

        _ = Assert.Throws<NotSupportedException>(() => ModelIntrospector.Introspect(session));
    }

    // 規則 (a): 入力 rank が 4 以外は非対応(要件 2.6)
    [Fact]
    public void Introspect_入力rank4以外は非対応_規則a()
    {
        using InferenceSession session = OpenFixture("face_rank3_input.onnx");

        _ = Assert.Throws<NotSupportedException>(() => ModelIntrospector.Introspect(session));
    }

    // 規則 (a): チャネル軸(値 3)を特定できない入力は非対応(要件 2.6)
    [Fact]
    public void Introspect_チャネル軸不明は非対応_規則a()
    {
        using InferenceSession session = OpenFixture("face_channel_unknown.onnx");

        _ = Assert.Throws<NotSupportedException>(() => ModelIntrospector.Introspect(session));
    }

    // 規則 (a): 入力が NCHW/NHWC 両形に一致する病的形状([1,3,3,3])は NCHW を優先する
    [Fact]
    public void Introspect_両形一致はNCHWを優先する_規則a()
    {
        using InferenceSession session = OpenFixture("face_tiny_ambiguous_3x3.onnx");

        DetectionModelSpec spec = ModelIntrospector.Introspect(session);

        Assert.Equal(TensorLayout.Nchw, spec.Layout);
    }

    // 規則 (d): 出力 rank が 3 以外は非対応(純粋関数レベル。要件 2.6)
    [Theory]
    [InlineData(new[] { 1, 5 })]        // rank 2
    [InlineData(new[] { 1, 5, 6, 2 })]  // rank 4
    public void ClassifyOutput_出力rank3以外は非対応_規則d(int[] shape)
    {
        _ = Assert.Throws<NotSupportedException>(() => ModelIntrospector.ClassifyOutput(shape));
    }

    // 規則 (d): 先頭次元が 1 以外は非対応(純粋関数レベル。要件 2.6)
    [Fact]
    public void ClassifyOutput_先頭次元が1以外は非対応_規則d()
    {
        _ = Assert.Throws<NotSupportedException>(() => ModelIntrospector.ClassifyOutput([2, 5, 6]));
    }

    // --- 物体検出用: ClassifyObjectOutput(規則 o-d〜o-f。要件 2.3, 2.8)---

    // 規則 (o-d)(o-e): 転置 [1,F,N] は 4+C・objectness なし(要件 2.3)
    [Fact]
    public void ClassifyObjectOutput_転置4C3を判別する_規則ode()
    {
        ObjectOutputSpec spec = ModelIntrospector.ClassifyObjectOutput([1, 7, 60]);

        Assert.Equal(OutputFormat.Transposed, spec.Format);
        Assert.Equal(7, spec.FeatureCount);
        Assert.Equal(60, spec.CandidateCount);
        Assert.Equal(3, spec.ClassCount);
        Assert.False(spec.HasObjectness);
    }

    // 規則 (o-d)(o-e): 標準 [1,N,F] は 5+C・objectness あり(要件 2.3)
    [Fact]
    public void ClassifyObjectOutput_標準5C3を判別する_規則ode()
    {
        ObjectOutputSpec spec = ModelIntrospector.ClassifyObjectOutput([1, 60, 8]);

        Assert.Equal(OutputFormat.Standard, spec.Format);
        Assert.Equal(8, spec.FeatureCount);
        Assert.Equal(60, spec.CandidateCount);
        Assert.Equal(3, spec.ClassCount);
        Assert.True(spec.HasObjectness);
    }

    // 規則 (o-d)(o-e): 転置 COCO 80 クラス(要件 2.3)
    [Fact]
    public void ClassifyObjectOutput_転置COCO80を判別する_規則ode()
    {
        ObjectOutputSpec spec = ModelIntrospector.ClassifyObjectOutput([1, 84, 100]);

        Assert.Equal(OutputFormat.Transposed, spec.Format);
        Assert.Equal(84, spec.FeatureCount);
        Assert.Equal(80, spec.ClassCount);
        Assert.False(spec.HasObjectness);
    }

    // 規則 (o-d): d1 = d2 のとき転置形式を優先する(要件 2.3)
    [Fact]
    public void ClassifyObjectOutput_両次元一致は転置を優先する_規則od()
    {
        ObjectOutputSpec spec = ModelIntrospector.ClassifyObjectOutput([1, 10, 10]);

        Assert.Equal(OutputFormat.Transposed, spec.Format);
        Assert.Equal(10, spec.FeatureCount);
    }

    // 規則 (o-f): 転置 F=4(C=0)は非対応(要件 2.8)
    [Fact]
    public void ClassifyObjectOutput_転置F4は非対応_規則of()
    {
        _ = Assert.Throws<NotSupportedException>(() => ModelIntrospector.ClassifyObjectOutput([1, 4, 60]));
    }

    // 規則 (o-f): 標準 F=5(C=0)は非対応。小さい方 d2=5 が F の境界(要件 2.8)
    [Fact]
    public void ClassifyObjectOutput_標準F5は非対応_規則of()
    {
        _ = Assert.Throws<NotSupportedException>(() => ModelIntrospector.ClassifyObjectOutput([1, 60, 5]));
    }

    // 規則 (o-f): 出力 rank が 3 以外は非対応(要件 2.8)
    [Theory]
    [InlineData(new[] { 1, 7 })]         // rank 2
    [InlineData(new[] { 1, 7, 60, 2 })]  // rank 4
    public void ClassifyObjectOutput_出力rank3以外は非対応_規則of(int[] shape)
    {
        _ = Assert.Throws<NotSupportedException>(() => ModelIntrospector.ClassifyObjectOutput(shape));
    }

    // 規則 (o-f): 先頭次元が 1 以外は非対応(要件 2.8)
    [Fact]
    public void ClassifyObjectOutput_先頭次元が1以外は非対応_規則of()
    {
        _ = Assert.Throws<NotSupportedException>(() => ModelIntrospector.ClassifyObjectOutput([2, 7, 60]));
    }

    // 規則 (o-e): 顔形状 [1,5,60] は物体としては転置 F=5・C=1 で有効。{5,20} を特別扱いしない(要件 2.3)
    [Fact]
    public void ClassifyObjectOutput_顔形状も物体として有効_規則oe()
    {
        ObjectOutputSpec spec = ModelIntrospector.ClassifyObjectOutput([1, 5, 60]);

        Assert.Equal(OutputFormat.Transposed, spec.Format);
        Assert.Equal(5, spec.FeatureCount);
        Assert.Equal(1, spec.ClassCount);
        Assert.False(spec.HasObjectness);
    }

    // --- 物体検出用: IntrospectObject(規則 a〜c 共通 + o-d〜o-f。要件 2.1, 2.8)---

    // 規則 (a)(b): 物体モデルの NCHW・640・入出力名を取得する(要件 2.1)
    [Fact]
    public void IntrospectObject_NCHW入力640と入出力名を取得する_規則ab()
    {
        using InferenceSession session = OpenFixture("object_nchw_transposed_4c3.onnx");

        DetectionModelSpec spec = ModelIntrospector.IntrospectObject(session);

        Assert.Equal(TensorLayout.Nchw, spec.Layout);
        Assert.Equal(640, spec.InputWidth);
        Assert.Equal(640, spec.InputHeight);
        Assert.Equal("images", spec.InputName);
        Assert.Equal("output", spec.OutputName);
    }

    // 規則 (o-f): 非対応形式(F=4)モデルは構築時に早期検出して例外(要件 2.8)
    [Fact]
    public void IntrospectObject_非対応F4モデルは例外_規則of()
    {
        using InferenceSession session = OpenFixture("object_unsupported_f4.onnx");

        _ = Assert.Throws<NotSupportedException>(() => ModelIntrospector.IntrospectObject(session));
    }

    // 規則 (o-f): 出力が複数はスコープ外で非対応(要件 2.8)
    [Fact]
    public void IntrospectObject_複数出力は非対応_規則of()
    {
        using InferenceSession session = OpenFixture("face_multi_output.onnx");

        _ = Assert.Throws<NotSupportedException>(() => ModelIntrospector.IntrospectObject(session));
    }

    // 規則 (a): 入力が複数はレイアウト判別不能で非対応(要件 2.8)
    [Fact]
    public void IntrospectObject_複数入力は非対応_規則a()
    {
        using InferenceSession session = OpenFixture("face_multi_input.onnx");

        _ = Assert.Throws<NotSupportedException>(() => ModelIntrospector.IntrospectObject(session));
    }

    // 規則 (a): 入力 rank が 4 以外は非対応(要件 2.8)
    [Fact]
    public void IntrospectObject_入力rank4以外は非対応_規則a()
    {
        using InferenceSession session = OpenFixture("face_rank3_input.onnx");

        _ = Assert.Throws<NotSupportedException>(() => ModelIntrospector.IntrospectObject(session));
    }

    // 規則 (a): チャネル軸(値 3)を特定できない入力は非対応(要件 2.8)
    [Fact]
    public void IntrospectObject_チャネル軸不明は非対応_規則a()
    {
        using InferenceSession session = OpenFixture("face_channel_unknown.onnx");

        _ = Assert.Throws<NotSupportedException>(() => ModelIntrospector.IntrospectObject(session));
    }

    // 規則 (o-g): 出力 N 軸が動的なモデルは構築時の分類を保留し、例外を投げず DetectionModelSpec を返す(要件 2.1, 2.8)
    [Fact]
    public void IntrospectObject_動的出力軸は分類を保留して返す_規則og()
    {
        using InferenceSession session = OpenFixture("object_dynamic_output_4c3.onnx");

        // 前提: 出力 N 軸が動的(≤ 0)であること。ここが静的だと (o-g) の保留分岐を実行できない。
        int[] declaredOutput = session.OutputMetadata["output"].Dimensions;
        Assert.Contains(declaredOutput, d => d <= 0);

        DetectionModelSpec spec = ModelIntrospector.IntrospectObject(session);

        Assert.Equal(TensorLayout.Nchw, spec.Layout);
        Assert.Equal(640, spec.InputWidth);
        Assert.Equal(640, spec.InputHeight);
        Assert.Equal("images", spec.InputName);
        Assert.Equal("output", spec.OutputName);
    }

    // 規則 (o-g) 後段: 動的出力モデルの初回 Run 実形状 [1,7,60] を ClassifyObjectOutput に渡すと転置・C=3(要件 2.3)
    [Fact]
    public void IntrospectObject_動的出力の実形状は転置C3に分類される_規則og()
    {
        using InferenceSession session = OpenFixture("object_dynamic_output_4c3.onnx");
        float[] input = new float[1 * 3 * 640 * 640];

        using IDisposableReadOnlyCollection<DisposableNamedOnnxValue> results = session.Run(
            [NamedOnnxValue.CreateFromTensor(
                "images",
                new Microsoft.ML.OnnxRuntime.Tensors.DenseTensor<float>(input, [1, 3, 640, 640]))]);
        int[] actualShape = [.. results.First().AsTensor<float>().Dimensions];

        ObjectOutputSpec objectSpec = ModelIntrospector.ClassifyObjectOutput(actualShape);

        Assert.Equal([1, 7, 60], actualShape);
        Assert.Equal(OutputFormat.Transposed, objectSpec.Format);
        Assert.Equal(3, objectSpec.ClassCount);
        Assert.False(objectSpec.HasObjectness);
    }
}
