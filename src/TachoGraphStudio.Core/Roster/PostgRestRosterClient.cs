using TachoGraphStudio.Core.Auth;

namespace TachoGraphStudio.Core.Roster;

public sealed class PostgRestRosterClient : IRosterClient
{
    private const string SelectColumns =
        "ctrl_num,detail,spec,vehicle_num,vehicle_type,driver,work_period,updated_at,is_tacho_target";

    private readonly PostgRestReader _reader;
    private readonly Uri _requestUri;
    private readonly TimeProvider _timeProvider;

    public PostgRestRosterClient(
        HttpClient httpClient,
        Uri projectUrl,
        ISupabaseSession session,
        TimeProvider? timeProvider = null)
    {
        ArgumentNullException.ThrowIfNull(projectUrl);

        if (!projectUrl.IsAbsoluteUri)
        {
            throw new ArgumentException("Supabase project URL は絶対 URI で指定してください。", nameof(projectUrl));
        }

        _reader = new PostgRestReader(httpClient, session);
        _timeProvider = timeProvider ?? TimeProvider.System;
        _requestUri = BuildRequestUri(projectUrl);
    }

    public async Task<RosterResult> GetRosterAsync(CancellationToken cancellationToken = default)
    {
        List<RosterEntry> entries = await _reader.ReadArrayAsync<RosterEntry>(
            _requestUri,
            "名簿",
            cancellationToken);

        return new RosterResult(entries, RosterDataSource.Remote, _timeProvider.GetUtcNow());
    }

    private static Uri BuildRequestUri(Uri projectUrl)
    {
        Uri baseUri = new(projectUrl.AbsoluteUri.TrimEnd('/') + "/", UriKind.Absolute);
        return new Uri(
            baseUri,
            $"rest/v1/machine_picklist?select={SelectColumns}&order=ctrl_num.asc");
    }
}
