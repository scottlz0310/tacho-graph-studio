namespace TachoGraphStudio.Core.Settings;

public sealed class SupabaseCredentials
{
    private SupabaseCredentials(Uri projectUrl, string anonKey, string email, string password)
    {
        ProjectUrl = projectUrl;
        AnonKey = anonKey;
        Email = email;
        Password = password;
    }

    public Uri ProjectUrl { get; }

    // apikey ヘッダー用。読み取り権限は JWT 側に依存する(#107)
    public string AnonKey { get; }

    public string Email { get; }

    public string Password { get; }

    public static SupabaseCredentials Create(Uri projectUrl, string anonKey, string email, string password)
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

        if (string.IsNullOrWhiteSpace(email))
        {
            throw new ArgumentException("Supabase のメールアドレスを指定してください。", nameof(email));
        }

        if (string.IsNullOrWhiteSpace(password))
        {
            throw new ArgumentException("Supabase のパスワードを指定してください。", nameof(password));
        }

        return new SupabaseCredentials(projectUrl, anonKey, email, password);
    }
}
