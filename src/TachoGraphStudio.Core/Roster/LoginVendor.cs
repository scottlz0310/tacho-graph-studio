using System.Text.Json.Serialization;

namespace TachoGraphStudio.Core.Roster;

// ログイン前に表示する最小限の業者情報。machinery-report-system の
// login_vendors ビューの公開列に対応する。
public sealed record LoginVendor
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

    // display_name は管理外の login_vendors ビュー由来で null / 空文字が混入し得る。
    // 空欄項目では業者を識別できないため、表示は Code へフォールバックする。
    [JsonIgnore]
    public string DisplayLabel =>
        string.IsNullOrWhiteSpace(_displayName) ? Code : _displayName;

    [JsonPropertyName("sort_order")]
    public int SortOrder { get; init; }
}
