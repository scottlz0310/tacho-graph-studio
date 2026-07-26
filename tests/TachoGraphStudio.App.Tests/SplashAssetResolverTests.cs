namespace TachoGraphStudio.App.Tests;

public sealed class SplashAssetResolverTests
{
    [Theory]
    // scale 100 は修飾子なしで出力される
    [InlineData(false, 100, "SplashScreen.png")]
    [InlineData(true, 100, "SplashScreenLight.png")]
    // 生成済みの派生と一致する倍率
    [InlineData(false, 125, "SplashScreen.scale-125.png")]
    [InlineData(false, 150, "SplashScreen.scale-150.png")]
    [InlineData(false, 200, "SplashScreen.scale-200.png")]
    [InlineData(true, 125, "SplashScreenLight.scale-125.png")]
    [InlineData(true, 150, "SplashScreenLight.scale-150.png")]
    [InlineData(true, 200, "SplashScreenLight.scale-200.png")]
    // 派生が無い倍率は拡大ぼけを避けるため直上の派生を選ぶ
    [InlineData(false, 110, "SplashScreen.scale-125.png")]
    [InlineData(false, 175, "SplashScreen.scale-200.png")]
    [InlineData(true, 175, "SplashScreenLight.scale-200.png")]
    // 最大の派生を超える倍率は最大に留める
    [InlineData(false, 225, "SplashScreen.scale-200.png")]
    [InlineData(false, 400, "SplashScreen.scale-200.png")]
    [InlineData(true, 400, "SplashScreenLight.scale-200.png")]
    // 100 未満は最小の派生に丸める
    [InlineData(false, 75, "SplashScreen.png")]
    public void GetFileName_ReturnsAssetForThemeAndScale(bool isLightTheme, int displayScale, string expected)
    {
        string actual = SplashAssetResolver.GetFileName(isLightTheme, displayScale);

        Assert.Equal(expected, actual);
    }

    [Theory]
    [InlineData(100)]
    [InlineData(125)]
    [InlineData(150)]
    [InlineData(175)]
    [InlineData(200)]
    [InlineData(400)]
    public void GetFileName_LightAndDarkDifferOnlyByBaseName(int displayScale)
    {
        string dark = SplashAssetResolver.GetFileName(isLightTheme: false, displayScale);
        string light = SplashAssetResolver.GetFileName(isLightTheme: true, displayScale);

        Assert.Equal(dark.Replace("SplashScreen", "SplashScreenLight"), light);
    }
}
