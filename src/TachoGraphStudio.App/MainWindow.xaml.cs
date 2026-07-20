using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;

using TachoGraphStudio.App.Imaging;
using TachoGraphStudio.App.Roster;
using TachoGraphStudio.App.Settings;
using TachoGraphStudio.App.Stage;
using TachoGraphStudio.App.Templates;
using TachoGraphStudio.Core.Imaging;
using TachoGraphStudio.Core.Roster;
using TachoGraphStudio.Core.Settings;
using TachoGraphStudio.Core.Templates;

using Windows.Storage;
using Windows.Storage.Pickers;
using Windows.System;

using WinRT.Interop;

using WinUI.TableView;

namespace TachoGraphStudio.App;

public sealed partial class MainWindow : Window
{
    private readonly IAppStateStore _appStateStore;
    private readonly SupabaseCredentialsValidator _credentialsValidator;
    private readonly HttpClient _httpClient = new();
    private readonly ISecretStore _secretStore;
    private readonly WindowPlacementTracker _windowPlacementTracker = new();

    private bool _isAppStateTrackingEnabled;
    private readonly TemplateSelectionComboBoxController _templateSelectionController;
    private Microsoft.UI.Dispatching.DispatcherQueueTimer? _saveAppStateTimer;

    public MainWindow()
    {
        InitializeComponent();
        Title = "TachoGraphStudio";

        string localCacheFolderPath = ApplicationData.Current.LocalCacheFolder.Path;
        string localFolderPath = ApplicationData.Current.LocalFolder.Path;

        _appStateStore = new JsonAppStateStore(
            Path.Combine(localFolderPath, "settings", "app-state.json"));
        // 変更通知(InfoBar の DP 更新)を UI スレッドへ marshal する。終了時 flush は
        // ワーカースレッドで走るため必須。キュー停止後(シャットダウン中)は通知を破棄する
        AppStateSaver = new AppStateSaver(
            _appStateStore,
            action => DispatcherQueue.TryEnqueue(() => action()));

        _secretStore = new DpapiSecretStore(
            Path.Combine(localCacheFolderPath, "secrets", "supabase.secret.json"));
        _credentialsValidator = new SupabaseCredentialsValidator(_httpClient);

        RosterViewModel = new RosterViewModel(
            new JsonRosterFilterSettingsStore(
                Path.Combine(localFolderPath, "settings", "roster-filter.json")));

        FileTemplateStore templateStore = new(Path.Combine(localFolderPath, "templates"));

        StageViewModel = new StageViewModel(
            new StagePipeline(new SheetLoader(new WindowsPdfRasterizer())),
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

    private async Task<AppState?> TryReadAppStateAsync()
    {
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

        if (state.OutputDirectory is { } outputDirectory && Directory.Exists(outputDirectory))
        {
            StageViewModel.OutputDirectory = outputDirectory;
        }

        if (state.LastTargetDate is { } lastTargetDate)
        {
            StageViewModel.TargetDate = lastTargetDate;
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

        if (credentials is null)
        {
            RosterViewModel.SetRosterClient(null);

            if (promptIfUnset)
            {
                await OpenSettingsDialogAsync();
            }

            return;
        }

        RosterViewModel.SetRosterClient(BuildRosterClient(credentials));
        await RosterViewModel.RefreshAsync();
    }

    private IRosterClient BuildRosterClient(SupabaseCredentials credentials)
    {
        PostgRestRosterClient remoteClient = new(_httpClient, credentials.ProjectUrl, credentials.AnonKey);
        JsonRosterCache cache = new(
            Path.Combine(
                ApplicationData.Current.LocalCacheFolder.Path,
                "roster",
                "roster-cache.json"));

        return new CachedRosterClient(remoteClient, cache);
    }

    private async Task OpenSettingsDialogAsync()
    {
        (SupabaseCredentials? existingCredentials, _) = await TryReadCredentialsAsync();
        SupabaseSettingsDialog dialog = new(existingCredentials, _credentialsValidator)
        {
            XamlRoot = Content.XamlRoot,
        };

        ContentDialogResult result = await dialog.ShowAsync();
        if (result != ContentDialogResult.Primary || dialog.Result is null)
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
}
