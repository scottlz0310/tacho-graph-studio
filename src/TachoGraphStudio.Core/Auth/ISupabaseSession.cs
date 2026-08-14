namespace TachoGraphStudio.Core.Auth;

// PostgREST 呼び出しに必要な認証情報の供給元(#107)。apikey ヘッダーは Supabase の
// API ゲートウェイが要求するため、JWT とは別に anon キーも保持する
public interface ISupabaseSession
{
    string ApiKey { get; }

    Task<string> GetAccessTokenAsync(CancellationToken cancellationToken = default);

    // 401/403 を受けた access token を破棄し、次回取得で再発行させる
    void Invalidate(string accessToken);
}
