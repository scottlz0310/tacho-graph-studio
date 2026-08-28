using System.Text.Json;

using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

using TachoGraphStudio.Core.Auth;
using TachoGraphStudio.Core.Roster;
using TachoGraphStudio.Core.Settings;

using WinUIEx;

namespace TachoGraphStudio.App.Settings;

public sealed partial class SupabaseSettingsDialog : WindowEx
{
    private readonly TaskCompletionSource<bool> _closed = new(
        TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly SupabaseCredentialsValidator _credentialsValidator;
    private readonly SupabaseCredentials? _existingCredentials;
    private readonly ILoginVendorClient _loginVendorClient;
    private string? _loadedAnonKey;
    private string? _loadedProjectUrl;
    private readonly string? _pendingVendorCode;
    private bool _accepted;
    private bool _isShown;

    public SupabaseSettingsDialog(
        SupabaseCredentials? existingCredentials,
        ImageProcessingSettings imageProcessingSettings,
        SupabaseCredentialsValidator credentialsValidator,
        ILoginVendorClient loginVendorClient,
        bool selectSupabaseSection = false)
    {
        ArgumentNullException.ThrowIfNull(imageProcessingSettings);

        InitializeComponent();
        _existingCredentials = existingCredentials;
        _credentialsValidator = credentialsValidator;
        _loginVendorClient = loginVendorClient;
        Closed += OnClosed;

        imageProcessingSettings.Validate();
        ThresholdNumberBox.Value = imageProcessingSettings.Threshold;
        PaddingNumberBox.Value = imageProcessingSettings.PaddingPx;
        EllipsePaddingNumberBox.Value = imageProcessingSettings.EllipsePaddingPx;
        SettingsTabView.SelectedIndex = selectSupabaseSection ? 1 : 0;

        if (existingCredentials is not null)
        {
            ProjectUrlTextBox.Text = existingCredentials.ProjectUrl.AbsoluteUri;
            AnonKeyPasswordBox.Password = existingCredentials.AnonKey;
            _pendingVendorCode = existingCredentials.VendorCode;
            PasswordBox.Password = existingCredentials.Password;
        }
    }

    public SupabaseCredentials? Result { get; private set; }

    public ImageProcessingSettings? ImageProcessingResult { get; private set; }

    public Task<bool> ShowAsync()
    {
        if (_isShown)
        {
            throw new InvalidOperationException("設定ウィンドウは既に表示されています。");
        }

        _isShown = true;
        Activate();
        this.CenterOnScreen();
        return _closed.Task;
    }

    private void OnClosed(object sender, WindowEventArgs args) => _closed.TrySetResult(_accepted);

    private async void OnLoaded(object sender, RoutedEventArgs args)
    {
        RootGrid.Loaded -= OnLoaded;

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

    private async void OnSaveButtonClick(object sender, RoutedEventArgs args)
    {
        if (!TryReadImageProcessingSettings(out ImageProcessingSettings imageProcessingSettings))
        {
            SettingsTabView.SelectedIndex = 0;
            return;
        }

        if (!HasAnyConnectionInput() || ConnectionInputsMatchExisting())
        {
            Accept(imageProcessingSettings, credentials: null);
            return;
        }

        if (!Uri.TryCreate(ProjectUrlTextBox.Text, UriKind.Absolute, out Uri? projectUrl)
            || projectUrl.Scheme != Uri.UriSchemeHttps)
        {
            SettingsTabView.SelectedIndex = 1;
            ShowError("プロジェクト URL は https://xxxxx.supabase.co の形式で入力してください。");
            return;
        }

        if (!IsLoadedFor(projectUrl, AnonKeyPasswordBox.Password))
        {
            SettingsTabView.SelectedIndex = 1;
            ShowError("先に「業者一覧を読み込む」を実行してください。");
            return;
        }

        if (VendorComboBox.SelectedItem is not LoginVendor selectedVendor)
        {
            SettingsTabView.SelectedIndex = 1;
            ShowError("業者を選択してください。");
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
            return;
        }

        SupabaseConnectionResult connectionResult = await VerifyConnectivityAsync(candidate);
        if (!connectionResult.IsValid)
        {
            ShowError(connectionResult.ErrorMessage ?? "Supabase に接続できませんでした。");
            return;
        }

        Accept(imageProcessingSettings, candidate);
    }

    private void OnCancelButtonClick(object sender, RoutedEventArgs args) => Close();

    private void Accept(
        ImageProcessingSettings imageProcessingSettings,
        SupabaseCredentials? credentials)
    {
        ImageProcessingResult = imageProcessingSettings;
        Result = credentials;
        _accepted = true;
        Close();
    }

    private void OnRestoreImageProcessingDefaultsButtonClick(object sender, RoutedEventArgs args)
    {
        ImageProcessingSettings defaults = ImageProcessingSettings.Default;
        ThresholdNumberBox.Value = defaults.Threshold;
        PaddingNumberBox.Value = defaults.PaddingPx;
        EllipsePaddingNumberBox.Value = defaults.EllipsePaddingPx;
        HideError();
    }

    private bool TryReadImageProcessingSettings(out ImageProcessingSettings settings)
    {
        settings = ImageProcessingSettings.Default;
        if (!TryReadInteger(ThresholdNumberBox, "前景判定しきい値", out int threshold)
            || !TryReadInteger(PaddingNumberBox, "切り出し余白", out int paddingPx)
            || !TryReadInteger(EllipsePaddingNumberBox, "アルファ円マージン", out int ellipsePaddingPx))
        {
            return false;
        }

        ImageProcessingSettings candidate = new()
        {
            Threshold = threshold,
            PaddingPx = paddingPx,
            EllipsePaddingPx = ellipsePaddingPx,
        };

        try
        {
            candidate.Validate();
        }
        catch (ArgumentException exception)
        {
            ShowError(exception.Message);
            return false;
        }

        settings = candidate;
        return true;
    }

    private bool TryReadInteger(NumberBox numberBox, string label, out int value)
    {
        double input = numberBox.Value;
        if (!double.IsFinite(input)
            || input != Math.Truncate(input)
            || input < int.MinValue
            || input > int.MaxValue)
        {
            ShowError($"{label}は整数で指定してください。");
            value = default;
            return false;
        }

        value = (int)input;
        return true;
    }

    private async Task<SupabaseConnectionResult> VerifyConnectivityAsync(SupabaseCredentials candidate)
    {
        object originalContent = SaveButton.Content;
        SaveButton.IsEnabled = false;
        SaveButton.Content = "接続を確認しています...";
        try
        {
            return await _credentialsValidator.ValidateAsync(candidate);
        }
        finally
        {
            SaveButton.Content = originalContent;
            SaveButton.IsEnabled = true;
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

    private bool HasAnyConnectionInput() =>
        !string.IsNullOrWhiteSpace(ProjectUrlTextBox.Text)
        || !string.IsNullOrWhiteSpace(AnonKeyPasswordBox.Password)
        || !string.IsNullOrWhiteSpace(PasswordBox.Password)
        || VendorComboBox.SelectedItem is not null;

    private bool ConnectionInputsMatchExisting()
    {
        if (_existingCredentials is null
            || !string.Equals(
                ProjectUrlTextBox.Text,
                _existingCredentials.ProjectUrl.AbsoluteUri,
                StringComparison.Ordinal)
            || !string.Equals(
                AnonKeyPasswordBox.Password,
                _existingCredentials.AnonKey,
                StringComparison.Ordinal)
            || !string.Equals(
                PasswordBox.Password,
                _existingCredentials.Password,
                StringComparison.Ordinal))
        {
            return false;
        }

        return VendorComboBox.SelectedItem is not LoginVendor selectedVendor
            || string.Equals(
                selectedVendor.Code,
                _existingCredentials.VendorCode,
                StringComparison.OrdinalIgnoreCase);
    }

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
                    vendor => string.Equals(vendor.Code, _pendingVendorCode, StringComparison.OrdinalIgnoreCase));
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
        catch (OperationCanceledException)
        {
            ClearLoadedVendors();
            ShowError("Supabase への接続がタイムアウトしました。プロジェクト URL・anon キー・ネットワークを確認してください。");
            return false;
        }
        catch (JsonException)
        {
            ClearLoadedVendors();
            ShowError("Supabase のログイン用業者一覧の形式が不正です。login_vendors ビューを確認してください。");
            return false;
        }
        catch (NotSupportedException)
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
