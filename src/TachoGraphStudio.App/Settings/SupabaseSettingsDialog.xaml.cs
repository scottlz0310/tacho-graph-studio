using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

using TachoGraphStudio.Core.Settings;

namespace TachoGraphStudio.App.Settings;

public sealed partial class SupabaseSettingsDialog : ContentDialog
{
    public SupabaseSettingsDialog(SupabaseCredentials? existingCredentials)
    {
        InitializeComponent();

        if (existingCredentials is not null)
        {
            ProjectUrlTextBox.Text = existingCredentials.ProjectUrl.AbsoluteUri;
            AnonKeyPasswordBox.Password = existingCredentials.AnonKey;
        }
    }

    public SupabaseCredentials? Result { get; private set; }

    private void OnPrimaryButtonClick(ContentDialog sender, ContentDialogButtonClickEventArgs args)
    {
        if (!Uri.TryCreate(ProjectUrlTextBox.Text, UriKind.Absolute, out Uri? projectUrl))
        {
            ShowError("プロジェクト URL は https://xxxxx.supabase.co の形式で入力してください。");
            args.Cancel = true;
            return;
        }

        try
        {
            Result = SupabaseCredentials.Create(projectUrl, AnonKeyPasswordBox.Password);
        }
        catch (ArgumentException exception)
        {
            ShowError(exception.Message);
            args.Cancel = true;
        }
    }

    private void ShowError(string message)
    {
        ErrorTextBlock.Text = message;
        ErrorTextBlock.Visibility = Visibility.Visible;
    }
}
