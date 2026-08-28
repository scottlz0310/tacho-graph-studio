namespace TachoGraphStudio.Core.Imaging;

public sealed class BackgroundRemovalException : Exception
{
    public BackgroundRemovalException(string message)
        : base(message)
    {
    }

    public BackgroundRemovalException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
