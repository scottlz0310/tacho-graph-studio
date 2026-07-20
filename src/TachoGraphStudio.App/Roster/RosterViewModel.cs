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
    // 保存済みの業者コード。業者一覧のロード完了時(UpdateVendorOptions)に選択へ反映する
    private string? _pendingVendorCode;
    private IRosterClient? _rosterClient;
    private IVendorClient? _vendorClient;

    public RosterViewModel(IRosterFilterSettingsStore filterSettingsStore)
    {
        ArgumentNullException.ThrowIfNull(filterSettingsStore);

        _filterSettingsStore = filterSettingsStore;
    }

    // 名簿行の「適用」通知(FR-13)。選択変更に加え、行のダブルクリック(ActivateEntry)でも
    // 発火し、同じ行を複数の円盤へ続けて適用できる
    public event EventHandler<RosterEntry>? EntryActivated;

    public ObservableCollection<RosterEntry> Entries { get; } = [];

    // 業者フィルターの選択肢(#61)。先頭は常に「全て」で、業者一覧のロード成否に
    // かかわらず選択可能な状態を維持する
    public ObservableCollection<VendorOption> VendorOptions { get; } = [VendorOption.All];

    // 行由来の item のみ適用する。ヘッダー・空白部など名簿行以外からの操作では発火せず、
    // 手修正(FR-15)を意図せず名簿値へ戻さない
    public void ActivateEntry(object? item)
    {
        if (item is RosterEntry entry)
        {
            EntryActivated?.Invoke(this, entry);
        }
    }

    partial void OnSelectedEntryChanged(RosterEntry? value)
    {
        if (value is not null)
        {
            EntryActivated?.Invoke(this, value);
        }
    }

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
    public partial VendorOption? SelectedVendorOption { get; set; } = VendorOption.All;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasVendorWarning))]
    public partial string? VendorWarningMessage { get; set; }

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

    public bool HasVendorWarning => VendorWarningMessage is not null;

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
            _pendingVendorCode = savedSettings.VendorCode;
        }
        finally
        {
            _isInitializingFilterSettings = false;
        }
    }

    public void SetRosterClient(IRosterClient? rosterClient, IVendorClient? vendorClient = null)
    {
        _rosterClient = rosterClient;
        _vendorClient = vendorClient;
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

        // 業者一覧を先に取得する(保存済みの業者選択の復元とフィルタ適用に必要)。
        // 取得失敗しても名簿自体は利用できるため、警告表示のうえ「全て」のみで続行する
        await RefreshVendorOptionsAsync(cancellationToken);

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

    private async Task RefreshVendorOptionsAsync(CancellationToken cancellationToken)
    {
        if (_vendorClient is null)
        {
            return;
        }

        IReadOnlyList<VendorEntry> vendors;
        try
        {
            VendorResult result = await _vendorClient.GetVendorsAsync(cancellationToken);
            vendors = result.Vendors;
            VendorWarningMessage = null;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            vendors = [];
            VendorWarningMessage = "業者一覧の取得に失敗しました。業者フィルターなしで続行します。";
        }

        UpdateVendorOptions(vendors);
    }

    private void UpdateVendorOptions(IReadOnlyList<VendorEntry> vendors)
    {
        string? targetCode = _pendingVendorCode ?? SelectedVendorOption?.Code;
        _pendingVendorCode = null;

        VendorOptions.Clear();
        VendorOptions.Add(VendorOption.All);
        foreach (VendorEntry vendor in vendors)
        {
            // 閲覧用範囲を持たない業者(admin 等)は絞り込みに使えないため選択肢から除外する
            if (vendor.ViewRanges.Count > 0)
            {
                VendorOptions.Add(new VendorOption(vendor.Code, vendor.DisplayName, vendor.ViewRanges));
            }
        }

        // 保存済みの業者が一覧から消えていた場合は「全て」へフォールバックする。
        // 選択の同期はロード処理の一部なので、setter 由来の永続化は抑止する
        _isInitializingFilterSettings = true;
        try
        {
            SelectedVendorOption =
                VendorOptions.FirstOrDefault(option => option.Code == targetCode) ?? VendorOption.All;
        }
        finally
        {
            _isInitializingFilterSettings = false;
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

    partial void OnSelectedVendorOptionChanged(VendorOption? value)
    {
        if (!_isInitializingFilterSettings)
        {
            ApplyFilterAndPersistSettings();
        }
    }

    private void ApplyFilter()
    {
        IReadOnlyList<RosterEntry> filteredEntries = RosterFilter.Apply(
            _allEntries,
            BuildFilterSettings(),
            SearchKeyword,
            SelectedVendorOption?.ViewRanges);

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
            await _filterSettingsStore.WriteAsync(BuildFilterSettings());
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            FilterSettingsWarningMessage = "名簿フィルタ設定の保存に失敗しました。";
        }
    }

    private RosterFilterSettings BuildFilterSettings() => new()
    {
        Season = Season,
        TachoTargetsOnly = TachoTargetsOnly,
        VendorCode = SelectedVendorOption?.Code,
    };
}
