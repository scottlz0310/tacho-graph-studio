using TachoGraphStudio.App.Settings;
using TachoGraphStudio.Core.Settings;

using Windows.Graphics;

namespace TachoGraphStudio.App.Tests.Settings;

public sealed class WindowPlacementTrackerTests
{
    // 復元先の算出(#137)。作業領域の解決は WinUI 依存のため注入する
    private static readonly RectInt32 PrimaryWorkArea = new(0, 0, 1920, 1032);

    [Fact]
    public void ResolveRestoreBounds_WithoutSavedPlacementReturnsNull()
    {
        Assert.Null(WindowPlacementTracker.ResolveRestoreBounds(null, _ => PrimaryWorkArea));
    }

    // 寸法 0 以下は保存値が壊れている。既定サイズで起動させる
    [Theory]
    [InlineData(0, 900)]
    [InlineData(1440, 0)]
    [InlineData(-1, 900)]
    [InlineData(1440, -1)]
    public void ResolveRestoreBounds_WithInvalidSizeReturnsNull(int width, int height)
    {
        WindowPlacement placement = new(100, 50, width, height, IsMaximized: false);

        Assert.Null(WindowPlacementTracker.ResolveRestoreBounds(placement, _ => PrimaryWorkArea));
    }

    [Fact]
    public void ResolveRestoreBounds_WithinWorkAreaKeepsSavedBounds()
    {
        WindowPlacement placement = new(100, 50, 1440, 900, IsMaximized: false);

        Assert.Equal(
            new RectInt32(100, 50, 1440, 900),
            WindowPlacementTracker.ResolveRestoreBounds(placement, _ => PrimaryWorkArea));
    }

    // 作業領域より大きい保存値は縮める(高解像度モニタから低解像度モニタへ移った場合)
    [Fact]
    public void ResolveRestoreBounds_LargerThanWorkAreaShrinksToWorkArea()
    {
        WindowPlacement placement = new(0, 0, 3840, 2160, IsMaximized: false);

        Assert.Equal(
            new RectInt32(0, 0, 1920, 1032),
            WindowPlacementTracker.ResolveRestoreBounds(placement, _ => PrimaryWorkArea));
    }

    // 画面外の保存値は作業領域の内側へ寄せる(モニタを外した場合など)
    [Theory]
    [InlineData(-500, 50, 0, 50)]
    [InlineData(100, -500, 100, 0)]
    [InlineData(5000, 50, 480, 50)]
    [InlineData(100, 5000, 100, 132)]
    public void ResolveRestoreBounds_OutsideWorkAreaMovesInside(
        int savedX,
        int savedY,
        int expectedX,
        int expectedY)
    {
        WindowPlacement placement = new(savedX, savedY, 1440, 900, IsMaximized: false);

        Assert.Equal(
            new RectInt32(expectedX, expectedY, 1440, 900),
            WindowPlacementTracker.ResolveRestoreBounds(placement, _ => PrimaryWorkArea));
    }

    // セカンダリモニタが主モニタの左・上にある構成では作業領域の原点が負になる
    [Fact]
    public void ResolveRestoreBounds_HandlesWorkAreaWithNegativeOrigin()
    {
        RectInt32 leftMonitorWorkArea = new(-1920, -200, 1920, 1032);
        WindowPlacement placement = new(-3000, -900, 1440, 900, IsMaximized: false);

        Assert.Equal(
            new RectInt32(-1920, -200, 1440, 900),
            WindowPlacementTracker.ResolveRestoreBounds(placement, _ => leftMonitorWorkArea));
    }

    // 作業領域は保存値の位置で決まる(最寄りディスプレイの解決を呼び出し側へ委ねている)
    [Fact]
    public void ResolveRestoreBounds_PassesSavedBoundsToWorkAreaResolver()
    {
        WindowPlacement placement = new(100, 50, 1440, 900, IsMaximized: false);
        RectInt32? requested = null;

        WindowPlacementTracker.ResolveRestoreBounds(
            placement,
            saved =>
            {
                requested = saved;
                return PrimaryWorkArea;
            });

        Assert.Equal(new RectInt32(100, 50, 1440, 900), requested);
    }

    [Fact]
    public void Capture_WithoutAnyBoundsReturnsNull()
    {
        WindowPlacementTracker tracker = new();

        Assert.Null(tracker.Capture(isMaximized: false));
    }

    [Fact]
    public void MaximizeOnlyFirstRun_CapturesInitialBoundsWithMaximizedFlag()
    {
        WindowPlacementTracker tracker = new();
        // 追跡開始時は通常表示
        tracker.Initialize(isRestored: true, new RectInt32(100, 50, 1440, 900));

        // 移動・リサイズせずに最大化(最大化中の bounds 変更は記録しない)
        tracker.OnBoundsChanged(isRestored: false, new RectInt32(0, 0, 2560, 1400));

        WindowPlacement? placement = tracker.Capture(isMaximized: true);
        Assert.Equal(new WindowPlacement(100, 50, 1440, 900, IsMaximized: true), placement);
    }

    [Fact]
    public void MaximizeBeforeTrackingStarts_StillCapturesInitialBounds()
    {
        WindowPlacementTracker tracker = new();
        // ウィンドウ生成直後(表示前・必ず通常表示)に初期化される。
        // 起動処理の await 中に最大化されても(bounds 変更は未追跡)、初期 bounds が残る
        tracker.Initialize(isRestored: true, new RectInt32(100, 50, 1440, 900));

        Assert.Equal(
            new WindowPlacement(100, 50, 1440, 900, IsMaximized: true),
            tracker.Capture(isMaximized: true));
    }

    [Fact]
    public void Initialize_WhenNotRestoredKeepsNull()
    {
        WindowPlacementTracker tracker = new();

        tracker.Initialize(isRestored: false, new RectInt32(0, 0, 2560, 1400));

        Assert.Null(tracker.Capture(isMaximized: true));
    }

    [Fact]
    public void OnBoundsChanged_WhileRestoredUpdatesBounds()
    {
        WindowPlacementTracker tracker = new();
        tracker.Initialize(isRestored: true, new RectInt32(100, 50, 1440, 900));

        tracker.OnBoundsChanged(isRestored: true, new RectInt32(300, 120, 1200, 700));

        Assert.Equal(
            new WindowPlacement(300, 120, 1200, 700, IsMaximized: false),
            tracker.Capture(isMaximized: false));
    }

    [Fact]
    public void Seed_RestoredPlacementIsUsedUntilChanged()
    {
        WindowPlacementTracker tracker = new();
        // 起動時に保存済み配置を復元(最大化で起動しても通常時 bounds を引き継ぐ)
        tracker.Seed(new RectInt32(300, 120, 1200, 700));
        tracker.Initialize(isRestored: false, new RectInt32(0, 0, 2560, 1400));

        Assert.Equal(
            new WindowPlacement(300, 120, 1200, 700, IsMaximized: true),
            tracker.Capture(isMaximized: true));
    }
}
