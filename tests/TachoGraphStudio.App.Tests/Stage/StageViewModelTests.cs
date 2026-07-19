using System.Runtime.CompilerServices;

using Microsoft.UI.Xaml.Media;

using TachoGraphStudio.App.Stage;
using TachoGraphStudio.App.Tests.Templates;
using TachoGraphStudio.Core.Imaging;
using TachoGraphStudio.Core.Roster;
using TachoGraphStudio.Core.Templates;

namespace TachoGraphStudio.App.Tests.Stage;

public sealed class StageViewModelTests : IDisposable
{
    private readonly string _temporaryDirectory = Path.Combine(
        Path.GetTempPath(),
        $"TachoGraphStudio.Tests-{Guid.NewGuid():N}");

    public void Dispose()
    {
        if (Directory.Exists(_temporaryDirectory))
        {
            Directory.Delete(_temporaryDirectory, recursive: true);
        }
    }

    private static StageViewModel CreateViewModel(FakeStagePipeline pipeline) => new(
        pipeline,
        new NullImageSourceFactory(),
        new FakeTemplateStore(),
        new DateOnly(2026, 7, 19));

    [Theory]
    [InlineData(1)]
    [InlineData(3)]
    public async Task ImportAsync_PopulatesDiscsInOrderAndSelectsFirst(int discCount)
    {
        FakeStagePipeline pipeline = new()
        {
            Discs = [.. Enumerable.Range(0, discCount).Select(BuildDisc)],
        };
        StageViewModel viewModel = CreateViewModel(pipeline);

        await viewModel.ImportAsync(["sheet.pdf"]);

        Assert.Equal(discCount, viewModel.Discs.Count);
        for (int index = 0; index < discCount; index++)
        {
            Assert.Equal(index + 1, viewModel.Discs[index].Number);
            Assert.Equal(DiscStatus.Pending, viewModel.Discs[index].Status);
        }

        Assert.Same(viewModel.Discs[0], viewModel.SelectedDisc);
        Assert.False(viewModel.IsImporting);
        Assert.False(viewModel.HasImportError);
        Assert.False(viewModel.IsEmptyStateVisible);
    }

    [Fact]
    public async Task ImportAsync_PipelineErrorKeepsPartialDiscsAndSetsError()
    {
        FakeStagePipeline pipeline = new()
        {
            Discs = [BuildDisc(0)],
            ThrowAfterDiscs = new DiscSplitException("2 ページ目の分割に失敗"),
        };
        StageViewModel viewModel = CreateViewModel(pipeline);

        await viewModel.ImportAsync(["sheet.pdf"]);

        Assert.Single(viewModel.Discs);
        Assert.True(viewModel.HasImportError);
        Assert.Contains("分割に失敗", viewModel.ImportError);
        Assert.False(viewModel.IsImporting);
        Assert.False(viewModel.IsEmptyStateVisible);
    }

    [Fact]
    public async Task ImportAsync_ErrorWithNoDiscsShowsEmptyState()
    {
        FakeStagePipeline pipeline = new()
        {
            ThrowAfterDiscs = new SheetLoadException("未対応のファイル形式"),
        };
        StageViewModel viewModel = CreateViewModel(pipeline);

        await viewModel.ImportAsync(["sheet.bmp"]);

        Assert.Empty(viewModel.Discs);
        Assert.True(viewModel.HasImportError);
        Assert.True(viewModel.IsEmptyStateVisible);
    }

    [Fact]
    public async Task ImportAsync_SecondImportReplacesDiscsAndResetsSelection()
    {
        FakeStagePipeline pipeline = new() { Discs = [BuildDisc(0), BuildDisc(1)] };
        StageViewModel viewModel = CreateViewModel(pipeline);
        await viewModel.ImportAsync(["first.pdf"]);
        DiscWorkItem firstSelection = Assert.IsType<DiscWorkItem>(viewModel.SelectedDisc);
        viewModel.SelectedDisc = viewModel.Discs[1];

        pipeline.Discs = [BuildDisc(2)];
        await viewModel.ImportAsync(["second.pdf"]);

        DiscWorkItem item = Assert.Single(viewModel.Discs);
        Assert.Equal(1, item.Number);
        Assert.Same(item, viewModel.SelectedDisc);
        Assert.NotSame(firstSelection, viewModel.SelectedDisc);
    }

    [Fact]
    public async Task ResetRotation_ResetsSelectedDiscOnly()
    {
        FakeStagePipeline pipeline = new() { Discs = [BuildDisc(0), BuildDisc(1)] };
        StageViewModel viewModel = CreateViewModel(pipeline);
        await viewModel.ImportAsync(["sheet.pdf"]);
        viewModel.Discs[0].RotationAngle = 45.0;
        viewModel.Discs[1].RotationAngle = -30.0;

        viewModel.SelectedDisc = viewModel.Discs[0];
        viewModel.ResetRotation();

        Assert.Equal(0.0, viewModel.Discs[0].RotationAngle);
        Assert.Equal(-30.0, viewModel.Discs[1].RotationAngle);
    }

