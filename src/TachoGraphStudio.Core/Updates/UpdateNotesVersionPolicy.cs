namespace TachoGraphStudio.Core.Updates;

public static class UpdateNotesVersionPolicy
{
    public static bool IsNewInstallation(string? lastShownVersion, bool hasPersistedState) =>
        lastShownVersion is null && !hasPersistedState;

    public static Version? ResolveLastShownVersion(
        string? lastShownVersion,
        bool hasPersistedState)
    {
        if (lastShownVersion is null)
        {
            // LastShownVersion 導入前の状態ファイルは、v0.1.6 からの更新として扱う
            return hasPersistedState ? new Version(0, 1, 6) : null;
        }

        if (!Version.TryParse(lastShownVersion, out Version? parsed) || parsed is null)
        {
            return null;
        }

        return new Version(
            parsed.Major,
            Math.Max(parsed.Minor, 0),
            Math.Max(parsed.Build, 0));
    }
}
