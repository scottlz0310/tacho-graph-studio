namespace TachoGraphStudio.App.Settings;

// 検出プレビュー(#126)1 回分の描画結果。UI 型に依存しないため ViewModel のテストから検証できる。
// シートは不透明なので premultiplied と straight は一致する
public sealed record DetectionPreviewImage(
    int Width,
    int Height,
    byte[] PremultipliedBgra,
    int DiscCount);
