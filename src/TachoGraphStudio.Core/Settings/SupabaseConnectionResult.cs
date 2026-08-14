namespace TachoGraphStudio.Core.Settings;

// 接続確認の結果(#107)。認証失敗とネットワーク不通を利用者へ区別して伝えるため、
// 成否だけでなく表示用メッセージを返す
public readonly record struct SupabaseConnectionResult(bool IsValid, string? ErrorMessage)
{
    public static SupabaseConnectionResult Success => new(true, null);

    public static SupabaseConnectionResult Failed(string errorMessage) => new(false, errorMessage);
}
