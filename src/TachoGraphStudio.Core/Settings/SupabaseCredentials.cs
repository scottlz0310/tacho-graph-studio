namespace TachoGraphStudio.Core.Settings;

public sealed class SupabaseCredentials
{
    private SupabaseCredentials(Uri projectUrl, string anonKey, string vendorCode, string password)
    {
        ProjectUrl = projectUrl;
        AnonKey = anonKey;
        VendorCode = vendorCode;
        Password = password;
    }

    public Uri ProjectUrl { get; }

    // apikey ヘッダー用。読み取り権限は JWT 側に依存する(#107)
    public string AnonKey { get; }

    public string VendorCode { get; }

    public string Password { get; }

    public static SupabaseCredentials Create(Uri projectUrl, string anonKey, string vendorCode, string password)
    {
        ArgumentNullException.ThrowIfNull(projectUrl);

        if (!projectUrl.IsAbsoluteUri)
        {
            throw new ArgumentException("Supabase project URL は絶対 URI で指定してください。", nameof(projectUrl));
        }

        if (projectUrl.Scheme != Uri.UriSchemeHttps)
        {
            throw new ArgumentException("Supabase project URL は https で指定してください。", nameof(projectUrl));
        }

        if (string.IsNullOrWhiteSpace(anonKey))
        {
            throw new ArgumentException("Supabase anon key を指定してください。", nameof(anonKey));
        }

        if (string.IsNullOrWhiteSpace(vendorCode))
        {
            throw new ArgumentException("業者コードを指定してください。", nameof(vendorCode));
        }

        if (string.IsNullOrWhiteSpace(password))
        {
            throw new ArgumentException("Supabase のパスワードを指定してください。", nameof(password));
        }

        return new SupabaseCredentials(projectUrl, anonKey, vendorCode, password);
    }
}
