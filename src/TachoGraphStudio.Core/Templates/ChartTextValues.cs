namespace TachoGraphStudio.Core.Templates;

// 文字入れの入力値セット(FR-13〜15)。DateText は「2026/12/25」のような手修正可能な文字列で、
// 区切り文字で year / month / day に分解される(和暦等の非数値表記もそのまま通す)
public sealed record ChartTextValues
{
    public string DateText { get; init; } = "";

    public string RegistrationNumber { get; init; } = "";

    public string Driver { get; init; } = "";

    public string VehicleType { get; init; } = "";
}
