using System.Diagnostics;

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
        WinRT.ComWrappersSupport.InitializeComWrappers();

        SimpleSplashScreen? splashScreen = TryShowSplashScreen();

        Application.Start(_ =>
        {
            DispatcherQueueSynchronizationContext context = new(DispatcherQueue.GetForCurrentThread());
            SynchronizationContext.SetSynchronizationContext(context);
            new App(splashScreen);
        });
    }

    /// <summary>
    /// MSIX の uap:SplashScreen は WinUI 3 デスクトップアプリでは OS が描画しない（#81）。
    /// Application.Start より前に Win32 レイヤードウィンドウで表示することで、
    /// WinUI ランタイム初期化中の無表示区間も splash で覆う。
    /// </summary>
    private static SimpleSplashScreen? TryShowSplashScreen()
    {
        // splash は装飾であり、マニフェストの画像を解決できないだけでアプリを起動不能に
        // してはならない。XAML 初期化前で UI もロガーも使えないためトレース出力に留める。
        // Debug.WriteLine は [Conditional("DEBUG")] のため配布される Release 構成では
        // 呼び出しごと消える。Trace は Release でも TRACE が定義されるため残る
        try
        {
            return SimpleSplashScreen.ShowDefaultSplashScreen();
        }
        catch (InvalidOperationException ex)
        {
            Trace.WriteLine($"splash の表示に失敗したため splash なしで起動します: {ex.Message}");
            return null;
        }
    }
}
