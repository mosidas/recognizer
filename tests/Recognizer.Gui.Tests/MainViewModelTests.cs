using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using Recognizer.Gui.Models;
using Recognizer.Gui.Services;
using Recognizer.Gui.ViewModels;

namespace Recognizer.Gui.Tests;

public sealed class MainViewModelTests
{
    // 戻り値・遅延・例外を制御できる小さなテストダブル。
    private sealed class FakeDetectionService : IDetectionService
    {
        public int CallCount { get; private set; }

        public DetectionRequest? LastRequest { get; private set; }

        public Func<DetectionRequest, CancellationToken, Task<DetectionOutcome>> Handler { get; set; } =
            (_, _) => Task.FromResult(DetectionOutcome.Success([], "img"));

        public Task<DetectionOutcome> RunAsync(DetectionRequest request, CancellationToken cancellationToken)
        {
            CallCount++;
            LastRequest = request;
            return Handler(request, cancellationToken);
        }
    }

    private static DetectionOverlay Overlay(string label) =>
        new(new RectangleF(0, 0, 1, 1), 0.9f, label, null);

    // ---- 1.2 既定値 ----

    [Fact]
    public void 起動直後は顔モードで信頼度0_7とNMS0_5を初期表示する()
    {
        MainViewModel vm = new(new FakeDetectionService());

        Assert.Equal(DetectionMode.Face, vm.Mode);
        Assert.Equal(0.7f, vm.ConfidenceThreshold);
        Assert.Equal(0.5f, vm.NmsThreshold);
        Assert.False(vm.IsBusy);
    }

    [Fact]
    public void 物体モードへ切替えると信頼度既定が0_5に追従する()
    {
        MainViewModel vm = new(new FakeDetectionService());

        vm.Mode = DetectionMode.Object;
        Assert.Equal(0.5f, vm.ConfidenceThreshold);

        vm.Mode = DetectionMode.Face;
        Assert.Equal(0.7f, vm.ConfidenceThreshold);
    }

    // ---- 1.3 クラス名有効化 ----

    [Fact]
    public void クラス名指定は物体モードのときのみ有効になる()
    {
        MainViewModel vm = new(new FakeDetectionService());
        Assert.False(vm.IsClassNamesEnabled);

        List<string> changed = [];
        vm.PropertyChanged += (_, e) => changed.Add(e.PropertyName!);

        vm.Mode = DetectionMode.Object;

        Assert.True(vm.IsClassNamesEnabled);
        Assert.Contains(nameof(MainViewModel.IsClassNamesEnabled), changed);
    }

    [Fact]
    public void 入力プロパティ変更でPropertyChangedが発火する()
    {
        MainViewModel vm = new(new FakeDetectionService());
        List<string> changed = [];
        vm.PropertyChanged += (_, e) => changed.Add(e.PropertyName!);

        vm.ModelPath = "model.onnx";

        Assert.Contains(nameof(MainViewModel.ModelPath), changed);
    }

    // ---- 7.1 / 7.3 busy 遷移 ----

    [Fact]
    public async Task 実行の前後でbusyがtrueからfalseへ遷移する()
    {
        TaskCompletionSource<DetectionOutcome> gate = new();
        FakeDetectionService fake = new() { Handler = (_, _) => gate.Task };
        MainViewModel vm = new(fake) { ModelPath = "m", ImagePath = "i" };

        Task run = vm.RunAsync();
        Assert.True(vm.IsBusy);

        gate.SetResult(DetectionOutcome.Success([], "i"));
        await run;

        Assert.False(vm.IsBusy);
    }

    // ---- 7.2 多重実行防止 ----

    [Fact]
    public async Task busy中の再実行はサービスを呼ばない()
    {
        TaskCompletionSource<DetectionOutcome> gate = new();
        FakeDetectionService fake = new() { Handler = (_, _) => gate.Task };
        MainViewModel vm = new(fake) { ModelPath = "m", ImagePath = "i" };

        Task first = vm.RunAsync();
        Assert.True(vm.IsBusy);

        // 2 回目は IsBusy=true のため即座に何もせず返る。
        await vm.RunAsync();
        Assert.Equal(1, fake.CallCount);

        gate.SetResult(DetectionOutcome.Success([], "i"));
        await first;

        Assert.Equal(1, fake.CallCount);
        Assert.False(vm.IsBusy);
    }

    // ---- 8.3 成功時の反映 ----

    [Fact]
    public async Task 成功アウトカムで検出一覧とメッセージを反映する()
    {
        FakeDetectionService fake = new()
        {
            Handler = (_, _) => Task.FromResult(
                DetectionOutcome.Success([Overlay("face #0"), Overlay("face #1")], "i")),
        };
        MainViewModel vm = new(fake) { ModelPath = "m", ImagePath = "i" };

        await vm.RunAsync();

        Assert.Equal(2, vm.Detections.Count);
        Assert.NotNull(vm.StatusMessage);
        Assert.False(vm.IsBusy);
        Assert.Equal(DetectionStatus.Success, vm.LastOutcome!.Status);
    }

