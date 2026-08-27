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
        // 業者コードは login_vendors.code と突合させるため大小文字を保存し、
        // ドメイン部のみ大小文字非依存で照合する。
        string normalized = email.Trim();
        string suffix = $"@{LoginEmailDomain}";
        if (!normalized.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
        {
            vendorCode = string.Empty;
            return false;
        }

        vendorCode = normalized[..^suffix.Length];
        return vendorCode.Length > 0;
    }
}
