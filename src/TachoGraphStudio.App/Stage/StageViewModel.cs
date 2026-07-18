using System.Collections.ObjectModel;

using CommunityToolkit.Mvvm.ComponentModel;

using TachoGraphStudio.Core.Imaging;

namespace TachoGraphStudio.App.Stage;

public sealed partial class StageViewModel : ObservableObject
{
    private readonly IImageSourceFactory _imageSourceFactory;
    private readonly IStagePipeline _pipeline;

    [ObservableProperty]
    public partial DiscWorkItem? SelectedDisc { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsImportEnabled))]
    public partial bool IsImporting { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasImportError))]
    public partial string? ImportError { get; set; }

    [ObservableProperty]
    public partial bool IsEmptyStateVisible { get; set; } = true;

    public StageViewModel(IStagePipeline pipeline, IImageSourceFactory imageSourceFactory)
    {
        ArgumentNullException.ThrowIfNull(pipeline);
        ArgumentNullException.ThrowIfNull(imageSourceFactory);

        _pipeline = pipeline;
        _imageSourceFactory = imageSourceFactory;
    }

    public ObservableCollection<DiscWorkItem> Discs { get; } = [];

    public bool IsImportEnabled => !IsImporting;

    public bool HasImportError => ImportError is not null;

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
