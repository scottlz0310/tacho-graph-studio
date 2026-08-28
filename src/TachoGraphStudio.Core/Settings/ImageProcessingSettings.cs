using TachoGraphStudio.Core.Imaging;

namespace TachoGraphStudio.Core.Settings;

// スキャナやチャート紙に応じて利用者が調整する画像処理設定(FR-03)。
// 既定値は Core の処理オプションと共有し、設定未保存時の挙動を従来と一致させる
public sealed record ImageProcessingSettings
{
    public static ImageProcessingSettings Default { get; } = new();

    public int Threshold { get; init; } = DiscSplitOptions.DefaultThreshold;

    public int PaddingPx { get; init; } = DiscSplitOptions.DefaultPaddingPx;

    public int EllipsePaddingPx { get; init; }

    public void Validate() => DiscSplitOptions.Validate(ToSplitOptions());

    public DiscSplitOptions ToSplitOptions(double? dpi = null) => new()
    {
        Threshold = Threshold,
        PaddingPx = PaddingPx,
        Dpi = dpi,
    };

    public BackgroundRemovalOptions ToBackgroundRemovalOptions() => new()
    {
        EllipsePaddingPx = EllipsePaddingPx,
    };
}
