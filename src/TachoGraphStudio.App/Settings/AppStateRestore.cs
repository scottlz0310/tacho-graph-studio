namespace TachoGraphStudio.App.Settings;

// アプリ状態(FR-22)の復元のうち、ViewModel が持たない画面レイアウト値の判定(#137)。
// 状態ファイルは手動編集や旧バージョンの書き込みで壊れうるため、
// そのまま適用せず利用できる値かどうかをここで判定する
public static class AppStateRestore
{
    /// <summary>
    /// 保存されたサイドバー幅を、実際に適用してよい値へ解決する。
    /// 未保存・非有限値(壊れた状態ファイル)なら null を返し、既定幅のままにする。
    /// </summary>
    /// <param name="savedWidth">保存されていた幅。未保存なら null。</param>
    /// <param name="minWidth">列に設定された下限。</param>
    /// <param name="maxWidth">列に設定された上限。</param>
    public static double? ResolveSidebarWidth(double? savedWidth, double minWidth, double maxWidth)
    {
        if (savedWidth is not { } width || !double.IsFinite(width))
        {
            return null;
        }

        return Math.Clamp(width, minWidth, maxWidth);
    }
}
