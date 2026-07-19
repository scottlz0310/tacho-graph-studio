using CommunityToolkit.Mvvm.ComponentModel;

using TachoGraphStudio.Core.Templates;

namespace TachoGraphStudio.App.Stage;

// 円盤 1 枚分の文字入れメタデータ(FR-13〜15, FR-17)。円盤ごとに保持し、
// サムネイルで前の円盤に戻っても入力内容が残る
public sealed partial class DiscMetadata : ObservableObject
{
    [ObservableProperty]
    public partial string PrintDate { get; set; } = "";

    [ObservableProperty]
    public partial string RegistrationNumber { get; set; } = "";

    [ObservableProperty]
    public partial string DriverName { get; set; } = "";

    // テンプレートの vehicle_type フィールド用。名簿反映でのみ設定される(エディタ UI には出さない)
    [ObservableProperty]
    public partial string VehicleType { get; set; } = "";

    // 手書きのため自動文字入れをスキップ(FR-17)。ファイル名の運転者部の「手書き」化は #14
    [ObservableProperty]
    public partial bool SkipHandwritten { get; set; }

    // 選択中のチャート紙様式テンプレート ID(円盤ごと、FR-16、#43)。
    // ITemplateStore の ID を参照する。未選択時は null
    [ObservableProperty]
    public partial string? SelectedTemplateId { get; set; }

    public ChartTextValues ToTextValues() => new()
    {
        DateText = PrintDate,
        RegistrationNumber = RegistrationNumber,
        Driver = DriverName,
        VehicleType = VehicleType,
    };
}
