using Recognizer.Gui.Models;

namespace Recognizer.Gui.Services;

/// <summary>
/// ViewModel からコアライブラリの検出を呼ぶ境界。予期されるエラーは例外で伝播させず
/// <see cref="DetectionOutcome"/> の <see cref="DetectionStatus"/> に写して返す(spec §5.2)。
/// </summary>
public interface IDetectionService
{
    /// <summary>
    /// <paramref name="request"/> に従い顔 / 物体検出を UI スレッド外で実行し、結果を返す。
    /// </summary>
    Task<DetectionOutcome> RunAsync(DetectionRequest request, CancellationToken cancellationToken);
}
