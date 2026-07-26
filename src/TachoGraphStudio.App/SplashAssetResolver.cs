namespace TachoGraphStudio.App;

/// <summary>
/// テーマと表示倍率から splash 画像のファイル名を決める（#88）。
/// </summary>
/// <remarks>
/// MRT のリソース修飾子に theme は存在しないためライト/ダークはファイル名で分けており、
/// WinUIEx の ShowSplashScreenImage は渡されたファイルをそのまま読んで scale 解決を
/// 行わないため倍率の選択も必要になる。テーマと DPI の取得は呼び出し側の責務とし、
/// ここは副作用のない対応付けに限定してテスト可能にしている。
/// </remarks>
public static class SplashAssetResolver
{
    /// <summary>Generate-MsixAssets.ps1 が splash に対して出力する scale 修飾子（昇順）。</summary>
    private static readonly int[] Scales = [100, 125, 150, 200];

    /// <param name="isLightTheme">アプリのテーマがライトかどうか。</param>
    /// <param name="displayScale">Windows の表示倍率（100 / 125 / 150 …）。</param>
    public static string GetFileName(bool isLightTheme, int displayScale)
    {
        string baseName = isLightTheme ? "SplashScreenLight" : "SplashScreen";

        // 拡大時のぼけを避けるため実倍率以上で最も小さい派生を選ぶ。
        // 派生を超える倍率（225% 以上）では最大の派生に留める
        int scale = Scales.FirstOrDefault(candidate => candidate >= displayScale, Scales[^1]);

        // scale 100 は修飾子なしで出力される。`.scale-100.png` を併置すると
        // 100% DPI 環境でそちらが優先され非修飾版との差分が事故になるため（#80）
        return scale == 100 ? $"{baseName}.png" : $"{baseName}.scale-{scale}.png";
    }
}
