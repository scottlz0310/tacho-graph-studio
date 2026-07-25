using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;

using WinUIEx;

namespace TachoGraphStudio.App;

/// <summary>
/// XAML 生成の Main（DISABLE_XAML_GENERATED_MAIN で抑止）を置き換えるエントリポイント。
/// </summary>
public static class Program
{
    [STAThread]
    private static void Main()
    {
        // MSIX の uap:SplashScreen は WinUI 3 デスクトップアプリでは OS が描画しない（#81）。
        // Application.Start より前に Win32 レイヤードウィンドウで表示することで、
        // WinUI ランタイム初期化中の無表示区間も splash で覆う。
        SimpleSplashScreen splashScreen = SimpleSplashScreen.ShowDefaultSplashScreen();

        WinRT.ComWrappersSupport.InitializeComWrappers();

        Application.Start(_ =>
        {
            DispatcherQueueSynchronizationContext context = new(DispatcherQueue.GetForCurrentThread());
            SynchronizationContext.SetSynchronizationContext(context);
            new App(splashScreen);
        });
    }
}
