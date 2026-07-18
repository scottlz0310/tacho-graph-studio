using System.Collections.ObjectModel;
using System.Text.Json;

using CommunityToolkit.Mvvm.ComponentModel;

using TachoGraphStudio.Core.Roster;

namespace TachoGraphStudio.App.Roster;

public sealed partial class RosterViewModel : ObservableObject
{
    private readonly IRosterFilterSettingsStore _filterSettingsStore;

    private IReadOnlyList<RosterEntry> _allEntries = [];
    private bool _isInitializingFilterSettings;
    private IRosterClient? _rosterClient;

    public RosterViewModel(IRosterFilterSettingsStore filterSettingsStore)
    {
        ArgumentNullException.ThrowIfNull(filterSettingsStore);

        _filterSettingsStore = filterSettingsStore;
    }

    public ObservableCollection<RosterEntry> Entries { get; } = [];

    [ObservableProperty]
    public partial string ControlNumberJumpText { get; set; } = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DataSourceLabel))]
    public partial RosterDataSource? DataSource { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsErrorStateVisible))]
    [NotifyPropertyChangedFor(nameof(IsGridVisible))]
    public partial string? ErrorMessage { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasFilterSettingsWarning))]
    public partial string? FilterSettingsWarningMessage { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsErrorStateVisible))]
    [NotifyPropertyChangedFor(nameof(IsGridVisible))]
    [NotifyPropertyChangedFor(nameof(IsNotConfiguredStateVisible))]
    public partial bool IsCredentialsConfigured { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(NotConfiguredTitle))]
    public partial bool IsCredentialsInvalid { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsErrorStateVisible))]
    [NotifyPropertyChangedFor(nameof(IsGridVisible))]
    public partial bool IsLoading { get; set; }

    [ObservableProperty]
    public partial RosterSeason Season { get; set; } = RosterSeason.All;

    [ObservableProperty]
    public partial string SearchKeyword { get; set; } = string.Empty;

    [ObservableProperty]
    public partial RosterEntry? SelectedEntry { get; set; }

    [ObservableProperty]
    public partial bool TachoTargetsOnly { get; set; } = RosterFilterSettings.Default.TachoTargetsOnly;

    public string DataSourceLabel => DataSource switch
    {
        RosterDataSource.Remote => "最新の名簿を取得しました。",
        RosterDataSource.Cache => "オフラインキャッシュを表示しています(最新の取得に失敗しました)。",
        _ => string.Empty,
    };

    public string NotConfiguredTitle => IsCredentialsInvalid
        ? "Supabase 接続設定が無効です"
        : "Supabase 未接続";

    public bool HasFilterSettingsWarning => FilterSettingsWarningMessage is not null;

    public bool IsNotConfiguredStateVisible => !IsCredentialsConfigured;

    public bool IsErrorStateVisible => IsCredentialsConfigured && !IsLoading && ErrorMessage is not null;

    public bool IsGridVisible => IsCredentialsConfigured && !IsLoading && ErrorMessage is null;

    public async Task LoadFilterSettingsAsync(CancellationToken cancellationToken = default)
    {
        RosterFilterSettings? savedSettings;
        try
        {
            savedSettings = await _filterSettingsStore.ReadAsync(cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            FilterSettingsWarningMessage = "名簿フィルタ設定の読み込みに失敗しました。既定のフィルタで続行します。";
            return;
        }

        if (savedSettings is null)
        {
            return;
        }

        // Season/TachoTargetsOnly の setter が走らせるフィルタ再適用・永続化を初期読み込み時は抑止する
        _isInitializingFilterSettings = true;
        try
        {
            Season = savedSettings.Season;
            TachoTargetsOnly = savedSettings.TachoTargetsOnly;
        }
        finally
        {
            _isInitializingFilterSettings = false;
        }
    }

    public void SetRosterClient(IRosterClient? rosterClient)
    {
        _rosterClient = rosterClient;
        IsCredentialsConfigured = rosterClient is not null;

        if (rosterClient is not null)
        {
            return;
        }

        _allEntries = [];
        Entries.Clear();
        ErrorMessage = null;
        DataSource = null;
        SelectedEntry = null;
    }

    public async Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        if (_rosterClient is null)
        {
            return;
        }

        IsLoading = true;
        ErrorMessage = null;

        try
        {
            RosterResult result = await _rosterClient.GetRosterAsync(cancellationToken);
            _allEntries = result.Entries;
            DataSource = result.Source;
            ApplyFilter();
        }
        catch (RosterUnavailableException exception)
        {
            ErrorMessage = exception.Message;
        }
        catch (HttpRequestException)
        {
            ErrorMessage = "Supabase への接続に失敗しました。接続設定(URL・anon キー)を確認してください。";
        }
        catch (JsonException)
        {
            ErrorMessage = "名簿データの形式が不正です。Supabase 側の machine_picklist ビューを確認してください。";
        }
        catch (InvalidDataException)
        {
            ErrorMessage = "名簿データの形式が不正です。Supabase 側の machine_picklist ビューを確認してください。";
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            ErrorMessage = "名簿キャッシュの書き込みに失敗しました。ディスク容量や権限を確認してください。";
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            ErrorMessage = "名簿の取得に失敗しました。";
        }
        finally
        {
            IsLoading = false;
        }
    }

    public void JumpToControlNumber()
    {
        RosterEntry? match = RosterFilter.FindByControlNumberPrefix(Entries, ControlNumberJumpText);
        if (match is not null)
        {
            SelectedEntry = match;
        }
    }

    partial void OnSearchKeywordChanged(string value) => ApplyFilter();

    partial void OnSeasonChanged(RosterSeason value)
    {
        if (!_isInitializingFilterSettings)
        {
            ApplyFilterAndPersistSettings();
        }
    }

    partial void OnTachoTargetsOnlyChanged(bool value)
    {
        if (!_isInitializingFilterSettings)
        {
            ApplyFilterAndPersistSettings();
        }
    }

    private void ApplyFilter()
    {
        RosterFilterSettings settings = new() { Season = Season, TachoTargetsOnly = TachoTargetsOnly };
        IReadOnlyList<RosterEntry> filteredEntries = RosterFilter.Apply(_allEntries, settings, SearchKeyword);

        Entries.Clear();
        foreach (RosterEntry entry in filteredEntries)
        {
            Entries.Add(entry);
        }
    }

    private async void ApplyFilterAndPersistSettings()
    {
        ApplyFilter();

        try
        {
            RosterFilterSettings settings = new() { Season = Season, TachoTargetsOnly = TachoTargetsOnly };
            await _filterSettingsStore.WriteAsync(settings);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            FilterSettingsWarningMessage = "名簿フィルタ設定の保存に失敗しました。";
        }
    }
}
