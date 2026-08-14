using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

using TachoGraphStudio.Core.Settings;

namespace TachoGraphStudio.App.Settings;

public sealed partial class SupabaseSettingsDialog : ContentDialog
{
    private readonly SupabaseCredentialsValidator _credentialsValidator;

    public SupabaseSettingsDialog(
        SupabaseCredentials? existingCredentials,
        SupabaseCredentialsValidator credentialsValidator)
    {
        InitializeComponent();
        _credentialsValidator = credentialsValidator;

        if (existingCredentials is not null)
        {
            ProjectUrlTextBox.Text = existingCredentials.ProjectUrl.AbsoluteUri;
            AnonKeyPasswordBox.Password = existingCredentials.AnonKey;
            EmailTextBox.Text = existingCredentials.Email;
            PasswordBox.Password = existingCredentials.Password;
        }
    }

    public SupabaseCredentials? Result { get; private set; }

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

            SupabaseCredentials candidate;
            try
            {
                candidate = SupabaseCredentials.Create(
                    projectUrl,
                    AnonKeyPasswordBox.Password,
                    EmailTextBox.Text.Trim(),
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
}
