namespace TachoGraphStudio.Core.Imaging;

public sealed class BackgroundRemovalException : Exception
{
    public BackgroundRemovalException(string message)
        : base(message)
    {
    }
}
