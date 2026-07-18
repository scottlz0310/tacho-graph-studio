namespace TachoGraphStudio.App.Stage;

public interface IStagePipeline
{
    IAsyncEnumerable<ProcessedDisc> ProcessAsync(
        IReadOnlyList<string> paths,
        CancellationToken cancellationToken = default);
}
