using System.Text.Json;

using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

using TachoGraphStudio.Core.Auth;
using TachoGraphStudio.Core.Roster;
using TachoGraphStudio.Core.Settings;

namespace TachoGraphStudio.App.Settings;

public sealed partial class SupabaseSettingsDialog : ContentDialog
{
    private readonly SupabaseCredentialsValidator _credentialsValidator;
    private readonly ILoginVendorClient _loginVendorClient;
    private string? _loadedAnonKey;
    private string? _loadedProjectUrl;
    private readonly string? _pendingVendorCode;

    public SupabaseSettingsDialog(
        SupabaseCredentials? existingCredentials,
        SupabaseCredentialsValidator credentialsValidator,
        ILoginVendorClient loginVendorClient)
    {
        InitializeComponent();
        _credentialsValidator = credentialsValidator;
        _loginVendorClient = loginVendorClient;
        Loaded += OnLoaded;

        if (existingCredentials is not null)
        {
            ProjectUrlTextBox.Text = existingCredentials.ProjectUrl.AbsoluteUri;
            AnonKeyPasswordBox.Password = existingCredentials.AnonKey;
            _pendingVendorCode = existingCredentials.VendorCode;
            PasswordBox.Password = existingCredentials.Password;
        }
    }

    public SupabaseCredentials? Result { get; private set; }

    private async void OnLoaded(object sender, RoutedEventArgs args)
    {
        Loaded -= OnLoaded;

        if (HasConnectionInputs())
        {
            await LoadVendorsAsync();
        }
    }

    private async void OnLoadVendorsButtonClick(object sender, RoutedEventArgs args)
    {
        await LoadVendorsAsync();
    }

    private void OnProjectUrlTextChanged(object sender, TextChangedEventArgs args)
    {
        ClearLoadedVendors();
    }

    private void OnAnonKeyPasswordChanged(object sender, RoutedEventArgs args)
    {
        ClearLoadedVendors();
    }

    private async void OnPrimaryButtonClick(ContentDialog sender, ContentDialogButtonClickEventArgs args)
    {
        ContentDialogButtonClickDeferral deferral = args.GetDeferral();
        try
        {
            if (!Uri.TryCreate(ProjectUrlTextBox.Text, UriKind.Absolute, out Uri? projectUrl))
            {
                ShowError("プロジェクト URL は https://xxxxx.supabase.co の形式で入力してください。");
                args.Cancel = true;
                return;
            }

            if (projectUrl.Scheme != Uri.UriSchemeHttps)
            {
                ShowError("プロジェクト URL は https://xxxxx.supabase.co の形式で入力してください。");
                args.Cancel = true;
                return;
            }

            if (!IsLoadedFor(projectUrl, AnonKeyPasswordBox.Password))
            {
                ShowError("先に「業者一覧を読み込む」を実行してください。");
                args.Cancel = true;
                return;
            }

            if (VendorComboBox.SelectedItem is not LoginVendor selectedVendor)
            {
                ShowError("業者を選択してください。");
                args.Cancel = true;
                return;
            }

            SupabaseCredentials candidate;
            try
            {
                candidate = SupabaseCredentials.Create(
                    projectUrl,
                    AnonKeyPasswordBox.Password,
                    selectedVendor.Code,
                    PasswordBox.Password);
            }
            catch (ArgumentException exception)
            {
                ShowError(exception.Message);
                args.Cancel = true;
                return;
            }

            SupabaseConnectionResult connectionResult = await VerifyConnectivityAsync(candidate);
            if (!connectionResult.IsValid)
            {
                ShowError(connectionResult.ErrorMessage ?? "Supabase に接続できませんでした。");
                args.Cancel = true;
                return;
            }

            Result = candidate;
        }
        finally
        {
            deferral.Complete();
        }
    }

