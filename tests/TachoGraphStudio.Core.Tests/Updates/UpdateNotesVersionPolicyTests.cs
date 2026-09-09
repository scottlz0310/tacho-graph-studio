using TachoGraphStudio.Core.Updates;

namespace TachoGraphStudio.Core.Tests.Updates;

public sealed class UpdateNotesVersionPolicyTests
{
    // 変更点表示でとる行動の決定(#137)。変更履歴の読み込み前に判断できることが前提
    [Fact]
    public void Plan_NewInstallationRecordsVersionWithoutShowing()
    {
        UpdateNotesPlan plan = UpdateNotesVersionPolicy.Plan(
            lastShownVersion: null,
            hasPersistedState: false,
            new Version(0, 3, 0));

        Assert.Equal(UpdateNotesAction.RecordVersionOnly, plan.Action);
        Assert.Null(plan.LastShownVersion);
    }

    // 同一・過去バージョンでの起動では何もしない
    [Theory]
    [InlineData("0.3.0")]
    [InlineData("0.3.1")]
    [InlineData("1.0.0")]
    public void Plan_SameOrNewerLastShownVersionDoesNothing(string lastShownVersion)
    {
        UpdateNotesPlan plan = UpdateNotesVersionPolicy.Plan(
            lastShownVersion,
            hasPersistedState: true,
            new Version(0, 3, 0));

        Assert.Equal(UpdateNotesAction.None, plan.Action);
    }

    [Theory]
    [InlineData("0.2.0")]
    [InlineData("0.1.7")]
    public void Plan_OlderLastShownVersionShowsFromThatVersion(string lastShownVersion)
    {
        UpdateNotesPlan plan = UpdateNotesVersionPolicy.Plan(
            lastShownVersion,
            hasPersistedState: true,
            new Version(0, 3, 0));

        Assert.Equal(UpdateNotesAction.Show, plan.Action);
        Assert.Equal(Version.Parse(lastShownVersion), plan.LastShownVersion);
    }

    // LastShownVersion 導入前の状態ファイルは v0.1.6 からの更新として表示する
    [Fact]
    public void Plan_LegacyStateWithoutVersionShowsFromIntroductionBaseline()
    {
        UpdateNotesPlan plan = UpdateNotesVersionPolicy.Plan(
            lastShownVersion: null,
            hasPersistedState: true,
            new Version(0, 3, 0));

        Assert.Equal(UpdateNotesAction.Show, plan.Action);
        Assert.Equal(new Version(0, 1, 6), plan.LastShownVersion);
    }

    // 壊れた記録は前回表示なしとして扱い、変更点は表示する
    [Theory]
    [InlineData("")]
    [InlineData("not-a-version")]
    public void Plan_UnparsableLastShownVersionShowsWithoutBaseline(string lastShownVersion)
    {
        UpdateNotesPlan plan = UpdateNotesVersionPolicy.Plan(
            lastShownVersion,
            hasPersistedState: true,
            new Version(0, 3, 0));

        Assert.Equal(UpdateNotesAction.Show, plan.Action);
        Assert.Null(plan.LastShownVersion);
    }

    [Fact]
    public void Plan_WithoutCurrentVersionThrows()
    {
        Assert.Throws<ArgumentNullException>(
            () => UpdateNotesVersionPolicy.Plan("0.2.0", hasPersistedState: true, null!));
    }

    // 記録した文字列を ResolveLastShownVersion が読み戻せること(往復)
    [Theory]
    [InlineData(0, 3, 0, "0.3.0")]
    [InlineData(1, 0, 0, "1.0.0")]
    [InlineData(0, 1, 7, "0.1.7")]
    public void FormatVersion_RoundTripsThroughResolveLastShownVersion(
        int major,
        int minor,
        int build,
        string expected)
    {
        Version version = new(major, minor, build);

        string formatted = UpdateNotesVersionPolicy.FormatVersion(version);

        Assert.Equal(expected, formatted);
        Assert.Equal(
            version,
            UpdateNotesVersionPolicy.ResolveLastShownVersion(formatted, hasPersistedState: true));
    }

    // Version は 4 桁目(Revision)を持ちうるが、記録は 3 桁へ落とす
    [Fact]
    public void FormatVersion_DropsRevision()
    {
        Assert.Equal("0.3.0", UpdateNotesVersionPolicy.FormatVersion(new Version(0, 3, 0, 5)));
    }

    [Fact]
    public void FormatVersion_WithoutVersionThrows()
    {
        Assert.Throws<ArgumentNullException>(() => UpdateNotesVersionPolicy.FormatVersion(null!));
    }

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
