using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

using TachoGraphStudio.App.Settings;
using TachoGraphStudio.Core.Settings;

using Windows.Storage;

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

        string secretsFilePath = Path.Combine(
            ApplicationData.Current.LocalCacheFolder.Path,
            "secrets",
            "supabase.secret.json");
        _secretStore = new DpapiSecretStore(secretsFilePath);
        _credentialsValidator = new SupabaseCredentialsValidator(_httpClient);
    }

    private async void OnRootGridLoaded(object sender, RoutedEventArgs e)
    {
        await RefreshSupabaseSettingsStateAsync(promptIfUnset: true);
    }

    private async void OnOpenSettingsButtonClick(object sender, RoutedEventArgs e)
    {
        await OpenSettingsDialogAsync();
    }

    private async Task RefreshSupabaseSettingsStateAsync(bool promptIfUnset)
    {
        (SupabaseCredentials? credentials, bool isInvalid) = await TryReadCredentialsAsync();

        SupabaseSettingsInfoBar.Title = isInvalid
            ? "Supabase 接続設定が無効です"
            : "Supabase 名簿連携が未設定です";
        SupabaseSettingsInfoBar.IsOpen = credentials is null;

        if (credentials is null && promptIfUnset)
        {
            await OpenSettingsDialogAsync();
        }
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

        await RefreshSupabaseSettingsStateAsync(promptIfUnset: false);
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
