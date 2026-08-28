using System.Diagnostics;
using System.Runtime.InteropServices;

using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;

using TachoGraphStudio.App.Imaging;
using TachoGraphStudio.App.Roster;
using TachoGraphStudio.App.Settings;
using TachoGraphStudio.App.Stage;
using TachoGraphStudio.App.Templates;
using TachoGraphStudio.App.Updates;
using TachoGraphStudio.Core.Auth;
using TachoGraphStudio.Core.Imaging;
using TachoGraphStudio.Core.Roster;
using TachoGraphStudio.Core.Settings;
using TachoGraphStudio.Core.Templates;
using TachoGraphStudio.Core.Updates;

using Windows.ApplicationModel;
using Windows.Storage;
using Windows.Storage.Pickers;
using Windows.System;

using WinRT.Interop;

using WinUI.TableView;

namespace TachoGraphStudio.App;

public sealed partial class MainWindow : Window
{
    private readonly IAppStateStore _appStateStore;
    private readonly string _appStatePath;
    private readonly SupabaseCredentialsValidator _credentialsValidator;
    private readonly HttpClient _httpClient = new();
    private readonly ILoginVendorClient _loginVendorClient;
    private readonly ISecretStore _secretStore;
    private readonly WindowPlacementTracker _windowPlacementTracker = new();
    private readonly TaskCompletionSource _initializationCompleted = new(
        TaskCreationOptions.RunContinuationsAsynchronously);

    private bool _isAppStateTrackingEnabled;
    private bool _hasPersistedAppState;
    private string? _lastShownVersion;
    private readonly TemplateSelectionComboBoxController _templateSelectionController;
    private Microsoft.UI.Dispatching.DispatcherQueueTimer? _saveAppStateTimer;
    // 名簿・業者マスタで token を共有するため、接続設定ごとに 1 つだけ保持する(#107)
    private ISupabaseSession? _supabaseSession;

