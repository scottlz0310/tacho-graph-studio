namespace TachoGraphStudio.Core.Settings;

public sealed class SupabaseCredentials
{
    private SupabaseCredentials(Uri projectUrl, string anonKey)
    {
        ProjectUrl = projectUrl;
        AnonKey = anonKey;
    }

    public Uri ProjectUrl { get; }

    public string AnonKey { get; }

    public static SupabaseCredentials Create(Uri projectUrl, string anonKey)
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

        return new SupabaseCredentials(projectUrl, anonKey);
    }
}
