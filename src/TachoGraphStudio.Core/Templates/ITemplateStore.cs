namespace TachoGraphStudio.Core.Templates;

// テンプレートの永続化(FR-24)。ID はストア内で一意な識別子で、リネームしても変わらない
public interface ITemplateStore
{
    Task<TemplateStoreListResult> ListAsync(CancellationToken cancellationToken = default);

    // id が null の場合は新規保存し、テンプレート名から ID を生成する
    Task<StoredTemplate> SaveAsync(
        string? id,
        ChartTemplate template,
        CancellationToken cancellationToken = default);

    // 存在しない ID の削除は何もしない(冪等)
    Task DeleteAsync(string id, CancellationToken cancellationToken = default);
}

public sealed record StoredTemplate(string Id, ChartTemplate Template);

// 一部のファイルが壊れていても残りのテンプレートは利用できるよう、読込失敗は例外ではなく結果で返す
public sealed record TemplateStoreListResult(
    IReadOnlyList<StoredTemplate> Templates,
    IReadOnlyList<TemplateLoadFailure> Failures);

public sealed record TemplateLoadFailure(string FileName, string Message);
