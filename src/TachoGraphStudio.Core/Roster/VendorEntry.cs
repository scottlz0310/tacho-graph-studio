using System.Text.Json.Serialization;

namespace TachoGraphStudio.Core.Roster;

// ctrl_num の閲覧フィルター用範囲(両端含む)。vendor_ctrl_num_ranges の purpose='view' 行に対応
public readonly record struct CtrlNumRange(
    [property: JsonPropertyName("min_ctrl_num")] long MinCtrlNum,
    [property: JsonPropertyName("max_ctrl_num")] long MaxCtrlNum);

// Supabase vendors テーブルの業者(#61)。業者名・範囲はアプリ側にハードコードせず
// 必ずテーブル参照とする(将来の業者追加・範囲変更に耐えるため)
public sealed record VendorEntry
{
    private string _displayName = string.Empty;

    [JsonPropertyName("code")]
    public required string Code { get; init; }

    [JsonPropertyName("display_name")]
    public string DisplayName
    {
        get => _displayName;
        init => _displayName = value ?? string.Empty;
    }

    // purpose='view' の範囲のみ(共通ゾーン＋自社範囲)。admin のように範囲行を持たない
    // 業者は空になり、フィルター選択肢の対象外となる
    [JsonPropertyName("ranges")]
    public IReadOnlyList<CtrlNumRange> ViewRanges { get; init; } = [];
}
