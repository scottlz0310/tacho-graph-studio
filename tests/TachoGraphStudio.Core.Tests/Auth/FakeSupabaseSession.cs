using TachoGraphStudio.Core.Auth;

namespace TachoGraphStudio.Core.Tests.Auth;

// PostgREST クライアントのテスト用セッション。GetAccessTokenAsync のたびに
// 連番の token を返し、Invalidate 後の再取得を検証できるようにする
internal sealed class FakeSupabaseSession : ISupabaseSession
{
    private int _issuedCount;

    public FakeSupabaseSession(string apiKey = "test-anon-key")
    {
        ApiKey = apiKey;
    }

    public string ApiKey { get; }

    public List<string> InvalidatedTokens { get; } = [];

    public int IssuedCount => _issuedCount;

    public Task<string> GetAccessTokenAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult($"access-token-{Interlocked.Increment(ref _issuedCount)}");
    }

    public void Invalidate(string accessToken) => InvalidatedTokens.Add(accessToken);
}
