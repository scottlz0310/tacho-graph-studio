using System.Collections.ObjectModel;

using CommunityToolkit.Mvvm.ComponentModel;

using TachoGraphStudio.Core.Imaging;
using TachoGraphStudio.Core.Roster;
using TachoGraphStudio.Core.Templates;

namespace TachoGraphStudio.App.Stage;

public sealed partial class StageViewModel : ObservableObject
{
    private const string PrintDateFormat = "yyyy/MM/dd";

    private readonly IImageSourceFactory _imageSourceFactory;
    private readonly IStagePipeline _pipeline;
    private readonly ITemplateStore _templateStore;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelectedDisc))]
    public partial DiscWorkItem? SelectedDisc { get; set; }

    [ObservableProperty]
    public partial bool IsPreviewFullscreen { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsImportEnabled))]
    public partial bool IsImporting { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasImportError))]
    public partial string? ImportError { get; set; }

    [ObservableProperty]
    public partial bool IsEmptyStateVisible { get; set; } = true;

    // 処理対象日(FR-14)。変更すると全円盤の印字日付へ一括同期する。
    // 個別の手修正(FR-15)は DiscMetadata.PrintDate 側で行う
    [ObservableProperty]
    public partial DateOnly TargetDate { get; set; }

    // 手書きスキップ(FR-17)。トップバーで一括指定し全円盤に適用する(アーキテクチャ §4)
    [ObservableProperty]
    public partial bool SkipHandwritten { get; set; }

    // チャート紙様式の選択(FR-16)。文字入れ位置はテンプレート定義に従う
    [ObservableProperty]
    public partial StoredTemplate? SelectedTemplate { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasTemplateWarning))]
    public partial string? TemplateWarning { get; set; }

    public StageViewModel(
        IStagePipeline pipeline,
        IImageSourceFactory imageSourceFactory,
        ITemplateStore templateStore,
        DateOnly? initialTargetDate = null)
    {
        ArgumentNullException.ThrowIfNull(pipeline);
        ArgumentNullException.ThrowIfNull(imageSourceFactory);
        ArgumentNullException.ThrowIfNull(templateStore);

        _pipeline = pipeline;
        _imageSourceFactory = imageSourceFactory;
        _templateStore = templateStore;
        TargetDate = initialTargetDate ?? DateOnly.FromDateTime(DateTime.Now);
    }

    public ObservableCollection<DiscWorkItem> Discs { get; } = [];

    public ObservableCollection<StoredTemplate> Templates { get; } = [];

    public bool HasTemplateWarning => TemplateWarning is not null;

    public bool IsImportEnabled => !IsImporting;

    public bool HasImportError => ImportError is not null;

    public bool HasSelectedDisc => SelectedDisc is not null;

    public void ResetRotation()
    {
        if (SelectedDisc is not null)
        {
            SelectedDisc.RotationAngle = 0;
        }
    }

    // 名簿の行選択を選択中円盤のメタデータへ反映する(FR-13)
    public void ApplyRosterEntry(RosterEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);

        if (SelectedDisc is not { Metadata: { } metadata })
        {
            return;
        }

        metadata.RegistrationNumber = entry.RegistrationNumber;
        metadata.DriverName = entry.Driver;
        metadata.VehicleType = entry.VehicleType;
    }

    // テンプレート一覧の読込(FR-16)。一部の読込失敗は警告に留め、名簿・ステージ機能は継続する。
    // 再読込では選択中の様式(Id)を維持し、削除されていた場合のみ先頭へフォールバックする
    public async Task LoadTemplatesAsync(CancellationToken cancellationToken = default)
    {
        string? previousSelectedId = SelectedTemplate?.Id;

        TemplateWarning = null;
        Templates.Clear();
        SelectedTemplate = null;

        try
        {
            TemplateStoreListResult result = await _templateStore.ListAsync(cancellationToken);

            foreach (StoredTemplate stored in result.Templates)
            {
                Templates.Add(stored);
            }

            if (result.Failures.Count > 0)
            {
                TemplateWarning = "読み込めなかったテンプレートがあります: "
                    + string.Join(" / ", result.Failures.Select(
                        failure => $"{failure.FileName}({failure.Message})"));
            }

            SelectedTemplate = Templates.FirstOrDefault(stored => stored.Id == previousSelectedId)
                ?? Templates.FirstOrDefault();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            TemplateWarning = $"テンプレートの読み込みに失敗しました: {exception.Message}";
        }
    }

    partial void OnTargetDateChanged(DateOnly value)
    {
        foreach (DiscWorkItem disc in Discs)
        {
            disc.Metadata.PrintDate = value.ToString(PrintDateFormat);
        }
    }

    partial void OnSkipHandwrittenChanged(bool value)
    {
        foreach (DiscWorkItem disc in Discs)
        {
            disc.Metadata.SkipHandwritten = value;
        }
    }

    public async Task ImportAsync(IReadOnlyList<string> paths, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(paths);
        if (paths.Count == 0 || IsImporting)
        {
            return;
        }

        IsImporting = true;
        ImportError = null;
        SelectedDisc = null;
        Discs.Clear();
        IsEmptyStateVisible = false;

        try
        {
            await foreach (ProcessedDisc disc in _pipeline.ProcessAsync(paths, cancellationToken))
            {
                DiscWorkItem item = new(Discs.Count + 1, disc)
                {
                    Thumbnail = _imageSourceFactory.Create(
                        disc.ThumbnailPremultipliedBgra,
                        disc.ThumbnailWidth,
                        disc.ThumbnailHeight),
                    Preview = _imageSourceFactory.Create(disc.PremultipliedBgra, disc.Width, disc.Height),
                };
                item.Metadata.PrintDate = TargetDate.ToString(PrintDateFormat);
                item.Metadata.SkipHandwritten = SkipHandwritten;
                Discs.Add(item);
                SelectedDisc ??= item;
            }
        }
        catch (Exception exception)
            when (exception is SheetLoadException or DiscSplitException or BackgroundRemovalException)
        {
            ImportError = exception.Message;
        }
        finally
        {
            IsImporting = false;
            IsEmptyStateVisible = Discs.Count == 0;
        }
    }
}
