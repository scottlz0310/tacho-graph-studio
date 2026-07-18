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
