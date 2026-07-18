using System.Runtime.CompilerServices;

using Microsoft.UI.Xaml.Media;

using TachoGraphStudio.App.Stage;
using TachoGraphStudio.App.Tests.Templates;
using TachoGraphStudio.Core.Imaging;
using TachoGraphStudio.Core.Roster;
using TachoGraphStudio.Core.Templates;

namespace TachoGraphStudio.App.Tests.Stage;

public sealed class StageViewModelTests
{
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
    public async Task LoadTemplatesAsync_StoreFailureBecomesWarning()
    {
        FakeTemplateStore store = new() { NextException = new IOException("ディスクエラー") };
        StageViewModel viewModel = new(
            new FakeStagePipeline(), new NullImageSourceFactory(), store, new DateOnly(2026, 7, 19));

        await viewModel.LoadTemplatesAsync();

        Assert.True(viewModel.HasTemplateWarning);
        Assert.Empty(viewModel.Templates);
    }

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
        PremultipliedBgra: new byte[16],
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