    [Fact]
    public void ResetRotation_WithoutSelectionDoesNothing()
    {
        StageViewModel viewModel = CreateViewModel(new FakeStagePipeline());

        viewModel.ResetRotation();

        Assert.False(viewModel.HasSelectedDisc);
    }

    [Fact]
    public async Task HasSelectedDisc_FollowsSelection()
    {
        FakeStagePipeline pipeline = new() { Discs = [BuildDisc(0)] };
        StageViewModel viewModel = CreateViewModel(pipeline);
        Assert.False(viewModel.HasSelectedDisc);

        await viewModel.ImportAsync(["sheet.pdf"]);
        Assert.True(viewModel.HasSelectedDisc);

        viewModel.SelectedDisc = null;
        Assert.False(viewModel.HasSelectedDisc);
    }

    [Fact]
    public async Task ImportAsync_EmptyPathsDoesNothing()
    {
        FakeStagePipeline pipeline = new() { Discs = [BuildDisc(0)] };
        StageViewModel viewModel = CreateViewModel(pipeline);

        await viewModel.ImportAsync([]);

        Assert.Empty(viewModel.Discs);
        Assert.Equal(0, pipeline.ProcessCallCount);
        Assert.True(viewModel.IsEmptyStateVisible);
    }

    [Fact]
    public async Task ImportAsync_InitializesPrintDateFromTargetDate()
    {
        FakeStagePipeline pipeline = new() { Discs = [BuildDisc(0), BuildDisc(1)] };
        StageViewModel viewModel = CreateViewModel(pipeline);

        await viewModel.ImportAsync(["sheet.pdf"]);

        Assert.All(viewModel.Discs, disc => Assert.Equal("2026/07/19", disc.Metadata.PrintDate));
    }

    [Fact]
    public async Task TargetDateChange_SyncsPrintDateToAllDiscs()
    {
        FakeStagePipeline pipeline = new() { Discs = [BuildDisc(0), BuildDisc(1)] };
        StageViewModel viewModel = CreateViewModel(pipeline);
        await viewModel.ImportAsync(["sheet.pdf"]);
        // 個別の手修正(FR-15)は次の一括指定までは保持される
        viewModel.Discs[1].Metadata.PrintDate = "2026/01/01";

        viewModel.TargetDate = new DateOnly(2026, 12, 25);

        Assert.All(viewModel.Discs, disc => Assert.Equal("2026/12/25", disc.Metadata.PrintDate));
    }

    [Fact]
    public async Task SkipHandwrittenChange_SyncsToAllDiscsAcrossSelection()
    {
        FakeStagePipeline pipeline = new() { Discs = [BuildDisc(0), BuildDisc(1)] };
        StageViewModel viewModel = CreateViewModel(pipeline);
        await viewModel.ImportAsync(["sheet.pdf"]);
        // 選択切替をまたいでも、一括指定は未選択の円盤へも反映される(FR-17、アーキテクチャ §4)
        viewModel.SelectedDisc = viewModel.Discs[1];

        viewModel.SkipHandwritten = true;

        Assert.All(viewModel.Discs, disc => Assert.True(disc.Metadata.SkipHandwritten));

        viewModel.SkipHandwritten = false;

        Assert.All(viewModel.Discs, disc => Assert.False(disc.Metadata.SkipHandwritten));
    }

    [Fact]
    public async Task ImportAsync_InitializesSkipHandwrittenFromBulkSetting()
    {
        FakeStagePipeline pipeline = new() { Discs = [BuildDisc(0)] };
        StageViewModel viewModel = CreateViewModel(pipeline);
        viewModel.SkipHandwritten = true;

        await viewModel.ImportAsync(["sheet.pdf"]);

        Assert.True(viewModel.Discs[0].Metadata.SkipHandwritten);
    }

    [Fact]
    public async Task IndividualSkipHandwrittenChange_AffectsSelectedDiscOnlyAndCanSave()
    {
        FakeStagePipeline pipeline = new() { Discs = [BuildDisc(0), BuildDisc(1)] };
        StageViewModel viewModel = CreateViewModel(pipeline);
        await viewModel.ImportAsync(["sheet.pdf"]);
        viewModel.OutputDirectory = _temporaryDirectory;
        viewModel.Discs[0].Metadata.DriverName = "山田 太郎";
        List<string?> changedProperties = [];
        viewModel.PropertyChanged += (_, e) => changedProperties.Add(e.PropertyName);

        viewModel.Discs[0].Metadata.SkipHandwritten = true;

        Assert.True(viewModel.CanSave);
        Assert.Contains(nameof(StageViewModel.CanSave), changedProperties);
        Assert.Contains(nameof(StageViewModel.SaveTargetLabel), changedProperties);
        Assert.Contains("20260719__山田 太郎_手書き.png", viewModel.SaveTargetLabel, StringComparison.Ordinal);
        Assert.True(viewModel.Discs[0].Metadata.SkipHandwritten);
        Assert.False(viewModel.Discs[1].Metadata.SkipHandwritten);
        Assert.False(viewModel.SkipHandwritten);

        viewModel.SelectedDisc = viewModel.Discs[1];

        Assert.False(viewModel.CanSave);
    }

