using System.Net;
using System.Text.Json;

namespace TachoGraphStudio.Core.Roster;

// CachedRosterClient と同パターンの業者マスタ用キャッシュフォールバック(#61)
public sealed class CachedVendorClient : IVendorClient
{
    private readonly IVendorCache _cache;
    private readonly IVendorClient _remoteClient;

    public CachedVendorClient(IVendorClient remoteClient, IVendorCache cache)
    {
        ArgumentNullException.ThrowIfNull(remoteClient);
        ArgumentNullException.ThrowIfNull(cache);

        _remoteClient = remoteClient;
        _cache = cache;
    }

    public async Task<VendorResult> GetVendorsAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            VendorResult remoteVendors = await _remoteClient.GetVendorsAsync(cancellationToken);
            await _cache.WriteAsync(remoteVendors, cancellationToken);
            return remoteVendors;
        }
        catch (Exception remoteException) when (IsTransientRemoteFailure(remoteException, cancellationToken))
        {
            try
            {
                VendorResult? cachedVendors = await _cache.ReadAsync(cancellationToken);
                if (cachedVendors is not null)
                {
                    return cachedVendors with { Source = RosterDataSource.Cache };
                }
            }
            catch (Exception cacheException) when (IsCacheFailure(cacheException, cancellationToken))
            {
                throw new VendorUnavailableException(
                    "リモート業者マスタに接続できず、ローカルキャッシュも読み取れませんでした。",
                    new AggregateException(remoteException, cacheException));
            }

            throw new VendorUnavailableException(
                "リモート業者マスタに接続できず、利用可能なローカルキャッシュもありません。",
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
