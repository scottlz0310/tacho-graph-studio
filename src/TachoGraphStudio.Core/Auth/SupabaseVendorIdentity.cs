namespace TachoGraphStudio.Core.Auth;

// machinery-report-system と共通の業者共有アカウント規則。メールアドレスは利用者に
// 入力させず、業者コードから Supabase Auth のログイン識別子を組み立てる。
public static class SupabaseVendorIdentity
{
    public const string LoginEmailDomain = "zama-sys.internal";

    public static string GetLoginEmail(string vendorCode)
    {
        if (string.IsNullOrWhiteSpace(vendorCode))
        {
            throw new ArgumentException("業者コードを指定してください。", nameof(vendorCode));
        }

        return $"{vendorCode}@{LoginEmailDomain}";
    }

    public static bool TryGetVendorCode(string email, out string vendorCode)
    {
        string normalized = email.Trim().ToLowerInvariant();
        string suffix = $"@{LoginEmailDomain}";
        if (!normalized.EndsWith(suffix, StringComparison.Ordinal))
        {
            vendorCode = string.Empty;
            return false;
        }

        vendorCode = normalized[..^suffix.Length];
        return vendorCode.Length > 0;
    }
}
