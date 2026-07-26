using System.Diagnostics;
using System.Runtime.InteropServices;

using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.Win32;

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
            string? imagePath = ResolveSplashImagePath();

            // 解決できなければマニフェスト定義（ダーク）を MRT 経由で表示する既定動作に戻す
            return imagePath is null
                ? SimpleSplashScreen.ShowDefaultSplashScreen()
                : SimpleSplashScreen.ShowSplashScreenImage(imagePath);
        }
        catch (InvalidOperationException ex)
        {
            Trace.WriteLine($"splash の表示に失敗したため splash なしで起動します: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// システムテーマと DPI に対応する splash 画像のパスを返す。見つからなければ null。
    /// </summary>
    private static string? ResolveSplashImagePath()
    {
        string fileName = SplashAssetResolver.GetFileName(IsLightTheme(), GetSystemScale());
        string path = Path.Combine(AppContext.BaseDirectory, "Assets", fileName);

        return File.Exists(path) ? path : null;
    }

    /// <summary>
    /// アプリのテーマがライトかどうか。取得できない場合は、これまで唯一の splash であった
    /// ダークを既定とする。
    /// </summary>
    private static bool IsLightTheme()
    {
        object? value = Registry.GetValue(
            @"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize",
            "AppsUseLightTheme",
            defaultValue: null);

        return value is int useLightTheme && useLightTheme != 0;
    }

    /// <summary>システム DPI を Windows の表示倍率（100 / 125 / 150 …）に換算する。</summary>
    private static int GetSystemScale() => (int)Math.Round(GetDpiForSystem() * 100.0 / 96.0);

    // LibraryImport は AllowUnsafeBlocks をプロジェクト全体で要求する（SYSLIB1062）。
    // 引数なし・戻り値 blittable のこの 1 箇所のために unsafe を解禁はしない
    [DllImport("user32.dll")]
    private static extern uint GetDpiForSystem();
}
