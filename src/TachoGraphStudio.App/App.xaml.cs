using Microsoft.UI.Xaml;

using WinUIEx;

namespace TachoGraphStudio.App;

public partial class App : Application
{
    private SimpleSplashScreen? _splashScreen;
    private Window? _window;

    // XAML 生成コードは DISABLE_XAML_GENERATED_MAIN 定義時も（未使用の）
    // XamlGeneratedMain 内に new App() を残すため、引数なしでも構築可能にする
    public App(SimpleSplashScreen? splashScreen = null)
    {
        _splashScreen = splashScreen;
        InitializeComponent();
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        _window = new MainWindow();
        // MainWindow が描画されるまで splash を残す。Activate() の直後に閉じると
        // 初回レイアウト中の白画面が露出する
        _window.Activated += OnWindowActivated;
        _window.Activate();
    }

    private void OnWindowActivated(object sender, WindowActivatedEventArgs args)
    {
        ((Window)sender).Activated -= OnWindowActivated;
        _splashScreen?.Dispose();
        _splashScreen = null;
    }
}
