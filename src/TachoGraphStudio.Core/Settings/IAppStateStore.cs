namespace TachoGraphStudio.Core.Settings;

// アプリ状態(FR-22)の永続化。未保存時は Read が null を返す
public interface IAppStateStore
{
    Task<AppState?> ReadAsync(CancellationToken cancellationToken = default);

    Task WriteAsync(AppState state, CancellationToken cancellationToken = default);
}
