using System;
using System.Collections.ObjectModel;
using System.Threading;
using System.Threading.Tasks;
using Recognizer.Gui.Models;
using Recognizer.Gui.Services;

namespace Recognizer.Gui.ViewModels;

/// <summary>
/// メインウィンドウの状態と検出実行を集約する。入力値・busy・直近アウトカム・
/// 表示メッセージを保持し、idle → running → 完了/失敗 → idle の遷移を担う。
/// </summary>
public sealed class MainViewModel : ViewModelBase
{
    private readonly IDetectionService _detectionService;

    private string _modelPath = string.Empty;
    private string _imagePath = string.Empty;
    private DetectionMode _mode = DetectionMode.Face;
    private float _confidenceThreshold = DefaultConfidence(DetectionMode.Face);
    private float _nmsThreshold = 0.5f;
    private string? _classNamesPath;
    private bool _isBusy;
    private string? _statusMessage;
    private DetectionOutcome? _lastOutcome;

    public MainViewModel(IDetectionService detectionService)
    {
        ArgumentNullException.ThrowIfNull(detectionService);
        _detectionService = detectionService;
    }

    /// <summary>実サービスで動く既定の構築。DI 未使用の合成ルート向け。</summary>
    public MainViewModel()
        : this(new DetectionService())
    {
    }

    public string ModelPath
    {
        get => _modelPath;
        set => SetProperty(ref _modelPath, value);
    }

    public string ImagePath
    {
        get => _imagePath;
        set => SetProperty(ref _imagePath, value);
    }

    public DetectionMode Mode
    {
        get => _mode;
        set
        {
            if (SetProperty(ref _mode, value))
            {
                OnPropertyChanged(nameof(IsClassNamesEnabled));
                // Why: 信頼度の既定はモード依存(顔 0.7 / 物体 0.5)。モード切替で既定へ追従させ、
                // 起動直後だけでなく切替後も各モードの推奨既定を提示する。
                ConfidenceThreshold = DefaultConfidence(value);
            }
        }
    }

    public float ConfidenceThreshold
    {
        get => _confidenceThreshold;
        set => SetProperty(ref _confidenceThreshold, value);
    }

    public float NmsThreshold
    {
        get => _nmsThreshold;
        set => SetProperty(ref _nmsThreshold, value);
    }

    public string? ClassNamesPath
    {
        get => _classNamesPath;
        set => SetProperty(ref _classNamesPath, value);
    }

    /// <summary>物体モードのときのみクラス名指定が有効。</summary>
    public bool IsClassNamesEnabled => Mode == DetectionMode.Object;

    /// <summary>実行中フラグ。true の間は再実行を抑止する。</summary>
    public bool IsBusy
    {
        get => _isBusy;
        private set => SetProperty(ref _isBusy, value);
    }

    /// <summary>成功/失敗の人間可読な状態メッセージ(日本語)。</summary>
    public string? StatusMessage
    {
        get => _statusMessage;
        private set => SetProperty(ref _statusMessage, value);
    }

    /// <summary>直近の検出アウトカム。</summary>
    public DetectionOutcome? LastOutcome
    {
        get => _lastOutcome;
        private set => SetProperty(ref _lastOutcome, value);
    }

    /// <summary>検出結果の一覧(信頼度・ラベルの表示元)。</summary>
    public ObservableCollection<DetectionOverlay> Detections { get; } = [];

    /// <summary>
    /// 入力画像の選択を反映する。前回の検出結果は選択後の画像とは無関係なため、
    /// 同一ファイルの選び直しを含め、選択のたびに検出結果と実行由来の状態をクリアする(要件 1.6)。
    /// </summary>
    public void SelectImage(string path)
    {
        ImagePath = path;
        Detections.Clear();
        LastOutcome = null;
        StatusMessage = null;
    }

    /// <summary>
    /// 検出を実行する。busy 中は新たな実行を開始しない(多重実行防止)。
    /// いかなる場合も未処理例外を投げず、失敗は状態へ反映して busy を解除する。
    /// </summary>
    public async Task RunAsync(CancellationToken cancellationToken = default)
    {
        if (IsBusy)
        {
            // Why: 実行中の再入は新たな検出を開始しない(多重実行防止)。
            return;
        }

        IsBusy = true;
        try
        {
            DetectionRequest request = new(
                Mode,
                ModelPath,
                ImagePath,
                ConfidenceThreshold,
                NmsThreshold,
                // Why: クラス名は物体モードのみ意味を持つ。顔モードでは無視する。
                Mode == DetectionMode.Object ? ClassNamesPath : null);

            // Why not ConfigureAwait(false): 完了後の状態更新は UI スレッドで行う必要があり、
            // ViewModel のコマンド継続は同期コンテキストへ戻す(MVVM の慣習)。
            DetectionOutcome outcome = await _detectionService.RunAsync(request, cancellationToken);
            ApplyOutcome(outcome);
        }
        catch (OperationCanceledException)
        {
            // Why: サービスは Cancelled を結果型で返す契約だが、防御的に握って状態を復帰する。
            StatusMessage = "検出をキャンセルしました。";
        }
        catch (Exception ex)
        {
            // Why: サービスは例外を投げない契約。想定外例外もアプリを終了させず状態へ反映する。
            StatusMessage = $"検出中に予期しないエラーが発生しました: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void ApplyOutcome(DetectionOutcome outcome)
    {
        LastOutcome = outcome;

        if (outcome.Status == DetectionStatus.Success)
        {
            Detections.Clear();
            foreach (DetectionOverlay detection in outcome.Detections)
            {
                Detections.Add(detection);
            }

            StatusMessage = $"検出が完了しました({outcome.Detections.Count} 件)。";
            return;
        }

        // Why: 失敗時は直前の表示状態(Detections)を破壊せず、メッセージのみ差し替える(§5.1 事後条件)。
        StatusMessage = outcome.Message;
    }

    private static float DefaultConfidence(DetectionMode mode) =>
        mode == DetectionMode.Face ? 0.7f : 0.5f;
}
