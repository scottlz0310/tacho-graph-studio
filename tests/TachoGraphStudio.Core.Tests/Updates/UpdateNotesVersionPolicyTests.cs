using TachoGraphStudio.Core.Updates;

namespace TachoGraphStudio.Core.Tests.Updates;

public sealed class UpdateNotesVersionPolicyTests
{
    [Theory]
    [InlineData(null, false, null)]
    [InlineData(null, true, "0.1.6")]
    [InlineData("0.1.5", true, "0.1.5")]
    [InlineData("0.1.6.0", true, "0.1.6")]
    [InlineData("invalid", true, null)]
    public void ResolveLastShownVersion_DistinguishesNewAndLegacyState(
        string? lastShownVersion,
        bool hasPersistedState,
        string? expectedVersion)
    {
        Version? resolved = UpdateNotesVersionPolicy.ResolveLastShownVersion(
            lastShownVersion,
            hasPersistedState);

        Version? expected = expectedVersion is null
            ? null
            : Version.Parse(expectedVersion);
        Assert.Equal(expected, resolved);
    }

    [Theory]
    [InlineData(null, false, true)]
    [InlineData(null, true, false)]
    [InlineData("0.1.6", false, false)]
    public void IsNewInstallation_RequiresMissingStateAndVersion(
        string? lastShownVersion,
        bool hasPersistedState,
        bool expected)
    {
        Assert.Equal(
            expected,
            UpdateNotesVersionPolicy.IsNewInstallation(lastShownVersion, hasPersistedState));
    }
}
