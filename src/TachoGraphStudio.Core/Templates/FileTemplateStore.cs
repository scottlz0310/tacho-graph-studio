namespace TachoGraphStudio.Core.Templates;

// 1 テンプレート = 1 JSON ファイル(GIMP 互換フォーマット)をディレクトリで管理する。
// 書き込みは AtomicJsonFile と同じ一時ファイル + Move の原子的置換
public sealed class FileTemplateStore : ITemplateStore
{
    private const string FileExtension = ".json";

    private readonly string _directoryPath;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public FileTemplateStore(string directoryPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directoryPath);

        _directoryPath = Path.GetFullPath(directoryPath);
    }

    public async Task<TemplateStoreListResult> ListAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (!Directory.Exists(_directoryPath))
            {
                return new TemplateStoreListResult([], []);
            }

            List<StoredTemplate> templates = [];
            List<TemplateLoadFailure> failures = [];

            IEnumerable<string> filePaths = Directory
                .EnumerateFiles(_directoryPath, $"*{FileExtension}")
                .Order(StringComparer.OrdinalIgnoreCase);

            foreach (string filePath in filePaths)
            {
                cancellationToken.ThrowIfCancellationRequested();

                string fileName = Path.GetFileName(filePath);
                try
                {
                    string json = await File.ReadAllTextAsync(filePath, cancellationToken);
                    templates.Add(new StoredTemplate(
                        Path.GetFileNameWithoutExtension(filePath),
                        ChartTemplateSerializer.Deserialize(json)));
                }
                catch (Exception exception)
                    when (exception is TemplateFormatException or IOException or UnauthorizedAccessException)
                {
                    failures.Add(new TemplateLoadFailure(fileName, exception.Message));
                }
            }

            return new TemplateStoreListResult(templates, failures);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<StoredTemplate> SaveAsync(
        string? id,
        ChartTemplate template,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(template);

        // 検証(TemplateFormatException)をシリアライズと同時に行う
        string json = ChartTemplateSerializer.Serialize(template);

        await _gate.WaitAsync(cancellationToken);
        try
        {
            Directory.CreateDirectory(_directoryPath);

            string resolvedId = id is null ? GenerateId(template.Name) : ValidateId(id);
            string filePath = GetFilePath(resolvedId);

            string temporaryFilePath = $"{filePath}.{Guid.NewGuid():N}.tmp";
            try
            {
                await File.WriteAllTextAsync(temporaryFilePath, json, cancellationToken);
                File.Move(temporaryFilePath, filePath, overwrite: true);
            }
            catch
            {
                try
                {
                    File.Delete(temporaryFilePath);
                }
                catch (IOException)
                {
                    // 元例外を優先する。孤児 .tmp は次回保存の障害にならない
                }

                throw;
            }

            return new StoredTemplate(resolvedId, template);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task DeleteAsync(string id, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);

        string filePath = GetFilePath(ValidateId(id));

        await _gate.WaitAsync(cancellationToken);
        try
        {
            // ディレクトリ未作成時の DirectoryNotFoundException を避け、冪等な削除にする
            if (File.Exists(filePath))
            {
                File.Delete(filePath);
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    private string GetFilePath(string id) => Path.Combine(_directoryPath, id + FileExtension);

    // ID はファイル名(拡張子なし)。外部から渡される値がディレクトリ外を指せないことを保証する
    private static string ValidateId(string id)
    {
        if (id.Length == 0
            || id != id.Trim()
            || id.EndsWith('.')
            || id.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
        {
            throw new ArgumentException($"テンプレート ID に使えない文字が含まれています: {id}", nameof(id));
        }

        return id;
    }

    private string GenerateId(string name)
    {
        char[] invalidChars = Path.GetInvalidFileNameChars();
        string sanitized = new([.. name.Select(c => invalidChars.Contains(c) ? '_' : c)]);
        sanitized = sanitized.Trim().TrimEnd('.');
        if (sanitized.Length == 0)
        {
            sanitized = "template";
        }

        string candidate = sanitized;
        for (int suffix = 2; File.Exists(GetFilePath(candidate)); suffix++)
        {
            candidate = $"{sanitized}-{suffix}";
        }

        return candidate;
    }
}
