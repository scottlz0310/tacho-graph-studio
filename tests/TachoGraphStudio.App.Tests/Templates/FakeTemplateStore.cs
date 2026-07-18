using TachoGraphStudio.Core.Templates;

namespace TachoGraphStudio.App.Tests.Templates;

// TemplateEditorViewModel テスト用のインメモリストア
internal sealed class FakeTemplateStore : ITemplateStore
{
    private readonly Dictionary<string, ChartTemplate> _templates = [];

    public IReadOnlyList<TemplateLoadFailure> ListFailures { get; set; } = [];

    // 設定すると次の操作でこの例外を投げる
    public Exception? NextException { get; set; }

    public IReadOnlyDictionary<string, ChartTemplate> Saved => _templates;

    public List<string> DeletedIds { get; } = [];

    public Task<TemplateStoreListResult> ListAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfConfigured();
        return Task.FromResult(new TemplateStoreListResult(
            [.. _templates
                .OrderBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase)
                .Select(pair => new StoredTemplate(pair.Key, pair.Value))],
            ListFailures));
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
