using TachoGraphStudio.Core.Updates;

namespace TachoGraphStudio.Core.Tests.Updates;

public sealed class ChangelogParserTests
{
    private const string Changelog = """
        # Changelog

        ## [Unreleased]

        ### Added

        - 未リリースの変更

        ## [0.1.6] - 2026-07-26

        ### Added

        - 六番目の変更

        ## [0.1.5] - 2026-07-25

        ### Fixed

        - 五番目の変更

        ## [0.1.4]

        ### Changed

        - 四番目の変更
        """;

    [Fact]
    public void Parse_ExtractsVersionedSectionsUntilNextVersion()
    {
        IReadOnlyList<ChangelogSection> sections = ChangelogParser.Parse(Changelog);

        Assert.Collection(
            sections,
            section =>
            {
                Assert.Equal(new Version(0, 1, 6), section.Version);
                Assert.Equal("2026-07-26", section.Date);
                Assert.Contains("六番目の変更", section.Markdown);
                Assert.DoesNotContain("五番目の変更", section.Markdown);
            },
            section =>
            {
                Assert.Equal(new Version(0, 1, 5), section.Version);
                Assert.Equal("2026-07-25", section.Date);
                Assert.Contains("五番目の変更", section.Markdown);
            },
            section =>
            {
                Assert.Equal(new Version(0, 1, 4), section.Version);
                Assert.Null(section.Date);
                Assert.Contains("四番目の変更", section.Markdown);
            });
    }

    [Theory]
    [InlineData(null, "0.1.6", 3)]
    [InlineData("0.1.5", "0.1.6", 1)]
    [InlineData("0.1.4", "0.1.6", 2)]
    [InlineData("0.1.6", "0.1.6", 0)]
    public void SelectSections_ReturnsVersionsBetweenLastShownAndCurrent(
        string? lastShownVersion,
        string currentVersion,
        int expectedCount)
    {
        Version? lastShown = lastShownVersion is null
            ? null
            : Version.Parse(lastShownVersion);

        IReadOnlyList<ChangelogSection> sections = ChangelogParser.SelectSections(
            Changelog,
            lastShown,
            Version.Parse(currentVersion));

        Assert.Equal(expectedCount, sections.Count);
    }

    [Fact]
    public void SelectSections_NormalizesPackageRevision()
    {
        IReadOnlyList<ChangelogSection> sections = ChangelogParser.SelectSections(
            Changelog,
            new Version(0, 1, 5),
            new Version(0, 1, 6, 0));

        Assert.Single(sections);
        Assert.Equal(new Version(0, 1, 6), sections[0].Version);
    }
}