    [Fact]
    public async Task ApplyRosterEntry_UpdatesSelectedDiscOnly()
    {
        FakeStagePipeline pipeline = new() { Discs = [BuildDisc(0), BuildDisc(1)] };
        StageViewModel viewModel = CreateViewModel(pipeline);
        await viewModel.ImportAsync(["sheet.pdf"]);
        viewModel.SelectedDisc = viewModel.Discs[1];

        viewModel.ApplyRosterEntry(new RosterEntry
        {
            ControlNumber = 2,
            Detail = "除雪グレーダ",
            Specification = "3.7m",
            RegistrationNumber = "旭川123-45",
            VehicleType = "グレーダ",
            Driver = "山田 太郎",
            WorkPeriod = "winter",
            UpdatedAt = DateTimeOffset.UnixEpoch,
            IsTachoTarget = true,
        });

        DiscMetadata updated = viewModel.Discs[1].Metadata;
        Assert.Equal("旭川123-45", updated.RegistrationNumber);
        Assert.Equal("山田 太郎", updated.DriverName);
        Assert.Equal("グレーダ", updated.VehicleType);
        Assert.Equal("", viewModel.Discs[0].Metadata.RegistrationNumber);
    }

    [Fact]
    public void ApplyRosterEntry_WithoutSelectionDoesNothing()
    {
        StageViewModel viewModel = CreateViewModel(new FakeStagePipeline());

        viewModel.ApplyRosterEntry(new RosterEntry
        {
            ControlNumber = 1,
            Detail = "d",
            Specification = "s",
            RegistrationNumber = "r",
            VehicleType = "v",
            Driver = "d",
            WorkPeriod = "winter",
            UpdatedAt = DateTimeOffset.UnixEpoch,
            IsTachoTarget = true,
        });

        Assert.False(viewModel.HasSelectedDisc);
    }

    [Fact]
    public async Task LoadTemplatesAsync_PopulatesAndSelectsFirst()
    {
        FakeTemplateStore store = new();
        await store.SaveAsync(id: null, BuildTemplate("Task-Meter"));
        await store.SaveAsync(id: null, BuildTemplate("Yazaki45"));
        StageViewModel viewModel = new(
            new FakeStagePipeline(), new NullImageSourceFactory(), store, new DateOnly(2026, 7, 19));

        await viewModel.LoadTemplatesAsync();

        Assert.Equal(["Task-Meter", "Yazaki45"], viewModel.Templates.Select(stored => stored.Template.Name));
        Assert.Same(viewModel.Templates[0], viewModel.SelectedTemplate);
        Assert.False(viewModel.HasTemplateWarning);
    }

    [Fact]
    public async Task LoadTemplatesAsync_FailuresBecomeWarning()
    {
        FakeTemplateStore store = new()
        {
            ListFailures = [new TemplateLoadFailure("broken.json", "解析できません")],
        };
        StageViewModel viewModel = new(
            new FakeStagePipeline(), new NullImageSourceFactory(), store, new DateOnly(2026, 7, 19));

        await viewModel.LoadTemplatesAsync();

        Assert.True(viewModel.HasTemplateWarning);
        Assert.Contains("broken.json", viewModel.TemplateWarning, StringComparison.Ordinal);
        Assert.Null(viewModel.SelectedTemplate);
    }

    [Fact]
    public async Task LoadTemplatesAsync_ReloadPreservesSelectionById()
    {
        FakeTemplateStore store = new();
        await store.SaveAsync(id: null, BuildTemplate("Task-Meter"));
        await store.SaveAsync(id: null, BuildTemplate("Yazaki45"));
        StageViewModel viewModel = new(
            new FakeStagePipeline(), new NullImageSourceFactory(), store, new DateOnly(2026, 7, 19));
        await viewModel.LoadTemplatesAsync();
        viewModel.SelectedTemplate = viewModel.Templates[1];

        await viewModel.LoadTemplatesAsync();

        Assert.Equal("Yazaki45", viewModel.SelectedTemplate?.Id);
    }

