using System.Runtime.CompilerServices;

namespace TachoGraphStudio.Core.Imaging;

public sealed class SheetLoader
{
    // JPEG の SOI マーカー。この層の契約はシグネチャ確認（誤った拡張子・明白な非 JPEG の排除）のみで、
    // 切り詰め等の破損検出はデコード段（issue #8, OpenCvSharp）が担う
    private static readonly byte[] JpegMagic = [0xFF, 0xD8];

    private readonly IPdfRasterizer _pdfRasterizer;

    public SheetLoader(IPdfRasterizer pdfRasterizer)
    {
        ArgumentNullException.ThrowIfNull(pdfRasterizer);

        _pdfRasterizer = pdfRasterizer;
    }

    public async IAsyncEnumerable<SheetImage> LoadAsync(
        IEnumerable<string> paths,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(paths);

        foreach (string path in paths)
        {
            cancellationToken.ThrowIfCancellationRequested();

            string extension = Path.GetExtension(path);
            if (extension.Equals(".pdf", StringComparison.OrdinalIgnoreCase))
            {
                await foreach (SheetImage sheet in LoadPdfAsync(path, cancellationToken))
                {
                    yield return sheet;
                }
            }
            else if (extension.Equals(".jpg", StringComparison.OrdinalIgnoreCase)
                || extension.Equals(".jpeg", StringComparison.OrdinalIgnoreCase))
            {
                yield return await LoadJpegAsync(path, cancellationToken);
            }
            else
            {
                throw new SheetLoadException($"未対応のファイル形式です（PDF / JPEG のみ対応）: {path}");
            }
        }
    }

    private static async Task<SheetImage> LoadJpegAsync(string path, CancellationToken cancellationToken)
    {
        byte[] imageBytes;
        try
        {
            imageBytes = await File.ReadAllBytesAsync(path, cancellationToken);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            throw new SheetLoadException($"シート画像を読み込めません: {path}", exception);
        }

        if (imageBytes.Length < JpegMagic.Length || !imageBytes.AsSpan(0, JpegMagic.Length).SequenceEqual(JpegMagic))
        {
            throw new SheetLoadException($"JPEG 形式ではありません: {path}");
        }

        return new SheetImage(path, PageIndex: 0, imageBytes);
    }

    private async IAsyncEnumerable<SheetImage> LoadPdfAsync(
        string path,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        // MoveNextAsync をラスタライズ失敗としてラップするため await foreach ではなく手動列挙する
        // （async iterator は yield return を含む try ブロックで catch できない）
        await using IAsyncEnumerator<byte[]> pages = _pdfRasterizer
            .RasterizePagesAsync(path, cancellationToken)
            .GetAsyncEnumerator(cancellationToken);

        int pageIndex = 0;
        while (true)
        {
            byte[] pageBytes;
            try
            {
                if (!await pages.MoveNextAsync())
                {
                    break;
                }

                pageBytes = pages.Current;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception exception)
            {
                throw new SheetLoadException(
                    $"PDF のラスタライズに失敗しました（{pageIndex + 1} ページ目）: {path}",
                    exception);
            }

            yield return new SheetImage(path, pageIndex, pageBytes);
            pageIndex++;
        }
    }
}
