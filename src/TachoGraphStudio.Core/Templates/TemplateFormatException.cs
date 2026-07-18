namespace TachoGraphStudio.Core.Templates;

public sealed class TemplateFormatException : Exception
{
    public TemplateFormatException(string message)
        : base(message)
    {
    }

    public TemplateFormatException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
