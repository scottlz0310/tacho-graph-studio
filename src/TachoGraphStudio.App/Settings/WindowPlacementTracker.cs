using TachoGraphStudio.Core.Settings;

using Windows.Graphics;

namespace TachoGraphStudio.App.Settings;

// ウィンドウ配置(FR-22)の記録。最大化中の座標は復元に使えないため、
// 通常表示時の bounds のみを保持し、最大化フラグと組み合わせて保存する
public sealed class WindowPlacementTracker
{
    private RectInt32? _normalBounds;

    // 追跡開始時点の状態で初期化する。移動・リサイズせずに最大化して閉じても
    // 起動時の通常表示 bounds が残るようにする
    public void Initialize(bool isRestored, RectInt32 bounds)
    {
        if (isRestored)
        {
            _normalBounds = bounds;
        }
    }

    // 復元した保存済み配置を初期値として引き継ぐ
    public void Seed(RectInt32 bounds)
    {
        _normalBounds = bounds;
    }

    public void OnBoundsChanged(bool isRestored, RectInt32 bounds)
    {
        if (isRestored)
        {
            _normalBounds = bounds;
        }
    }

    public WindowPlacement? Capture(bool isMaximized) =>
        _normalBounds is { } bounds
            ? new WindowPlacement(bounds.X, bounds.Y, bounds.Width, bounds.Height, isMaximized)
            : null;
}