    private async Task<SupabaseConnectionResult> VerifyConnectivityAsync(SupabaseCredentials candidate)
    {
        string originalPrimaryButtonText = PrimaryButtonText;
        IsPrimaryButtonEnabled = false;
        PrimaryButtonText = "接続を確認しています...";
        try
        {
            return await _credentialsValidator.ValidateAsync(candidate);
        }
        finally
        {
            PrimaryButtonText = originalPrimaryButtonText;
            IsPrimaryButtonEnabled = true;
        }
    }

    private void ShowError(string message)
    {
        ErrorTextBlock.Text = message;
        ErrorTextBlock.Visibility = Visibility.Visible;
    }

    private bool HasConnectionInputs() =>
        !string.IsNullOrWhiteSpace(ProjectUrlTextBox.Text)
        && !string.IsNullOrWhiteSpace(AnonKeyPasswordBox.Password);

    private bool IsLoadedFor(Uri projectUrl, string anonKey) =>
        string.Equals(_loadedProjectUrl, projectUrl.AbsoluteUri, StringComparison.Ordinal)
        && string.Equals(_loadedAnonKey, anonKey, StringComparison.Ordinal);

    private void ClearLoadedVendors()
    {
        _loadedProjectUrl = null;
        _loadedAnonKey = null;
        VendorComboBox.ItemsSource = null;
        VendorComboBox.SelectedItem = null;
        VendorComboBox.IsEnabled = false;
    }

    private async Task<bool> LoadVendorsAsync()
    {
        if (!Uri.TryCreate(ProjectUrlTextBox.Text, UriKind.Absolute, out Uri? projectUrl)
            || projectUrl.Scheme != Uri.UriSchemeHttps)
        {
            ShowError("プロジェクト URL は https://xxxxx.supabase.co の形式で入力してください。");
            return false;
        }

        string anonKey = AnonKeyPasswordBox.Password;
        if (string.IsNullOrWhiteSpace(anonKey))
        {
            ShowError("Supabase anon key を指定してください。");
            return false;
        }

        LoadVendorsButton.IsEnabled = false;
        LoadVendorsButton.Content = "業者一覧を読み込んでいます...";
        try
        {
            IReadOnlyList<LoginVendor> vendors = await _loginVendorClient.GetLoginVendorsAsync(
                projectUrl,
                anonKey);
            if (vendors.Count == 0)
            {
                ShowError("選択可能な業者が見つかりませんでした。Supabase 側の login_vendors ビューを確認してください。");
                ClearLoadedVendors();
                return false;
            }

            VendorComboBox.ItemsSource = vendors;
            VendorComboBox.IsEnabled = true;
            _loadedProjectUrl = projectUrl.AbsoluteUri;
            _loadedAnonKey = anonKey;

            if (_pendingVendorCode is not null)
            {
                VendorComboBox.SelectedItem = vendors.FirstOrDefault(
                    vendor => string.Equals(vendor.Code, _pendingVendorCode, StringComparison.Ordinal));
            }

            HideError();
            return true;
        }
        catch (SupabaseAuthenticationException exception)
        {
            ClearLoadedVendors();
            ShowError(exception.Message);
            return false;
        }
        catch (HttpRequestException exception)
        {
            ClearLoadedVendors();
            string statusSuffix = exception.StatusCode is null
                ? string.Empty
                : $"(HTTP {(int)exception.StatusCode})";
            ShowError(
                "Supabase に接続できませんでした。プロジェクト URL・anon キー・ネットワークを確認してください。"
                + statusSuffix);
            return false;
        }
        catch (JsonException)
        {
            ClearLoadedVendors();
            ShowError("Supabase のログイン用業者一覧の形式が不正です。login_vendors ビューを確認してください。");
            return false;
        }
        catch (InvalidDataException exception)
        {
            ClearLoadedVendors();
            ShowError(exception.Message);
            return false;
        }
        finally
        {
            LoadVendorsButton.IsEnabled = true;
            LoadVendorsButton.Content = "業者一覧を読み込む";
        }
    }

    private void HideError()
    {
        ErrorTextBlock.Text = string.Empty;
        ErrorTextBlock.Visibility = Visibility.Collapsed;
    }
}
