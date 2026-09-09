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

    /// <summary>
    /// 保存済み配置から復元先の bounds を求める。保存値が無い・寸法が不正な場合は null を返す。
    /// モニタ構成の変更で画面外へ復元されないよう、<paramref name="resolveWorkArea"/> が返す
    /// 作業領域へ収める。作業領域の解決は WinUI の DisplayArea に依存するため呼び出し側へ委ねる。
    /// </summary>
    public static RectInt32? ResolveRestoreBounds(
        WindowPlacement? placement,
        Func<RectInt32, RectInt32> resolveWorkArea)
    {
        ArgumentNullException.ThrowIfNull(resolveWorkArea);

        if (placement is not { Width: > 0, Height: > 0 })
        {
            return null;
        }

        RectInt32 saved = new(placement.X, placement.Y, placement.Width, placement.Height);
        RectInt32 workArea = resolveWorkArea(saved);

        int width = Math.Min(saved.Width, workArea.Width);
        int height = Math.Min(saved.Height, workArea.Height);
        int x = Math.Clamp(saved.X, workArea.X, workArea.X + workArea.Width - width);
        int y = Math.Clamp(saved.Y, workArea.Y, workArea.Y + workArea.Height - height);
        return new RectInt32(x, y, width, height);
    }

    public WindowPlacement? Capture(bool isMaximized) =>
        _normalBounds is { } bounds
            ? new WindowPlacement(bounds.X, bounds.Y, bounds.Width, bounds.Height, isMaximized)
            : null;
}
