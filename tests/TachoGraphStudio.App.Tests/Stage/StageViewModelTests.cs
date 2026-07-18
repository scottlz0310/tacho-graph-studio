using System.Runtime.CompilerServices;

using Microsoft.UI.Xaml.Media;

using TachoGraphStudio.App.Stage;
using TachoGraphStudio.Core.Imaging;

namespace TachoGraphStudio.App.Tests.Stage;

public sealed class StageViewModelTests
{
    [Theory]
    [InlineData(1)]
    [InlineData(3)]
    public async Task ImportAsync_PopulatesDiscsInOrderAndSelectsFirst(int discCount)
    {
        FakeStagePipeline pipeline = new()
        {
            Discs = [.. Enumerable.Range(0, discCount).Select(BuildDisc)],
        };
        StageViewModel viewModel = new(pipeline, new NullImageSourceFactory());

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
        StageViewModel viewModel = new(pipeline, new NullImageSourceFactory());

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
        StageViewModel viewModel = new(pipeline, new NullImageSourceFactory());

        await viewModel.ImportAsync(["sheet.bmp"]);

        Assert.Empty(viewModel.Discs);
        Assert.True(viewModel.HasImportError);
        Assert.True(viewModel.IsEmptyStateVisible);
    }

    [Fact]
    public async Task ImportAsync_SecondImportReplacesDiscsAndResetsSelection()
    {
        FakeStagePipeline pipeline = new() { Discs = [BuildDisc(0), BuildDisc(1)] };
        StageViewModel viewModel = new(pipeline, new NullImageSourceFactory());
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
    public async Task ImportAsync_EmptyPathsDoesNothing()
    {
        FakeStagePipeline pipeline = new() { Discs = [BuildDisc(0)] };
        StageViewModel viewModel = new(pipeline, new NullImageSourceFactory());

        await viewModel.ImportAsync([]);

        Assert.Empty(viewModel.Discs);
        Assert.Equal(0, pipeline.ProcessCallCount);
        Assert.True(viewModel.IsEmptyStateVisible);
    }

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
