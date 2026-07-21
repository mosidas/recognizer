namespace Recognizer.Gui.Models;

/// <summary>検出実行の結果種別。予期されるエラーは例外ではなくこの種別で表す。</summary>
public enum DetectionStatus
{
    Success,
    ModelLoadFailed,
    ImageLoadFailed,
    UnsupportedModel,
    ClassNamesFileFailed,
    Cancelled,
    InvalidInput,
}
