using TachoGraphStudio.Core.Settings;

namespace TachoGraphStudio.App.Settings;

// 設定画面の検出プレビュー(#126)。実際の描画は OpenCV とファイル入出力に依存するため、
// ViewModel のデバウンス・キャンセル制御をテストできるように分離する
public interface IDetectionPreviewRenderer
{
    Task<DetectionPreviewImage> RenderAsync(
        ImageProcessingSettings settings,
        CancellationToken cancellationToken);
}