    [Fact]
    public async Task LoadTemplatesAsync_DeletedSelectionFallsBackToFirst()
    {
        FakeTemplateStore store = new();
        await store.SaveAsync(id: null, BuildTemplate("Task-Meter"));
        await store.SaveAsync(id: null, BuildTemplate("Yazaki45"));
        StageViewModel viewModel = new(
            new FakeStagePipeline(), new NullImageSourceFactory(), store, new DateOnly(2026, 7, 19));
        await viewModel.LoadTemplatesAsync();
        viewModel.SelectedTemplate = viewModel.Templates[1];
        await store.DeleteAsync("Yazaki45");

        await viewModel.LoadTemplatesAsync();

        Assert.Equal("Task-Meter", viewModel.SelectedTemplate?.Id);
    }

    [Fact]
    public async Task LoadTemplatesAsync_StoreFailureBecomesWarning()
    {
        FakeTemplateStore store = new() { NextException = new IOException("ディスクエラー") };
        StageViewModel viewModel = new(
            new FakeStagePipeline(), new NullImageSourceFactory(), store, new DateOnly(2026, 7, 19));

        await viewModel.LoadTemplatesAsync();

        Assert.True(viewModel.HasTemplateWarning);
        Assert.Empty(viewModel.Templates);
    }

    [Fact]
    public async Task LoadTemplatesAsync_RebuildsSelectionItemsBeforeChangingSelectedTemplate()
    {
        // SelectedTemplate への代入(ComboBox.SelectedItem への反映を誘発する)時点で、
        // TemplateSelectionItems(ComboBox の候補一覧)が既に新しい内容へ更新済みであることを
        // 確認する。Items 構築前の SelectedItem 設定は WinUI 側で例外・表示不整合の原因になる
        // ため、この順序を守る必要がある(#43 レビュー指摘)
        FakeTemplateStore store = new();
        await store.SaveAsync(id: null, BuildTemplate("Yazaki45"));
        StageViewModel viewModel = new(
            new FakeStagePipeline(), new NullImageSourceFactory(), store, new DateOnly(2026, 7, 19));
        List<int> selectionItemsCountWhenSelectedTemplateChanged = [];
        viewModel.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(StageViewModel.SelectedTemplate) && viewModel.SelectedTemplate is not null)
            {
                selectionItemsCountWhenSelectedTemplateChanged.Add(viewModel.TemplateSelectionItems.Count);
            }
        };

        await viewModel.LoadTemplatesAsync();

        Assert.NotEmpty(selectionItemsCountWhenSelectedTemplateChanged);
        // 実テンプレート(Yazaki45) + 末尾の編集エントリの 2 件が既に構築済みであること
        Assert.All(selectionItemsCountWhenSelectedTemplateChanged, count => Assert.Equal(2, count));
    }

    [Fact]
    public async Task LoadTemplatesAsync_RebuildsSelectionItemsWithTrailingEditEntry()
    {
        FakeTemplateStore store = new();
        await store.SaveAsync(id: null, BuildTemplate("Task-Meter"));
        await store.SaveAsync(id: null, BuildTemplate("Yazaki45"));
        StageViewModel viewModel = new(
            new FakeStagePipeline(), new NullImageSourceFactory(), store, new DateOnly(2026, 7, 19));

        await viewModel.LoadTemplatesAsync();

        Assert.Equal(
            ["Task-Meter", "Yazaki45", "EditEntry"],
            viewModel.TemplateSelectionItems.Select(
                item => item is StoredTemplate stored ? stored.Template.Name : "EditEntry"));
        Assert.Same(TemplateEditEntry.Instance, viewModel.TemplateSelectionItems[^1]);
    }

    [Fact]
    public async Task LoadTemplatesAsync_EmptyStoreStillExposesEditEntry()
    {
        StageViewModel viewModel = new(
            new FakeStagePipeline(), new NullImageSourceFactory(), new FakeTemplateStore(), new DateOnly(2026, 7, 19));

        await viewModel.LoadTemplatesAsync();

        Assert.Same(TemplateEditEntry.Instance, Assert.Single(viewModel.TemplateSelectionItems));
    }

    [Fact]
    public async Task SelectedDiscChange_SyncsSelectedTemplateFromDiscMetadata()
    {
        FakeTemplateStore store = new();
        await store.SaveAsync(id: null, BuildTemplate("Task-Meter"));
        await store.SaveAsync(id: null, BuildTemplate("Yazaki45"));
        FakeStagePipeline pipeline = new() { Discs = [BuildDisc(0), BuildDisc(1)] };
        StageViewModel viewModel = new(
            pipeline, new NullImageSourceFactory(), store, new DateOnly(2026, 7, 19));
        await viewModel.LoadTemplatesAsync();
        await viewModel.ImportAsync(["sheet.pdf"]);
        // 円盤ごとに異なる様式を選択する(#43)
        viewModel.SelectTemplateForSelectedDisc(viewModel.Templates[1]);
        viewModel.SelectedDisc = viewModel.Discs[1];
        viewModel.SelectTemplateForSelectedDisc(viewModel.Templates[0]);

        viewModel.SelectedDisc = viewModel.Discs[0];
        Assert.Equal("Yazaki45", viewModel.SelectedTemplate?.Id);

        viewModel.SelectedDisc = viewModel.Discs[1];
        Assert.Equal("Task-Meter", viewModel.SelectedTemplate?.Id);
    }

    [Fact]
    public async Task SelectedDiscChange_UnresolvableSavedIdResultsInNullSelection()
    {
        FakeTemplateStore store = new();
        await store.SaveAsync(id: null, BuildTemplate("Yazaki45"));
        FakeStagePipeline pipeline = new() { Discs = [BuildDisc(0)] };
        StageViewModel viewModel = new(
            pipeline, new NullImageSourceFactory(), store, new DateOnly(2026, 7, 19));
        await viewModel.LoadTemplatesAsync();
        await viewModel.ImportAsync(["sheet.pdf"]);
        viewModel.Discs[0].Metadata.SelectedTemplateId = "deleted-template";

        viewModel.SelectedDisc = null;
        viewModel.SelectedDisc = viewModel.Discs[0];

        Assert.Null(viewModel.SelectedTemplate);
    }

    [Fact]
    public async Task LoadTemplatesAsync_FailureThenRetryRestoresSelectedDiscTemplate()
    {
        // 選択済み円盤がある状態で ListAsync が失敗しても円盤のメタデータを変更せず、
        // 再試行(成功)時に元の選択を復元できることを確認する(#43 レビュー指摘)
        FakeTemplateStore store = new();
        await store.SaveAsync(id: null, BuildTemplate("Task-Meter"));
        await store.SaveAsync(id: null, BuildTemplate("Yazaki45"));
        FakeStagePipeline pipeline = new() { Discs = [BuildDisc(0)] };
        StageViewModel viewModel = new(
            pipeline, new NullImageSourceFactory(), store, new DateOnly(2026, 7, 19));
        await viewModel.LoadTemplatesAsync();
        await viewModel.ImportAsync(["sheet.pdf"]);
        viewModel.SelectTemplateForSelectedDisc(viewModel.Templates[1]);

        store.NextException = new IOException("一時的な失敗");
        await viewModel.LoadTemplatesAsync();

        Assert.True(viewModel.HasTemplateWarning);
        Assert.Null(viewModel.SelectedTemplate);
        // 失敗時は円盤側の保存値を変更しない
        Assert.Equal("Yazaki45", viewModel.Discs[0].Metadata.SelectedTemplateId);

        await viewModel.LoadTemplatesAsync();

        Assert.False(viewModel.HasTemplateWarning);
        Assert.Equal("Yazaki45", viewModel.SelectedTemplate?.Id);
        Assert.Equal("Yazaki45", viewModel.Discs[0].Metadata.SelectedTemplateId);
    }

    [Fact]
    public async Task LoadTemplatesAsync_SelectedDiscSwitchedDuringReloadDoesNotOverwriteNewDisc()
    {
        // 読込対象(A)の解決結果が、await 中に切り替わった先の円盤(B)へ誤って
        // 書き戻されないことを確認する(#43 レビュー指摘)
        FakeTemplateStore store = new();
        await store.SaveAsync(id: null, BuildTemplate("Task-Meter"));
        await store.SaveAsync(id: null, BuildTemplate("Yazaki45"));
        FakeStagePipeline pipeline = new() { Discs = [BuildDisc(0), BuildDisc(1)] };
        StageViewModel viewModel = new(
            pipeline, new NullImageSourceFactory(), store, new DateOnly(2026, 7, 19));
        await viewModel.LoadTemplatesAsync();
        await viewModel.ImportAsync(["sheet.pdf"]);
        viewModel.SelectedDisc = viewModel.Discs[0];
        viewModel.SelectTemplateForSelectedDisc(viewModel.Templates[1]); // A(Discs[0]) = Yazaki45
        viewModel.SelectedDisc = viewModel.Discs[1];
        viewModel.SelectTemplateForSelectedDisc(viewModel.Templates[0]); // B(Discs[1]) = Task-Meter
        viewModel.SelectedDisc = viewModel.Discs[0]; // 読込開始時点の対象は A
        // A の保存済み ID を削除済みへ差し替え、リロード時にフォールバック解決が発生するようにする
        viewModel.Discs[0].Metadata.SelectedTemplateId = "deleted-template";

        store.ListGate = new TaskCompletionSource<bool>();
        Task reload = viewModel.LoadTemplatesAsync();
        viewModel.SelectedDisc = viewModel.Discs[1]; // await 中に A → B へ切り替え
        store.ListGate.SetResult(true);
        await reload;

        // A(対象円盤)自身はフォールバック解決で補正されてよい
        Assert.NotNull(viewModel.Discs[0].Metadata.SelectedTemplateId);
        Assert.NotEqual("deleted-template", viewModel.Discs[0].Metadata.SelectedTemplateId);
        // B(await 中に選択が切り替わった円盤)は A 向けの解決結果で上書きされない
        Assert.Equal("Task-Meter", viewModel.Discs[1].Metadata.SelectedTemplateId);
        // 表示値は現在選択中の B に追従する
        Assert.Equal("Task-Meter", viewModel.SelectedTemplate?.Id);
    }

    [Fact]
    public async Task ImportAsync_NewDiscsInheritCurrentlySelectedTemplate()
    {
        FakeTemplateStore store = new();
        await store.SaveAsync(id: null, BuildTemplate("Task-Meter"));
        await store.SaveAsync(id: null, BuildTemplate("Yazaki45"));
        FakeStagePipeline pipeline = new() { Discs = [BuildDisc(0)] };
        StageViewModel viewModel = new(
            pipeline, new NullImageSourceFactory(), store, new DateOnly(2026, 7, 19));
        await viewModel.LoadTemplatesAsync();
        viewModel.SelectTemplateForSelectedDisc(viewModel.Templates[1]);

        await viewModel.ImportAsync(["sheet.pdf"]);

        Assert.Equal("Yazaki45", viewModel.Discs[0].Metadata.SelectedTemplateId);
        Assert.Equal("Yazaki45", viewModel.SelectedTemplate?.Id);
    }

    [Fact]
    public async Task ImportAsync_ReimportWithSelectedDiscInheritsPreviouslySelectedTemplate()
    {
        // 取込→様式選択→再取込。ImportAsync 冒頭の SelectedDisc = null による
        // SelectedTemplate リセットより前に選択を退避できているかを確認する(#43 レビュー指摘)
        FakeTemplateStore store = new();
        await store.SaveAsync(id: null, BuildTemplate("Task-Meter"));
        await store.SaveAsync(id: null, BuildTemplate("Yazaki45"));
        FakeStagePipeline pipeline = new() { Discs = [BuildDisc(0)] };
        StageViewModel viewModel = new(
            pipeline, new NullImageSourceFactory(), store, new DateOnly(2026, 7, 19));
        await viewModel.LoadTemplatesAsync();
        await viewModel.ImportAsync(["first.pdf"]);
        viewModel.SelectTemplateForSelectedDisc(viewModel.Templates[1]);

        pipeline.Discs = [BuildDisc(0), BuildDisc(1)];
        await viewModel.ImportAsync(["second.pdf"]);

        Assert.All(viewModel.Discs, disc => Assert.Equal("Yazaki45", disc.Metadata.SelectedTemplateId));
        Assert.Equal("Yazaki45", viewModel.SelectedTemplate?.Id);
    }

    [Fact]
    public async Task SaveAndAdvanceAsync_WritesPngMarksDoneAndAdvances()
    {
        FakeStagePipeline pipeline = new() { Discs = [BuildDisc(0), BuildDisc(1)] };
        StageViewModel viewModel = CreateViewModel(pipeline);
        await viewModel.ImportAsync(["sheet.pdf"]);
        viewModel.SelectedTemplate = BuildStoredTemplate();
        viewModel.OutputDirectory = _temporaryDirectory;
        viewModel.Discs[0].Metadata.RegistrationNumber = "旭川123-45";
        viewModel.Discs[0].Metadata.DriverName = "山田 太郎";

        bool saved = await viewModel.SaveAndAdvanceAsync();

        Assert.True(saved);
        string expectedPath = Path.Combine(_temporaryDirectory, "20260719_旭川123-45_山田 太郎.png");
        Assert.True(File.Exists(expectedPath));
        // PNG シグネチャ
        byte[] header = (await File.ReadAllBytesAsync(expectedPath))[..4];
        Assert.Equal(new byte[] { 0x89, 0x50, 0x4E, 0x47 }, header);
        Assert.Equal(DiscStatus.Done, viewModel.Discs[0].Status);
        Assert.Same(viewModel.Discs[1], viewModel.SelectedDisc);
        Assert.False(viewModel.IsSaving);
        Assert.False(viewModel.HasSaveError);
    }

    [Fact]
    public async Task SaveAndAdvanceAsync_SkipHandwrittenRetainsDriverAndAddsSuffix()
    {
        FakeStagePipeline pipeline = new() { Discs = [BuildDisc(0)] };
        StageViewModel viewModel = CreateViewModel(pipeline);
        viewModel.SkipHandwritten = true;
        await viewModel.ImportAsync(["sheet.pdf"]);
        viewModel.OutputDirectory = _temporaryDirectory;
        viewModel.Discs[0].Metadata.RegistrationNumber = "旭川123-45";
        viewModel.Discs[0].Metadata.DriverName = "山田 太郎";

        bool saved = await viewModel.SaveAndAdvanceAsync();

        Assert.True(saved);
        Assert.True(File.Exists(Path.Combine(
            _temporaryDirectory,
            "20260719_旭川123-45_山田 太郎_手書き.png")));
    }

    [Fact]
    public async Task SaveAndAdvanceAsync_LastPendingDiscKeepsSelection()
    {
        FakeStagePipeline pipeline = new() { Discs = [BuildDisc(0), BuildDisc(1)] };
        StageViewModel viewModel = CreateViewModel(pipeline);
        await viewModel.ImportAsync(["sheet.pdf"]);
        viewModel.SelectedTemplate = BuildStoredTemplate();
        viewModel.OutputDirectory = _temporaryDirectory;
        viewModel.Discs[1].Status = DiscStatus.Done;

        await viewModel.SaveAndAdvanceAsync();

        // 未処理が残っていないため現在位置に留まる
        Assert.Same(viewModel.Discs[0], viewModel.SelectedDisc);
        Assert.All(viewModel.Discs, disc => Assert.Equal(DiscStatus.Done, disc.Status));
    }

    [Fact]
    public async Task SaveAndAdvanceAsync_WrapsAroundToEarlierPendingDisc()
    {
        FakeStagePipeline pipeline = new() { Discs = [BuildDisc(0), BuildDisc(1), BuildDisc(2)] };
        StageViewModel viewModel = CreateViewModel(pipeline);
        await viewModel.ImportAsync(["sheet.pdf"]);
        viewModel.OutputDirectory = _temporaryDirectory;
        viewModel.Discs[1].Status = DiscStatus.Done;
        viewModel.Discs[2].Status = DiscStatus.Done;
        viewModel.SelectedDisc = viewModel.Discs[2];
        viewModel.Discs[2].Status = DiscStatus.Pending;
        // 様式は保存対象円盤(Discs[2], #43 で円盤ごとに保持)へ設定する
        viewModel.SelectedTemplate = BuildStoredTemplate();

        await viewModel.SaveAndAdvanceAsync();

        // 後ろに未処理が無い場合は先頭側の未処理へ戻る
        Assert.Same(viewModel.Discs[0], viewModel.SelectedDisc);
    }

    [Fact]
    public async Task SaveAndAdvanceAsync_WithoutOutputDirectoryReturnsFalse()
    {
        FakeStagePipeline pipeline = new() { Discs = [BuildDisc(0)] };
        StageViewModel viewModel = CreateViewModel(pipeline);
        await viewModel.ImportAsync(["sheet.pdf"]);

        bool saved = await viewModel.SaveAndAdvanceAsync();

        Assert.False(saved);
        Assert.False(viewModel.CanSave);
        Assert.Equal(DiscStatus.Pending, viewModel.Discs[0].Status);
    }

    [Fact]
    public async Task SaveAndAdvanceAsync_WriteFailureSetsSaveError()
    {
        FakeStagePipeline pipeline = new() { Discs = [BuildDisc(0)] };
        StageViewModel viewModel = CreateViewModel(pipeline);
        await viewModel.ImportAsync(["sheet.pdf"]);
        viewModel.SelectedTemplate = BuildStoredTemplate();
        // 保存先パスをディレクトリで塞ぎ、書き込みを失敗させる
        viewModel.OutputDirectory = _temporaryDirectory;
        Directory.CreateDirectory(Path.Combine(_temporaryDirectory, "20260719__.png"));

        bool saved = await viewModel.SaveAndAdvanceAsync();

        Assert.False(saved);
        Assert.True(viewModel.HasSaveError);
        Assert.Equal(DiscStatus.Pending, viewModel.Discs[0].Status);
        Assert.False(viewModel.IsSaving);
    }

    [Fact]
    public async Task SaveTargetLabel_FollowsMetadataEdits()
    {
        FakeStagePipeline pipeline = new() { Discs = [BuildDisc(0)] };
        StageViewModel viewModel = CreateViewModel(pipeline);
        await viewModel.ImportAsync(["sheet.pdf"]);
        viewModel.OutputDirectory = _temporaryDirectory;
        int notificationCount = 0;
        viewModel.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(StageViewModel.SaveTargetLabel))
            {
                notificationCount++;
            }
        };

        viewModel.Discs[0].Metadata.DriverName = "山田 太郎";

        Assert.True(notificationCount > 0);
        Assert.Contains("20260719__山田 太郎.png", viewModel.SaveTargetLabel, StringComparison.Ordinal);
        Assert.Contains(_temporaryDirectory, viewModel.SaveTargetLabel, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SaveAndAdvanceAsync_WithoutTemplateAndNotSkippedIsRejected()
    {
        FakeStagePipeline pipeline = new() { Discs = [BuildDisc(0)] };
        StageViewModel viewModel = CreateViewModel(pipeline);
        await viewModel.ImportAsync(["sheet.pdf"]);
        viewModel.OutputDirectory = _temporaryDirectory;

        // 様式未選択かつ手書きスキップでない場合、文字なし PNG を成功扱いで保存しない
        Assert.False(viewModel.CanSave);
        bool saved = await viewModel.SaveAndAdvanceAsync();

        Assert.False(saved);
        Assert.Equal(DiscStatus.Pending, viewModel.Discs[0].Status);
        // 保存が拒否されるため出力先へは何も書かれない(ディレクトリ作成もされない)
        Assert.False(Directory.Exists(_temporaryDirectory));

        // 手書きスキップなら様式なしでも保存できる(FR-17)
        viewModel.SkipHandwritten = true;
        Assert.True(viewModel.CanSave);
    }

    [Fact]
    public async Task SaveAndAdvanceAsync_UsesSnapshotTakenBeforeAwait()
    {
        FakeStagePipeline pipeline = new() { Discs = [BuildDisc(0)] };
        StageViewModel viewModel = CreateViewModel(pipeline);
        await viewModel.ImportAsync(["sheet.pdf"]);
        viewModel.SelectedTemplate = BuildStoredTemplate();
        viewModel.OutputDirectory = _temporaryDirectory;
        viewModel.Discs[0].Metadata.DriverName = "山田 太郎";

        // スナップショットは最初の await より前に同期的に取られるため、
        // 開始直後の編集は合成にもファイル名にも混ざらない
        Task<bool> saving = viewModel.SaveAndAdvanceAsync();
        viewModel.Discs[0].Metadata.DriverName = "編集後";
        bool saved = await saving;

        Assert.True(saved);
        Assert.True(File.Exists(Path.Combine(_temporaryDirectory, "20260719__山田 太郎.png")));
        Assert.False(File.Exists(Path.Combine(_temporaryDirectory, "20260719__編集後.png")));
    }

    [Fact]
    public async Task SaveAndAdvanceAsync_FailedReplaceKeepsExistingFile()
    {
        FakeStagePipeline pipeline = new() { Discs = [BuildDisc(0)] };
        StageViewModel viewModel = CreateViewModel(pipeline);
        await viewModel.ImportAsync(["sheet.pdf"]);
        viewModel.SelectedTemplate = BuildStoredTemplate();
        viewModel.OutputDirectory = _temporaryDirectory;
        Directory.CreateDirectory(_temporaryDirectory);
        string targetPath = Path.Combine(_temporaryDirectory, "20260719__.png");
        await File.WriteAllTextAsync(targetPath, "既存の成果物");

        bool saved;
        // 既存ファイルをロックして置換(Move)を失敗させる
        await using (FileStream _ = new(targetPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            saved = await viewModel.SaveAndAdvanceAsync();
        }

        Assert.False(saved);
        Assert.True(viewModel.HasSaveError);
        // 途中失敗しても既存の成果物は破損しない
        Assert.Equal("既存の成果物", await File.ReadAllTextAsync(targetPath));
        Assert.Empty(Directory.EnumerateFiles(_temporaryDirectory, "*.tmp"));
        Assert.Equal(DiscStatus.Pending, viewModel.Discs[0].Status);
    }

    private static StoredTemplate BuildStoredTemplate() => new("T", BuildTemplate("T"));

    private static ChartTemplate BuildTemplate(string name) => new()
    {
        Name = name,
        Fields = new Dictionary<string, TextFieldDefinition>
        {
            ["driver"] = new(),
        },
    };

    private static ProcessedDisc BuildDisc(int indexInSheet) => new(
        SourcePath: "sheet.pdf",
        PageIndex: 0,
        IndexInSheet: indexInSheet,
        Width: 2,
        Height: 2,
        Bgra: new byte[16],
        ThumbnailWidth: 1,
        ThumbnailHeight: 1,
        ThumbnailPremultipliedBgra: new byte[4],
        EllipseCenterX: 1.0,
        EllipseCenterY: 1.0);

    private sealed class FakeStagePipeline : IStagePipeline
    {
        public List<ProcessedDisc> Discs { get; set; } = [];

        public Exception? ThrowAfterDiscs { get; init; }

        public int ProcessCallCount { get; private set; }

        public async IAsyncEnumerable<ProcessedDisc> ProcessAsync(
            IReadOnlyList<string> paths,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            ProcessCallCount++;
            await Task.Yield();
            foreach (ProcessedDisc disc in Discs)
            {
                yield return disc;
            }

            if (ThrowAfterDiscs is not null)
            {
                throw ThrowAfterDiscs;
            }
        }
    }

    private sealed class NullImageSourceFactory : IImageSourceFactory
    {
        public ImageSource? Create(byte[] premultipliedBgra, int width, int height) => null;
    }
}
