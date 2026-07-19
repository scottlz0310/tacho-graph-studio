using TachoGraphStudio.App.Settings;
using TachoGraphStudio.Core.Settings;

using Windows.Graphics;

namespace TachoGraphStudio.App.Tests.Settings;

public sealed class WindowPlacementTrackerTests
{
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