    [Fact]
    public async Task 物体モードでのみクラス名パスをリクエストへ渡す()
    {
        FakeDetectionService fake = new();
        MainViewModel vm = new(fake)
        {
            ModelPath = "m",
            ImagePath = "i",
            Mode = DetectionMode.Object,
            ClassNamesPath = "classes.txt",
        };

        await vm.RunAsync();

        Assert.Equal("classes.txt", fake.LastRequest!.ClassNamesPath);
    }

    [Fact]
    public async Task 顔モードではクラス名パスをリクエストへ渡さない()
    {
        FakeDetectionService fake = new();
        MainViewModel vm = new(fake)
        {
            ModelPath = "m",
            ImagePath = "i",
            ClassNamesPath = "classes.txt",
        };

        // 既定は顔モード。
        await vm.RunAsync();

        Assert.Null(fake.LastRequest!.ClassNamesPath);
    }

    // ---- 1.6 画像選択時のクリア ----

    [Fact]
    public async Task 画像を選択すると前回の検出結果をクリアする()
    {
        FakeDetectionService fake = new()
        {
            Handler = (_, _) => Task.FromResult(
                DetectionOutcome.Success([Overlay("face #0")], "i")),
        };
        MainViewModel vm = new(fake) { ModelPath = "m", ImagePath = "i" };
        await vm.RunAsync();
        Assert.Single(vm.Detections);

        vm.SelectImage("j");

        Assert.Equal("j", vm.ImagePath);
        Assert.Empty(vm.Detections);
        Assert.Null(vm.LastOutcome);
        Assert.Null(vm.StatusMessage);
    }

    [Fact]
    public async Task 同一画像を選び直しても前回の検出結果をクリアする()
    {
        FakeDetectionService fake = new()
        {
            Handler = (_, _) => Task.FromResult(
                DetectionOutcome.Success([Overlay("face #0")], "i")),
        };
        MainViewModel vm = new(fake) { ModelPath = "m", ImagePath = "i" };
        await vm.RunAsync();
        Assert.Single(vm.Detections);

        // 同一パスの選び直し(プロパティ変更は発火しない)でもクリアする(要件 1.6)。
        vm.SelectImage("i");

        Assert.Equal("i", vm.ImagePath);
        Assert.Empty(vm.Detections);
        Assert.Null(vm.LastOutcome);
        Assert.Null(vm.StatusMessage);
    }

    // ---- 6.4 失敗時のメッセージ表示 ----

    [Fact]
    public async Task 失敗アウトカムでメッセージを表示しbusyを解除する()
    {
        FakeDetectionService fake = new()
        {
            Handler = (_, _) => Task.FromResult(
                DetectionOutcome.Failure(DetectionStatus.ModelLoadFailed, "モデルを読み込めません。")),
        };
        MainViewModel vm = new(fake) { ModelPath = "m", ImagePath = "i" };

        await vm.RunAsync();

        Assert.Equal("モデルを読み込めません。", vm.StatusMessage);
        Assert.False(vm.IsBusy);
        Assert.Empty(vm.Detections);
    }

    [Fact]
    public async Task 失敗時は直前の検出一覧を破壊しない()
    {
        FakeDetectionService fake = new()
        {
            Handler = (_, _) => Task.FromResult(
                DetectionOutcome.Success([Overlay("face #0")], "i")),
        };
        MainViewModel vm = new(fake) { ModelPath = "m", ImagePath = "i" };
        await vm.RunAsync();
        Assert.Single(vm.Detections);

        // 続く実行が失敗しても、直前の表示状態を保持する(§5.1 事後条件)。
        fake.Handler = (_, _) => Task.FromResult(
            DetectionOutcome.Failure(DetectionStatus.ImageLoadFailed, "画像を読み込めません。"));
        await vm.RunAsync();

        Assert.Single(vm.Detections);
        Assert.Equal("画像を読み込めません。", vm.StatusMessage);
    }

    [Fact]
    public async Task サービスが例外を投げてもViewModelは例外を伝播せずbusyを解除する()
    {
        FakeDetectionService fake = new()
        {
            Handler = (_, _) => throw new InvalidOperationException("想定外"),
        };
        MainViewModel vm = new(fake) { ModelPath = "m", ImagePath = "i" };

        // 例外が伝播しないことを await 自体で検証する(throw されればテスト失敗)。
        await vm.RunAsync();

        Assert.False(vm.IsBusy);
        Assert.NotNull(vm.StatusMessage);
    }

    [Fact]
    public async Task サービスが非同期に例外を投げてもbusyを解除しメッセージを表示する()
    {
        FakeDetectionService fake = new()
        {
            Handler = async (_, _) =>
            {
                await Task.Yield();
                throw new InvalidOperationException("非同期の想定外");
            },
        };
        MainViewModel vm = new(fake) { ModelPath = "m", ImagePath = "i" };

        await vm.RunAsync();

        Assert.False(vm.IsBusy);
        Assert.NotNull(vm.StatusMessage);
    }
}
