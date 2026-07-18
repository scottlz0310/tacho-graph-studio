using TachoGraphStudio.Core.Roster;

namespace TachoGraphStudio.Core.Tests.Roster;

public sealed class RosterFilterTests
{
    [Theory]
    [InlineData(RosterSeason.All, null, true)]
    [InlineData(RosterSeason.All, "unknown", true)]
    [InlineData(RosterSeason.Winter, "winter", true)]
    [InlineData(RosterSeason.Winter, "both", true)]
    [InlineData(RosterSeason.Winter, "summer", false)]
    [InlineData(RosterSeason.Winter, null, false)]
    [InlineData(RosterSeason.Summer, "summer", true)]
    [InlineData(RosterSeason.Summer, "both", true)]
    [InlineData(RosterSeason.Summer, "winter", false)]
    [InlineData(RosterSeason.YearRound, "both", true)]
    [InlineData(RosterSeason.YearRound, "winter", false)]
    public void Apply_FiltersBySeason(
        RosterSeason season,
        string? workPeriod,
        bool expected)
    {
        RosterEntry entry = CreateEntry(workPeriod: workPeriod);
        RosterFilterSettings settings = new()
        {
            Season = season,
            TachoTargetsOnly = false,
        };

        IReadOnlyList<RosterEntry> result = RosterFilter.Apply([entry], settings);

        Assert.Equal(expected, result.Count == 1);
    }

    [Theory]
    [InlineData("snow", true)]
    [InlineData("DOZER", true)]
    [InlineData("8T", true)]
    [InlineData("札幌 100", true)]
    [InlineData("YAMADA", true)]
    [InlineData("  dozer  ", true)]
    [InlineData("123", false)]
    [InlineData("", true)]
    [InlineData("   ", true)]
    public void Apply_FiltersKeywordAcrossRosterFields(string keyword, bool expected)
    {
        RosterEntry entry = CreateEntry(
            detail: "Snow Dozer",
            specification: "8t",
            registrationNumber: "札幌 100 あ 12-34",
            driver: "Yamada");

        IReadOnlyList<RosterEntry> result = RosterFilter.Apply(
            [entry],
            new RosterFilterSettings { TachoTargetsOnly = false },
            keyword);

        Assert.Equal(expected, result.Count == 1);
    }

    [Fact]
    public void Apply_DefaultSettingsShowOnlyTachoTargets()
    {
        RosterEntry target = CreateEntry(controlNumber: 100, isTachoTarget: true);
        RosterEntry nonTarget = CreateEntry(controlNumber: 200, isTachoTarget: false);

        IReadOnlyList<RosterEntry> result = RosterFilter.Apply(
            [target, nonTarget],
            RosterFilterSettings.Default);

        Assert.Equal([target], result);
    }

    [Fact]
    public void Apply_DisabledTachoTargetFilterPreservesInputOrder()
    {
        RosterEntry first = CreateEntry(controlNumber: 200, isTachoTarget: false);
        RosterEntry second = CreateEntry(controlNumber: 100, isTachoTarget: true);

        IReadOnlyList<RosterEntry> result = RosterFilter.Apply(
            [first, second],
            new RosterFilterSettings { TachoTargetsOnly = false });

        Assert.Equal([first, second], result);
    }

    [Theory]
    [InlineData("55", 550L)]
    [InlineData("５５", 550L)]
    [InlineData(" ５５ ", 550L)]
    [InlineData("551", 551L)]
    [InlineData("9", null)]
    [InlineData("", null)]
    [InlineData("   ", null)]
    public void FindByControlNumberPrefix_ReturnsFirstMatch(
        string input,
        long? expectedControlNumber)
    {
        RosterEntry[] entries =
        [
            CreateEntry(controlNumber: 150),
            CreateEntry(controlNumber: 550),
            CreateEntry(controlNumber: 551),
        ];

        RosterEntry? result = RosterFilter.FindByControlNumberPrefix(entries, input);

        Assert.Equal(expectedControlNumber, result?.ControlNumber);
    }

    private static RosterEntry CreateEntry(
        long controlNumber = 100,
        string detail = "",
        string specification = "",
        string registrationNumber = "",
        string driver = "",
        string? workPeriod = null,
        bool isTachoTarget = true)
    {
        return new RosterEntry
        {
            ControlNumber = controlNumber,
            Detail = detail,
            Specification = specification,
            RegistrationNumber = registrationNumber,
            Driver = driver,
            WorkPeriod = workPeriod,
            IsTachoTarget = isTachoTarget,
        };
    }
}
