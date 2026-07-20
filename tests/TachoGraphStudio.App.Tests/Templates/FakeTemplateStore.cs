using TachoGraphStudio.Core.Templates;

namespace TachoGraphStudio.App.Tests.Templates;

// TemplateEditorViewModel テスト用のインメモリストア
internal sealed class FakeTemplateStore : ITemplateStore
{
    private readonly Dictionary<string, ChartTemplate> _templates = [];

    public IReadOnlyList<TemplateLoadFailure> ListFailures { get; set; } = [];

    // 設定すると次の操作でこの例外を投げる
    public Exception? NextException { get; set; }

    // 設定すると ListAsync がこのシグナルの完了まで待機する(呼び出し中の状態遷移をテストするため)
    public TaskCompletionSource<bool>? ListGate { get; set; }

    public IReadOnlyDictionary<string, ChartTemplate> Saved => _templates;

    public List<string> DeletedIds { get; } = [];

    public async Task<TemplateStoreListResult> ListAsync(CancellationToken cancellationToken = default)
    {
        if (ListGate is { } gate)
        {
            await gate.Task;
        }

        ThrowIfConfigured();
        return new TemplateStoreListResult(
            [.. _templates
                .OrderBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase)
                .Select(pair => new StoredTemplate(pair.Key, pair.Value))],
            ListFailures);
    }

    public Task<StoredTemplate> SaveAsync(
        string? id,
        ChartTemplate template,
        CancellationToken cancellationToken = default)
    {
        ThrowIfConfigured();
        ChartTemplateSerializer.Serialize(template);

        string resolvedId = id ?? GenerateId(template.Name);
        _templates[resolvedId] = template;
        return Task.FromResult(new StoredTemplate(resolvedId, template));
    }

    public Task DeleteAsync(string id, CancellationToken cancellationToken = default)
    {
        ThrowIfConfigured();
        _templates.Remove(id);
        DeletedIds.Add(id);
        return Task.CompletedTask;
    }

    // 設定すると ExportAllAsync がこの結果を返す(未設定時は全件成功)
    public TemplateExportResult? NextExportResult { get; set; }

    public List<string> ExportedDirectories { get; } = [];

    public Task<TemplateExportResult> ExportAllAsync(
        string destinationDirectoryPath,
        CancellationToken cancellationToken = default)
    {
        ThrowIfConfigured();
        ExportedDirectories.Add(destinationDirectoryPath);
        return Task.FromResult(
            NextExportResult ?? new TemplateExportResult(_templates.Count, []));
    }

    private string GenerateId(string name)
    {
        string candidate = name;
        for (int suffix = 2; _templates.ContainsKey(candidate); suffix++)
        {
            candidate = $"{name}-{suffix}";
        }

        return candidate;
    }

    private void ThrowIfConfigured()
    {
        if (NextException is { } exception)
        {
            NextException = null;
            throw exception;
        }
    }
}
