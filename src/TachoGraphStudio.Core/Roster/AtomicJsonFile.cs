using System.Text.Json;

namespace TachoGraphStudio.Core.Roster;

internal sealed class AtomicJsonFile<TDocument> : IDisposable
    where TDocument : class
{
    private readonly string _displayName;
    private readonly string _filePath;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly JsonSerializerOptions _serializerOptions;

    public AtomicJsonFile(
        string filePath,
        JsonSerializerOptions serializerOptions,
        string displayName)
    {
        _filePath = filePath;
        _serializerOptions = serializerOptions;
        _displayName = displayName;
    }

    public async Task<TDocument?> ReadAsync(CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (!File.Exists(_filePath))
            {
                return null;
            }

            await using FileStream stream = new(
                _filePath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                bufferSize: 4096,
                useAsync: true);

            TDocument? document = await JsonSerializer.DeserializeAsync<TDocument>(
                stream,
                _serializerOptions,
                cancellationToken);

            return document ?? throw new InvalidDataException(
                $"{_displayName}が JSON オブジェクトではありません。");
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task WriteAsync(TDocument document, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(document);

        await _gate.WaitAsync(cancellationToken);
        try
        {
            string? directoryPath = Path.GetDirectoryName(_filePath);
            if (!string.IsNullOrEmpty(directoryPath))
            {
                Directory.CreateDirectory(directoryPath);
            }

            string temporaryFilePath = $"{_filePath}.{Guid.NewGuid():N}.tmp";
            try
            {
                await using (FileStream stream = new(
                    temporaryFilePath,
                    FileMode.CreateNew,
                    FileAccess.Write,
                    FileShare.None,
                    bufferSize: 4096,
                    useAsync: true))
                {
                    await JsonSerializer.SerializeAsync(
                        stream,
                        document,
                        _serializerOptions,
                        cancellationToken);
                    await stream.FlushAsync(cancellationToken);
                }

                File.Move(temporaryFilePath, _filePath, overwrite: true);
            }
            catch (Exception writeException)
            {
                try
                {
                    File.Delete(temporaryFilePath);
                }
                catch (Exception cleanupException)
                {
                    throw new IOException(
                        $"{_displayName}の書き込みと一時ファイルの削除に失敗しました。",
                        new AggregateException(writeException, cleanupException));
                }

                throw;
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    public void Dispose()
    {
        _gate.Dispose();
    }
}
