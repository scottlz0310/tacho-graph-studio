namespace TachoGraphStudio.Core.Imaging;

public sealed class SheetLoadException : Exception
{
    public SheetLoadException(string message)
        : base(message)
    {
    }

    public SheetLoadException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
