using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;

using CommunityToolkit.Mvvm.ComponentModel;

using TachoGraphStudio.Core.Imaging;
using TachoGraphStudio.Core.Naming;
using TachoGraphStudio.Core.Roster;
using TachoGraphStudio.Core.Templates;

namespace TachoGraphStudio.App.Stage;

public sealed partial class StageViewModel : ObservableObject
{
    // 書式中の "/" はカルチャの日付区切り文字へ置換されるため、必ず
    // InvariantCulture で整形する(Windows の地域設定に表示が左右されないように)
    private const string PrintDateFormat = "yyyy/MM/dd";

    private readonly IImageSourceFactory _imageSourceFactory;
    private readonly IStagePipeline _pipeline;
    private readonly ITemplateStore _templateStore;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelectedDisc))]
    [NotifyPropertyChangedFor(nameof(SaveTargetLabel))]
    [NotifyPropertyChangedFor(nameof(CanSave))]
    public partial DiscWorkItem? SelectedDisc { get; set; }

    // 出力先ディレクトリ(FR-19)。永続化は #15、当面はセッション内で保持する
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SaveTargetLabel))]
    [NotifyPropertyChangedFor(nameof(CanSave))]
    public partial string? OutputDirectory { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanSave))]
    public partial bool IsSaving { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSaveError))]
    public partial string? SaveError { get; set; }

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

    // 手書きスキップ(FR-17)。トップバーの一括設定兼、新規取込時の既定値
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanSave))]
    public partial bool SkipHandwritten { get; set; }

    // 選択中円盤のチャート紙様式(FR-16、#43)。円盤ごとに DiscMetadata.SelectedTemplateId として
    // 保持し、SelectedDisc の切替に追従する。文字入れ位置はテンプレート定義に従う
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanSave))]
    public partial StoredTemplate? SelectedTemplate { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasTemplateWarning))]
    public partial string? TemplateWarning { get; set; }

    // トップバーのテンプレート選択 ComboBox 用。実テンプレートに加え、末尾に
    // TemplateEditEntry.Instance(「テンプレート登録・編集」導線)を含む(#43)
    public ObservableCollection<object> TemplateSelectionItems { get; } = [];

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

    public bool HasSaveError => SaveError is not null;

    // 文字なし保存は手書きスキップ時のみ許可する(FR-17)。様式未選択のまま
    // 文字なし PNG を成功扱いで保存しない
    public bool CanSave => SelectedDisc is not null
        && OutputDirectory is not null
        && !IsSaving
        && (SelectedDisc.Metadata.SkipHandwritten || SelectedTemplate is not null);

    // 保存前のファイル名プレビュー(FR-20)。メタデータの編集にリアルタイム追従する
    public string SaveTargetLabel
    {
        get
        {
            if (SelectedDisc is not { Metadata: { } metadata })
            {
                return "";
            }

            string fileName = OutputNaming.CreateFileName(
                metadata.PrintDate,
                metadata.RegistrationNumber,
                metadata.DriverName,
                metadata.SkipHandwritten);

            return OutputDirectory is null
                ? $"保存先: (未選択) {fileName}"
                : $"保存先: {Path.Combine(OutputDirectory, fileName)}";
        }
    }

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
    // 再読込では選択中の様式(Id)を維持し、削除されていた場合のみ先頭へフォールバックする。
    // 復元元は選択中円盤の保存値を優先する(SelectedTemplate は読込中に一時的に null になるが、
    // 円盤のメタデータは読込失敗時も変更しないため、失敗後の再試行でも元の選択を復元できる)
    public async Task LoadTemplatesAsync(CancellationToken cancellationToken = default)
    {
        // 読込対象の円盤を参照として固定する。await 中に SelectedDisc が別の円盤へ
        // 切り替わっても、この円盤自身の補正とは独立して扱う(#43 レビュー指摘)
        DiscWorkItem? targetDisc = SelectedDisc;
        string? previousSelectedId = targetDisc?.Metadata.SelectedTemplateId ?? SelectedTemplate?.Id;

        TemplateWarning = null;
        Templates.Clear();
        SelectedTemplate = null;
        // ComboBox の候補一覧(TemplateSelectionItems)を先に空(編集エントリのみ)へ確定させる。
        // SelectedTemplate への代入は ItemsSource に存在しない値の選択を試みることになり、
        // Items 未構築時の SelectedItem 設定は WinUI 側で例外・表示不整合の原因になるため、
        // 常に「候補一覧の確定 → SelectedTemplate の代入」の順を守る(#43 レビュー指摘)
        RebuildTemplateSelectionItems();

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

            // SelectedTemplate へ触れる前に候補一覧を新しい Templates で再確定させる
            RebuildTemplateSelectionItems();

            if (targetDisc is not null)
            {
                // 読込対象だった円盤の保存値を補正する(previousSelectedId が削除等で解決
                // できない場合のみフォールバック結果に変わる)。await 中に選択が別の円盤へ
                // 切り替わっていても、この円盤自身の補正は独立して行う
                StoredTemplate? resolvedForTarget =
                    Templates.FirstOrDefault(stored => stored.Id == previousSelectedId)
                    ?? Templates.FirstOrDefault();
                targetDisc.Metadata.SelectedTemplateId = resolvedForTarget?.Id;
            }

            if (SelectedDisc is { } currentDisc)
            {
                // VM の表示値は「現在選択中」の円盤(await 中に切り替わっている可能性がある)
                // へ同期する。targetDisc と同一なら直前の補正結果と一致する
                SyncSelectedTemplateFromDisc(currentDisc);
            }
            else
            {
                SelectedTemplate = Templates.FirstOrDefault(stored => stored.Id == previousSelectedId)
                    ?? Templates.FirstOrDefault();
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // 失敗時は円盤のメタデータに触れない。previousSelectedId は円盤の保存値から
            // 取得しているため、次回の再試行でも元の選択を正しく復元できる。候補一覧は
            // 冒頭で既に空へ確定済みのため、ここで再度 RebuildTemplateSelectionItems は不要
            TemplateWarning = $"テンプレートの読み込みに失敗しました: {exception.Message}";
        }
    }

    // ユーザーによる明示的なテンプレート選択(トップバー ComboBox 操作、#43)。
    // 選択中円盤のメタデータへ書き戻す。内部的なリセット(ImportAsync・LoadTemplatesAsync・
    // 円盤切替時の再同期)はこのメソッドを経由せず SelectedTemplate を直接設定するため、
    // 意図しない書き戻しは発生しない
    public void SelectTemplateForSelectedDisc(StoredTemplate? template)
    {
        SelectedTemplate = template;
        if (SelectedDisc is { } disc)
        {
            disc.Metadata.SelectedTemplateId = template?.Id;
        }
    }

    private void RebuildTemplateSelectionItems()
    {
        TemplateSelectionItems.Clear();
        foreach (StoredTemplate stored in Templates)
        {
            TemplateSelectionItems.Add(stored);
        }

        TemplateSelectionItems.Add(TemplateEditEntry.Instance);
    }

    // 確定保存して次へ(FR-19〜21): 回転・文字入れを本合成した透過 PNG を保存し、
    // 円盤を処理済みにして次の未処理円盤へ自動遷移する。同名ファイルは上書きする
    public async Task<bool> SaveAndAdvanceAsync(CancellationToken cancellationToken = default)
    {
        if (SelectedDisc is not { } item || OutputDirectory is not { } outputDirectory || IsSaving)
        {
            return false;
        }

        // 保存中の編集が結果に混ざらないよう、合成入力とファイル名は最初の await より前に
        // 同一スナップショットへ固定する
        DiscMetadata metadata = item.Metadata;
        bool skipHandwritten = metadata.SkipHandwritten;
        ChartTemplate? template = skipHandwritten ? null : SelectedTemplate?.Template;

        // 文字なし合成は手書きスキップ時のみ許可する(FR-17)。様式未選択の保存は拒否
        if (!skipHandwritten && template is null)
        {
            return false;
        }

        ChartTextValues? values = skipHandwritten ? null : metadata.ToTextValues();
        string fileName = OutputNaming.CreateFileName(
            metadata.PrintDate,
            metadata.RegistrationNumber,
            metadata.DriverName,
            skipHandwritten);
        double angle = item.RotationAngle;
        ProcessedDisc disc = item.Disc;

        IsSaving = true;
        SaveError = null;
        try
        {
            byte[] png = await Task.Run(
                () => DiscComposer.ComposePng(disc.Bgra, disc.Width, disc.Height, angle, template, values),
                cancellationToken);

            Directory.CreateDirectory(outputDirectory);
            await ReplaceFileAsync(Path.Combine(outputDirectory, fileName), png, cancellationToken);

            item.Status = DiscStatus.Done;
            AdvanceToNextDisc(item);
            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            SaveError = $"保存に失敗しました: {exception.Message}";
            return false;
        }
        finally
        {
            IsSaving = false;
        }
    }

    // 同一ディレクトリの一時ファイルへ書き切ってから置換し、途中失敗で既存の成果物を
    // 破損させない(AtomicJsonFile と同じ一時ファイル + Move パターン)
    private static async Task ReplaceFileAsync(
        string filePath,
        byte[] content,
        CancellationToken cancellationToken)
    {
        string temporaryFilePath = $"{filePath}.{Guid.NewGuid():N}.tmp";
        try
        {
            await File.WriteAllBytesAsync(temporaryFilePath, content, cancellationToken);
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
    }

    // 現在の円盤より後ろの最初の未処理へ、なければ先頭側の未処理へ遷移する。
    // 未処理が残っていなければ現在位置に留まる(FR-21)
    private void AdvanceToNextDisc(DiscWorkItem current)
    {
        int index = Discs.IndexOf(current);
        DiscWorkItem? next = Discs.Skip(index + 1)
            .Concat(Discs.Take(index + 1))
            .FirstOrDefault(disc => disc.Status == DiscStatus.Pending);
        if (next is not null)
        {
            SelectedDisc = next;
        }
    }

    partial void OnSelectedDiscChanged(DiscWorkItem? oldValue, DiscWorkItem? newValue)
    {
        // ファイル名プレビューがメタデータの編集に追従するよう購読を張り替える
        if (oldValue is not null)
        {
            oldValue.Metadata.PropertyChanged -= OnSelectedDiscMetadataPropertyChanged;
        }

        if (newValue is not null)
        {
            newValue.Metadata.PropertyChanged += OnSelectedDiscMetadataPropertyChanged;
        }

        // SelectedTemplate を新しい円盤の選択(DiscMetadata.SelectedTemplateId)へ同期する(#43)
        if (newValue is not null)
        {
            SyncSelectedTemplateFromDisc(newValue);
        }
        else
        {
            SelectedTemplate = null;
        }
    }

    // SelectedTemplate の setter に書き戻し副作用はないため、直接代入するだけでよい
    private void SyncSelectedTemplateFromDisc(DiscWorkItem disc)
    {
        string? templateId = disc.Metadata.SelectedTemplateId;
        SelectedTemplate = templateId is null
            ? null
            : Templates.FirstOrDefault(stored => stored.Id == templateId);
    }

    private void OnSelectedDiscMetadataPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        OnPropertyChanged(nameof(SaveTargetLabel));
        if (e.PropertyName == nameof(DiscMetadata.SkipHandwritten))
        {
            OnPropertyChanged(nameof(CanSave));
        }
    }

    partial void OnTargetDateChanged(DateOnly value)
    {
        foreach (DiscWorkItem disc in Discs)
        {
            disc.Metadata.PrintDate = value.ToString(PrintDateFormat, CultureInfo.InvariantCulture);
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

        // 直近の選択(新規円盤の初期値、#43)は SelectedDisc = null で失われる前に退避する
        string? lastUsedTemplateId = SelectedTemplate?.Id;

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
                    // フル解像度はストレートアルファで保持しているため、表示用にのみ変換する
                    Preview = _imageSourceFactory.Create(
                        PremultipliedAlpha.FromStraightBgra(disc.Bgra),
                        disc.Width,
                        disc.Height),
                };
                item.Metadata.PrintDate = TargetDate.ToString(PrintDateFormat, CultureInfo.InvariantCulture);
                item.Metadata.SkipHandwritten = SkipHandwritten;
                // 直近の選択(または起動時に復元した既定)を新規円盤の初期値として引き継ぐ(#43)
                item.Metadata.SelectedTemplateId = lastUsedTemplateId;
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
