namespace TachoGraphStudio.Core.Auth;

// 認証情報そのものが受け付けられなかった場合の例外(#107)。ネットワーク不通は
// HttpRequestException のまま伝播させ、キャッシュフォールバックの対象を変えない
public sealed class SupabaseAuthenticationException : Exception
{
    public SupabaseAuthenticationException(string message)
        : base(message)
    {
    }

    public SupabaseAuthenticationException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
