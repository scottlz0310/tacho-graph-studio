using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

using TachoGraphStudio.App.Settings;
using TachoGraphStudio.Core.Settings;

using Windows.Storage;

namespace TachoGraphStudio.App;

public sealed partial class MainWindow : Window
{
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
        SupabaseSettingsDialog dialog = new(existingCredentials)
        {
            XamlRoot = Content.XamlRoot,
        };

        ContentDialogResult result = await dialog.ShowAsync();
        if (result == ContentDialogResult.Primary && dialog.Result is not null)
        {
            await _secretStore.WriteAsync(dialog.Result);
            await RefreshSupabaseSettingsStateAsync(promptIfUnset: false);
        }
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