    public MainWindow()
    {
        InitializeComponent();
        Title = "TachoGraphStudio";
        // WinUI 3 は ApplicationIcon で exe に埋め込んだアイコンをウィンドウへ適用しないため、
        // タイトルバー・Alt+Tab 用に明示設定する（#79）
        AppWindow.SetIcon(Path.Combine(AppContext.BaseDirectory, "Assets", "app.ico"));

        string localCacheFolderPath = ApplicationData.Current.LocalCacheFolder.Path;
        string localFolderPath = ApplicationData.Current.LocalFolder.Path;

        _appStatePath = Path.Combine(localFolderPath, "settings", "app-state.json");
        _appStateStore = new JsonAppStateStore(_appStatePath);
        // 変更通知(InfoBar の DP 更新)を UI スレッドへ marshal する。終了時 flush は
        // ワーカースレッドで走るため必須。キュー停止後(シャットダウン中)は通知を破棄する
        AppStateSaver = new AppStateSaver(
            _appStateStore,
            action => DispatcherQueue.TryEnqueue(() => action()));

        _secretStore = new DpapiSecretStore(
            Path.Combine(localCacheFolderPath, "secrets", "supabase.secret.json"));
        _credentialsValidator = new SupabaseCredentialsValidator(_httpClient);
        _loginVendorClient = new PostgRestLoginVendorClient(_httpClient);

        RosterViewModel = new RosterViewModel(
            new JsonRosterFilterSettingsStore(
                Path.Combine(localFolderPath, "settings", "roster-filter.json")));

        FileTemplateStore templateStore = new(Path.Combine(localFolderPath, "templates"));

        StageViewModel = new StageViewModel(
            new StagePipeline(new SheetLoader(new WindowsPdfRasterizer(Program.GetSystemRasterizationScale))),
            new WriteableBitmapImageSourceFactory(),
            templateStore);

        TemplateEditorViewModel = new TemplateEditorViewModel(templateStore);
        TemplateEditor.ViewModel = TemplateEditorViewModel;
        TemplateEditor.HostWindow = this;

        // テンプレート選択 ComboBox の SelectedItem 同期(#43)。VM 駆動の変更と
        // ユーザー操作を切り分けるロジックは WinUI 非依存のコントローラへ切り出し済み
        _templateSelectionController = new TemplateSelectionComboBoxController(
            item => TemplateSelectionComboBox.SelectedItem = item,
            StageViewModel.SelectTemplateForSelectedDisc,
            OpenTemplateEditorAsync);

        // 名簿の行選択・再クリックを選択中円盤のメタデータへ反映する(FR-13)。
        // 選択変更に依存しないため、同じ行を複数の円盤へ続けて適用できる
        RosterViewModel.EntryActivated += (_, entry) => StageViewModel.ApplyRosterEntry(entry);

        // テンプレート編集を閉じたら様式一覧へ反映する(FR-16)
        TemplateEditorViewModel.PropertyChanged += async (_, e) =>
        {
            if (e.PropertyName == nameof(TemplateEditorViewModel.IsOpen)
                && !TemplateEditorViewModel.IsOpen)
            {
                await StageViewModel.LoadTemplatesAsync();
            }
        };

        // OnRootGridLoaded の LoadTemplatesAsync/ApplySavedTemplateSelection より前に
        // 購読しておく必要があるためコンストラクタで行う
        StageViewModel.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(StageViewModel.SelectedTemplate))
            {
                _templateSelectionController.ApplyFromViewModel(StageViewModel.SelectedTemplate);
            }
        };

        // 起動処理(OnRootGridLoaded)の await 中に最大化されても配置を保存できるよう、
        // ウィンドウ表示前(必ず通常表示)の bounds でトラッカーを初期化する
        _windowPlacementTracker.Initialize(IsPresenterRestored(), CurrentWindowBounds());
    }

    public AppStateSaver AppStateSaver { get; }

    public RosterViewModel RosterViewModel { get; }

    public StageViewModel StageViewModel { get; }

    public TemplateEditorViewModel TemplateEditorViewModel { get; }

    private async void OnImportSheetsButtonClick(object sender, RoutedEventArgs e)
    {
        FileOpenPicker picker = new()
        {
            SuggestedStartLocation = PickerLocationId.DocumentsLibrary,
        };
        picker.FileTypeFilter.Add(".pdf");
        picker.FileTypeFilter.Add(".jpg");
        picker.FileTypeFilter.Add(".jpeg");
        InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(this));

        IReadOnlyList<StorageFile> files = await picker.PickMultipleFilesAsync();
        if (files.Count == 0)
        {
            return;
        }

        await StageViewModel.ImportAsync([.. files.Select(file => file.Path)]);
    }

    private void OnPreviewFullscreenRequested(object? sender, EventArgs e)
    {
        StageViewModel.IsPreviewFullscreen = true;
    }

    private void OnCloseFullscreenPreviewClick(object sender, RoutedEventArgs e)
    {
        StageViewModel.IsPreviewFullscreen = false;
    }

    private void OnResetRotationClick(object sender, RoutedEventArgs e)
    {
        StageViewModel.ResetRotation();
    }

    private async void OnSelectOutputDirectoryButtonClick(object sender, RoutedEventArgs e)
    {
        FolderPicker picker = new()
        {
            SuggestedStartLocation = PickerLocationId.PicturesLibrary,
        };
        picker.FileTypeFilter.Add("*");
        InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(this));

        StorageFolder? folder = await picker.PickSingleFolderAsync();
        if (folder is not null)
        {
            // 永続化(前回出力先の復元)は #15
            StageViewModel.OutputDirectory = folder.Path;
        }
    }

    private async void OnSaveAndAdvanceButtonClick(object sender, RoutedEventArgs e)
    {
        await StageViewModel.SaveAndAdvanceAsync();
    }


    private async void OnRootGridLoaded(object sender, RoutedEventArgs e)
    {
        try
        {
            // アプリ状態の復元(FR-22)。読込失敗時は既定値で継続する
            AppState? appState = await TryReadAppStateAsync();
            ApplyAppState(appState);

            TargetDatePicker.Date = new DateTimeOffset(
                StageViewModel.TargetDate.ToDateTime(TimeOnly.MinValue));

            await RosterViewModel.LoadFilterSettingsAsync();
            ApplyFilterSettingsToControls();

            await StageViewModel.LoadTemplatesAsync();
            ApplySavedTemplateSelection(appState?.SelectedTemplateId);

            // 復元が終わってから変更追跡を開始する(復元途中の保存を避ける)
            StartAppStateTracking();

            await RefreshSupabaseConnectionAsync(promptIfUnset: true);
        }
        finally
        {
            _initializationCompleted.TrySetResult();
        }
    }

    private async Task<AppState?> TryReadAppStateAsync()
    {
        _hasPersistedAppState = File.Exists(_appStatePath);

        try
        {
            return await _appStateStore.ReadAsync();
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // 破損・旧バージョン等は既定値で起動する(致命的にしない)
            return null;
        }
    }

    private void ApplyAppState(AppState? state)
    {
        if (state is null)
        {
            return;
        }

        _lastShownVersion = state.LastShownVersion;

        if (state.OutputDirectory is { } outputDirectory && Directory.Exists(outputDirectory))
        {
            StageViewModel.OutputDirectory = outputDirectory;
        }

        if (state.LastTargetDate is { } lastTargetDate)
        {
            StageViewModel.TargetDate = lastTargetDate;
        }

        if (state.ExportDpi is { } exportDpi
            && StageViewModel.ExportDpiOptions.Contains(exportDpi))
        {
            StageViewModel.ExportDpi = exportDpi;
        }

        if (state.ImageProcessing is { } imageProcessing)
        {
            try
            {
                StageViewModel.ProcessingSettings = imageProcessing;
            }
            catch (ArgumentException)
            {
                // 手動編集などで範囲外になった項目は適用せず、既定値で安全に起動する
            }
        }

        if (state.SidebarWidth is { } sidebarWidth && double.IsFinite(sidebarWidth))
        {
            SidebarColumn.Width = new GridLength(
                Math.Clamp(sidebarWidth, SidebarColumn.MinWidth, SidebarColumn.MaxWidth));
        }

        ApplyWindowPlacement(state.Window);
    }

    private void ApplyWindowPlacement(WindowPlacement? placement)
    {
        if (placement is not { Width: > 0, Height: > 0 })
        {
            return;
        }

        Windows.Graphics.RectInt32 bounds = new(
            placement.X, placement.Y, placement.Width, placement.Height);

        // モニタ構成の変更で画面外に復元されないよう、最寄りディスプレイの作業領域へ収める
        Microsoft.UI.Windowing.DisplayArea displayArea = Microsoft.UI.Windowing.DisplayArea
            .GetFromRect(bounds, Microsoft.UI.Windowing.DisplayAreaFallback.Nearest);
        Windows.Graphics.RectInt32 workArea = displayArea.WorkArea;
        int width = Math.Min(bounds.Width, workArea.Width);
        int height = Math.Min(bounds.Height, workArea.Height);
        int x = Math.Clamp(bounds.X, workArea.X, workArea.X + workArea.Width - width);
        int y = Math.Clamp(bounds.Y, workArea.Y, workArea.Y + workArea.Height - height);

        AppWindow.MoveAndResize(new Windows.Graphics.RectInt32(x, y, width, height));
        _windowPlacementTracker.Seed(new Windows.Graphics.RectInt32(x, y, width, height));

        if (placement.IsMaximized
            && AppWindow.Presenter is Microsoft.UI.Windowing.OverlappedPresenter presenter)
        {
            presenter.Maximize();
        }
    }

    internal async Task ShowUpdateNotesIfNeededAsync()
    {
        await _initializationCompleted.Task;

        try
        {
            if (!TryGetCurrentPackageVersion(out Version? currentVersion)
                || currentVersion is null)
            {
                return;
            }

            string currentVersionText = FormatVersion(currentVersion);
            Version? lastShownVersion = UpdateNotesVersionPolicy.ResolveLastShownVersion(
                _lastShownVersion,
                _hasPersistedAppState);
            if (UpdateNotesVersionPolicy.IsNewInstallation(
                    _lastShownVersion,
                    _hasPersistedAppState))
            {
                // 新規インストールでは変更履歴を表示せず、基準バージョンだけ記録する
                _lastShownVersion = currentVersionText;
                await AppStateSaver.TrySaveAsync(CaptureAppState());
                return;
            }

            if (lastShownVersion is not null && lastShownVersion >= currentVersion)
            {
                return;
            }

            string changelogPath = Path.Combine(AppContext.BaseDirectory, "CHANGELOG.md");
            string changelog = await File.ReadAllTextAsync(changelogPath);
            IReadOnlyList<ChangelogSection> sections = ChangelogParser.SelectSections(
                changelog,
                lastShownVersion,
                currentVersion);
            if (sections.Count == 0)
            {
                return;
            }

            UpdateNotesDialog dialog = new(
                sections,
                new Uri(
                    $"https://github.com/scottlz0310/tacho-graph-studio/releases/tag/v{currentVersionText}"));
            dialog.XamlRoot = Content.XamlRoot;
            await dialog.ShowAsync();

            _lastShownVersion = currentVersionText;
            await AppStateSaver.TrySaveAsync(CaptureAppState());
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // 更新通知は装飾機能のため、パッケージ外実行やファイル欠落で起動を止めない
            Trace.WriteLine($"更新内容の表示をスキップしました: {exception.Message}");
        }
    }

    private static bool TryGetCurrentPackageVersion(out Version? version)
    {
        try
        {
            var packageVersion = Package.Current.Id.Version;
            version = new Version(
                packageVersion.Major,
                packageVersion.Minor,
                packageVersion.Build);
            return true;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            Trace.WriteLine($"パッケージバージョンを取得できないため更新内容を表示しません: {exception.Message}");
            version = null;
            return false;
        }
    }

    private static string FormatVersion(Version version) =>
        $"{version.Major}.{version.Minor}.{version.Build}";

    private void ApplySavedTemplateSelection(string? templateId)
    {
        if (templateId is null)
        {
            return;
        }

        StoredTemplate? saved = StageViewModel.Templates
            .FirstOrDefault(stored => stored.Id == templateId);
        if (saved is not null)
        {
            StageViewModel.SelectedTemplate = saved;
        }
    }

    private void StartAppStateTracking()
    {
        if (_isAppStateTrackingEnabled)
        {
            return;
        }

        _isAppStateTrackingEnabled = true;

        _saveAppStateTimer = DispatcherQueue.CreateTimer();
        _saveAppStateTimer.Interval = TimeSpan.FromMilliseconds(500);
        _saveAppStateTimer.IsRepeating = false;
        _saveAppStateTimer.Tick += async (_, _) => await SaveAppStateAsync();

        StageViewModel.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(StageViewModel.OutputDirectory)
                or nameof(StageViewModel.TargetDate)
                or nameof(StageViewModel.ExportDpi)
                or nameof(StageViewModel.ProcessingSettings)
                or nameof(StageViewModel.SelectedTemplate))
            {
                RequestSaveAppState();
            }
        };

        AppWindow.Changed += (_, args) =>
        {
            if (args.DidPositionChange || args.DidSizeChange)
            {
                _windowPlacementTracker.OnBoundsChanged(IsPresenterRestored(), CurrentWindowBounds());
                RequestSaveAppState();
            }
        };

        Closed += OnMainWindowClosed;
    }

    private bool IsPresenterRestored() =>
        AppWindow.Presenter is Microsoft.UI.Windowing.OverlappedPresenter
        {
            State: Microsoft.UI.Windowing.OverlappedPresenterState.Restored,
        };

    private Windows.Graphics.RectInt32 CurrentWindowBounds() => new(
        AppWindow.Position.X,
        AppWindow.Position.Y,
        AppWindow.Size.Width,
        AppWindow.Size.Height);

    // 変更をまとめて書き込むデバウンス(500ms)。終了時は Closed で最終保存する
    private void RequestSaveAppState()
    {
        _saveAppStateTimer?.Stop();
        _saveAppStateTimer?.Start();
    }

    private void OnMainWindowClosed(object sender, WindowEventArgs args)
    {
        _saveAppStateTimer?.Stop();

        // 終了時の最終保存。fault は AppStateSaver 内で捕捉され、タイムアウトも false として
        // 明示的に扱われる(UI スレッドへ throw しない)。失敗理由はトレースログへ伝播する
        AppStateSaver.TryFlush(CaptureAppState(), TimeSpan.FromSeconds(2));
    }

    private Task SaveAppStateAsync() => AppStateSaver.TrySaveAsync(CaptureAppState());

    private AppState CaptureAppState()
    {
        bool isMaximized = AppWindow.Presenter is Microsoft.UI.Windowing.OverlappedPresenter
        {
            State: Microsoft.UI.Windowing.OverlappedPresenterState.Maximized,
        };

        return new AppState
        {
            OutputDirectory = StageViewModel.OutputDirectory,
            LastTargetDate = StageViewModel.TargetDate,
            SelectedTemplateId = StageViewModel.SelectedTemplate?.Id,
            ExportDpi = StageViewModel.ExportDpi,
            ImageProcessing = StageViewModel.ProcessingSettings,
            LastShownVersion = _lastShownVersion,
            SidebarWidth = SidebarColumn.ActualWidth,
            Window = _windowPlacementTracker.Capture(isMaximized),
        };
    }

    // 処理対象日の一括指定(FR-14)。クリア(null)時は表示を直前の日付へ戻し、
    // 表示値と TargetDate が乖離しないようにする(戻す代入で再度 DateChanged が発火するが、
    // 同値のため TargetDate の変更通知は起きない)
    private void OnTargetDatePickerDateChanged(
        CalendarDatePicker sender,
        CalendarDatePickerDateChangedEventArgs args)
    {
        if (args.NewDate is { } date)
        {
            StageViewModel.TargetDate = DateOnly.FromDateTime(date.LocalDateTime);
            return;
        }

        sender.Date = new DateTimeOffset(StageViewModel.TargetDate.ToDateTime(TimeOnly.MinValue));
    }

    private async void OnOpenSettingsButtonClick(object sender, RoutedEventArgs e)
    {
        await OpenSettingsDialogAsync();
    }

    private async void OnReprocessSheetsButtonClick(object sender, RoutedEventArgs e)
    {
        await StageViewModel.ReprocessAsync();
    }

    private async Task OpenTemplateEditorAsync()
    {
        // 背景はステージで選択中の円盤(なければプレースホルダー円)。開いた時点の画像で固定する
        TemplateEditor.PreviewBackground = StageViewModel.SelectedDisc?.Preview;
        TemplateEditorViewModel.IsOpen = true;
        await TemplateEditorViewModel.LoadAsync();
    }

    // テンプレート選択 ComboBox の SelectionChanged(#43)。VM 駆動の変更とユーザー操作の
    // 区別・「テンプレート登録・編集」選択時の revert とエディタ起動は
    // TemplateSelectionComboBoxController(WinUI 非依存、ユニットテスト対象)に委譲する
    private async void OnTemplateSelectionComboBoxSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (sender is ComboBox comboBox)
        {
            await _templateSelectionController.OnSelectionChangedAsync(
                comboBox.SelectedItem, StageViewModel.SelectedTemplate);
        }
    }

    private async void OnRosterRetryButtonClick(object sender, RoutedEventArgs e)
    {
        await RosterViewModel.RefreshAsync();
    }

    // 行のダブルクリックで名簿を再適用できるようにする(FR-13)。行スコープのイベントを使い、
    // ヘッダー・空白部の操作では発火させない(手修正 FR-15 を上書きしないため)
    private void OnRosterDataGridRowDoubleTapped(object sender, TableViewRowDoubleTappedEventArgs e)
    {
        RosterViewModel.ActivateEntry(e.Item);
    }

    private void OnSeasonComboBoxSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (SeasonComboBox.SelectedItem is ComboBoxItem { Tag: string tag }
            && Enum.TryParse(tag, out RosterSeason season))
        {
            RosterViewModel.Season = season;
        }
    }

    private void OnTachoTargetsOnlyCheckBoxCheckedChanged(object sender, RoutedEventArgs e)
    {
        RosterViewModel.TachoTargetsOnly = TachoTargetsOnlyCheckBox.IsChecked ?? false;
    }

    private async void OnControlNumberJumpTextBoxKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key != VirtualKey.Enter)
        {
            return;
        }

        e.Handled = true;
        RosterViewModel.JumpToControlNumber();

        RosterEntry? selectedEntry = RosterViewModel.SelectedEntry;
        if (selectedEntry is null)
        {
            return;
        }

        int index = RosterViewModel.Entries.IndexOf(selectedEntry);
        if (index >= 0)
        {
            await RosterDataGrid.ScrollRowIntoView(index);
        }
    }

    private void ApplyFilterSettingsToControls()
    {
        string seasonTag = RosterViewModel.Season.ToString();
        foreach (object item in SeasonComboBox.Items)
        {
            if (item is ComboBoxItem { Tag: string itemTag } comboBoxItem
                && itemTag == seasonTag)
            {
                SeasonComboBox.SelectedItem = comboBoxItem;
                break;
            }
        }

        TachoTargetsOnlyCheckBox.IsChecked = RosterViewModel.TachoTargetsOnly;
    }

    private async Task RefreshSupabaseConnectionAsync(bool promptIfUnset)
    {
        (SupabaseCredentials? credentials, bool isInvalid) = await TryReadCredentialsAsync();

        RosterViewModel.IsCredentialsInvalid = isInvalid;

        _supabaseSession = null;

        if (credentials is null)
        {
            RosterViewModel.SetRosterClient(null);

            if (promptIfUnset)
            {
                await OpenSettingsDialogAsync(selectSupabaseSection: true);
            }

            return;
        }

        _supabaseSession = new SupabasePasswordSession(
            _httpClient,
            credentials.ProjectUrl,
            credentials.AnonKey,
            credentials.VendorCode,
            credentials.Password);

        RosterViewModel.SetRosterClient(
            BuildRosterClient(credentials.ProjectUrl, _supabaseSession),
            BuildVendorClient(credentials.ProjectUrl, _supabaseSession));
        await RosterViewModel.RefreshAsync();
    }

    private IRosterClient BuildRosterClient(Uri projectUrl, ISupabaseSession session)
    {
        PostgRestRosterClient remoteClient = new(_httpClient, projectUrl, session);
        JsonRosterCache cache = new(
            Path.Combine(
                ApplicationData.Current.LocalCacheFolder.Path,
                "roster",
                "roster-cache.json"));

        return new CachedRosterClient(remoteClient, cache);
    }

    private IVendorClient BuildVendorClient(Uri projectUrl, ISupabaseSession session)
    {
        PostgRestVendorClient remoteClient = new(_httpClient, projectUrl, session);
        JsonVendorCache cache = new(
            Path.Combine(
                ApplicationData.Current.LocalCacheFolder.Path,
                "roster",
                "vendor-cache.json"));

        return new CachedVendorClient(remoteClient, cache);
    }

    private async Task OpenSettingsDialogAsync(bool selectSupabaseSection = false)
    {
        (SupabaseCredentials? existingCredentials, _) = await TryReadCredentialsAsync();
        SupabaseSettingsDialog dialog = new(
            existingCredentials,
            StageViewModel.ProcessingSettings,
            _credentialsValidator,
            _loginVendorClient,
            selectSupabaseSection);

        // 設定ダイアログは独立した Window のため、hit test の抑止だけではタイトルバー(閉じる)が
        // 生き残り、表示中に親を閉じられる。EnableWindow で親をモーダル相当に無効化し、
        // 設定反映前の状態 flush と破棄済み Window への Activate() を防ぐ
        nint ownerHandle = WindowNative.GetWindowHandle(this);
        bool accepted;
        EnableWindow(ownerHandle, false);
        try
        {
            accepted = await dialog.ShowAsync(ownerHandle);
        }
        finally
        {
            EnableWindow(ownerHandle, true);
            Activate();
        }

        if (!accepted || dialog.ImageProcessingResult is null)
        {
            return;
        }

        StageViewModel.ProcessingSettings = dialog.ImageProcessingResult;

        if (dialog.Result is null)
        {
            return;
        }

        bool saved = await _secretStore.TryWriteAsync(dialog.Result);
        if (!saved)
        {
            await ShowSaveFailedDialogAsync();
            return;
        }

        await RefreshSupabaseConnectionAsync(promptIfUnset: false);
    }

    private async Task ShowSaveFailedDialogAsync()
    {
        ContentDialog errorDialog = new()
        {
            XamlRoot = Content.XamlRoot,
            Title = "設定の保存に失敗しました",
            Content = "Supabase 接続設定をローカルに保存できませんでした。ディスク容量や権限をご確認のうえ、"
                + "再度お試しください。名簿以外の機能は引き続き利用できます。",
            CloseButtonText = "閉じる",
        };
        await errorDialog.ShowAsync();
    }

    private async Task<(SupabaseCredentials? Credentials, bool IsInvalid)> TryReadCredentialsAsync()
    {
        try
        {
            return (await _secretStore.ReadAsync(), false);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return (null, true);
        }
    }

    // LibraryImport は AllowUnsafeBlocks をプロジェクト全体で要求する（SYSLIB1062）。
    // blittable なハンドル・BOOL だけを扱うため、この 1 箇所のために unsafe は解禁しない
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnableWindow(nint windowHandle, [MarshalAs(UnmanagedType.Bool)] bool enable);
}
