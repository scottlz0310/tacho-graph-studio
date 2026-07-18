using CommunityToolkit.Mvvm.ComponentModel;

using Microsoft.UI.Xaml.Media;

namespace TachoGraphStudio.App.Stage;

// サムネイルナビ 1 枠分のワークアイテム(FR-04)
public sealed partial class DiscWorkItem : ObservableObject
{
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StatusLabel))]
    public partial DiscStatus Status { get; set; } = DiscStatus.Pending;

    [ObservableProperty]
    public partial ImageSource? Thumbnail { get; set; }

    [ObservableProperty]
    public partial ImageSource? Preview { get; set; }

    private double _rotationAngle;

    // 回転補正角(度、-180〜+180)。プレビューでは非破壊レイヤー合成、確定保存(issue #14)で本合成する(FR-07)
    public double RotationAngle
    {
        get => _rotationAngle;
        set
        {
            // NumberBox は空入力で NaN を書き込む。非有限値は直前の有効値を保持し、
            // 変更通知だけ発行して UI 側の表示を有効値へ巻き戻す
            if (!double.IsFinite(value))
            {
                OnPropertyChanged(nameof(RotationAngle));
                return;
            }

            SetProperty(ref _rotationAngle, Math.Clamp(value, -180.0, 180.0));
        }
    }

    public DiscWorkItem(int number, ProcessedDisc disc)
    {
        ArgumentNullException.ThrowIfNull(disc);

        Number = number;
        Disc = disc;
    }

    // 表示用の 1 始まり連番(No.1〜)
    public int Number { get; }

    public ProcessedDisc Disc { get; }

    public string Label => $"No.{Number}";

    public string StatusLabel => Status switch
    {
        DiscStatus.Done => "処理済み",
        DiscStatus.Skipped => "スキップ",
        _ => "未処理",
    };
}
