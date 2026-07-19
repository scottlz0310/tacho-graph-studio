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
    private readonly SupabaseCredentialsValidator _credentialsValidator;
    private readonly HttpClient _httpClient = new();
    private readonly ISecretStore _secretStore;

    public MainWindow()
    {
        InitializeComponent();
        Title = "TachoGraphStudio";

        string localCacheFolderPath = ApplicationData.Current.LocalCacheFolder.Path;
        string localFolderPath = ApplicationData.Current.LocalFolder.Path;

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
    }

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
        TargetDatePicker.Date = new DateTimeOffset(
            StageViewModel.TargetDate.ToDateTime(TimeOnly.MinValue));

        await RosterViewModel.LoadFilterSettingsAsync();
        ApplyFilterSettingsToControls();

        await StageViewModel.LoadTemplatesAsync();
        await RefreshSupabaseConnectionAsync(promptIfUnset: true);
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

    private async void OnOpenTemplateEditorButtonClick(object sender, RoutedEventArgs e)
    {
        // 背景はステージで選択中の円盤(なければプレースホルダー円)。開いた時点の画像で固定する
        TemplateEditor.PreviewBackground = StageViewModel.SelectedDisc?.Preview;
        TemplateEditorViewModel.IsOpen = true;
        await TemplateEditorViewModel.LoadAsync();
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
