using TachoGraphStudio.Core.Settings;

namespace TachoGraphStudio.App.Stage;

public interface IStagePipeline
{
    IAsyncEnumerable<ProcessedDisc> ProcessAsync(
        IReadOnlyList<string> paths,
        ImageProcessingSettings? settings = null,
        CancellationToken cancellationToken = default);
}
