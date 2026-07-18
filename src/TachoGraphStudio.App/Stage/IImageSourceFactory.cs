using Microsoft.UI.Xaml.Media;

namespace TachoGraphStudio.App.Stage;

// WriteableBitmap の生成は XAML ランタイム(UI スレッド)が必要なため、
// xUnit ホストで動く ViewModel テストから差し替えられるように抽象化する
public interface IImageSourceFactory
{
    ImageSource? Create(byte[] premultipliedBgra, int width, int height);
}
