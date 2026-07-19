using System.Diagnostics;

using CommunityToolkit.Mvvm.ComponentModel;

using TachoGraphStudio.Core.Settings;

namespace TachoGraphStudio.App.Settings;

// アプリ状態(FR-22)の保存を例外安全に行う。失敗は LastError(InfoBar バインド用に
// 変更通知付き)とトレースログへ伝播し、アプリの動作は止めない
// (次回の変更・終了時 flush で再試行される)
public sealed partial class AppStateSaver : ObservableObject
{
    private readonly Action<Action> _notificationDispatcher;
    private readonly IAppStateStore _store;

    // notificationDispatcher: LastError の変更通知(x:Bind → InfoBar の DP 更新)を
    // UI スレッドへ marshal する。終了時 flush はワーカースレッドで実行されるため、
    // 通知を直接発火すると UI 要素のクロススレッド更新でクラッシュする
    public AppStateSaver(IAppStateStore store, Action<Action>? notificationDispatcher = null)
    {
        ArgumentNullException.ThrowIfNull(store);

        _store = store;
        _notificationDispatcher = notificationDispatcher ?? (action => action());
    }

    // 直近の保存失敗理由。成功すると null に戻る。
    // 変更通知は必ず notificationDispatcher 経由で発火する
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasError))]
    public partial string? LastError { get; private set; }

    public bool HasError => LastError is not null;

    public async Task<bool> TrySaveAsync(AppState state, CancellationToken cancellationToken = default)
    {
        try
        {
            await _store.WriteAsync(state, cancellationToken);
            SetLastError(null);
            return true;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            string message = $"アプリ状態の保存に失敗しました: {exception.Message}";
            Trace.WriteLine(message);
            SetLastError(message);
            return false;
        }
    }

    // 終了時の最終保存。UI スレッドの同期コンテキストを避けてスレッドプールで書き切り、
    // fault は TrySaveAsync 内で捕捉、タイムアウトは false として明示的に扱う(throw しない)
    public bool TryFlush(AppState state, TimeSpan timeout)
    {
        Task<bool> save = Task.Run(() => TrySaveAsync(state));
        if (!save.Wait(timeout))
        {
            string message = $"アプリ状態の保存が {timeout.TotalSeconds:0.#} 秒以内に完了しませんでした。";
            Trace.WriteLine(message);
            SetLastError(message);
            return false;
        }

        return save.Result;
    }

    private void SetLastError(string? message) => _notificationDispatcher(() => LastError = message);
}
