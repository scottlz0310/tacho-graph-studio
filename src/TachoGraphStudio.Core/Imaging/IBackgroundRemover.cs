namespace TachoGraphStudio.Core.Imaging;

// 円盤 1 枚の背景除去。パイプラインのストリーミング契約（同一シートの後続円盤が
// 失敗しても先行分は呼び出し元へ届く）をテストで固定するため、差し替え可能にしている
public interface IBackgroundRemover
{
    BackgroundRemovalResult Remove(DiscImage disc, BackgroundRemovalOptions? options = null);
}
