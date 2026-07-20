namespace TachoGraphStudio.Core.Roster;

public sealed class VendorUnavailableException : Exception
{
    public VendorUnavailableException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
