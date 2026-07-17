using System.Net;
using System.Text.Json;

namespace TachoGraphStudio.Core.Roster;

public sealed class CachedRosterClient : IRosterClient
{
    private readonly IRosterCache _cache;
    private readonly IRosterClient _remoteClient;

    public CachedRosterClient(IRosterClient remoteClient, IRosterCache cache)
    {
        ArgumentNullException.ThrowIfNull(remoteClient);
        ArgumentNullException.ThrowIfNull(cache);

        _remoteClient = remoteClient;
        _cache = cache;
    }

    public async Task<RosterResult> GetRosterAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            RosterResult remoteRoster = await _remoteClient.GetRosterAsync(cancellationToken);
            await _cache.WriteAsync(remoteRoster, cancellationToken);
            return remoteRoster;
        }
        catch (Exception remoteException) when (IsTransientRemoteFailure(remoteException, cancellationToken))
        {
            try
            {
                RosterResult? cachedRoster = await _cache.ReadAsync(cancellationToken);
                if (cachedRoster is not null)
                {
                    return cachedRoster with { Source = RosterDataSource.Cache };
                }
            }
            catch (Exception cacheException) when (IsCacheFailure(cacheException, cancellationToken))
            {
                throw new RosterUnavailableException(
                    "リモート名簿に接続できず、ローカルキャッシュも読み取れませんでした。",
                    new AggregateException(remoteException, cacheException));
            }

            throw new RosterUnavailableException(
                "リモート名簿に接続できず、利用可能なローカルキャッシュもありません。",
                remoteException);
        }
    }

    private static bool IsTransientRemoteFailure(Exception exception, CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested)
        {
            return false;
        }

        if (exception is OperationCanceledException or TimeoutException)
        {
            return true;
        }

        return exception is HttpRequestException httpRequestException
            && (httpRequestException.StatusCode is null
                || httpRequestException.StatusCode is HttpStatusCode.RequestTimeout
                || (int)httpRequestException.StatusCode >= 500);
    }

    private static bool IsCacheFailure(Exception exception, CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested)
        {
            return false;
        }

        return exception is IOException
            or JsonException
            or UnauthorizedAccessException;
    }
}
