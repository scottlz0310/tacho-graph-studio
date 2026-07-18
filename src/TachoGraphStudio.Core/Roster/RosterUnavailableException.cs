namespace TachoGraphStudio.Core.Roster;

public sealed class RosterUnavailableException : Exception
{
    public RosterUnavailableException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
