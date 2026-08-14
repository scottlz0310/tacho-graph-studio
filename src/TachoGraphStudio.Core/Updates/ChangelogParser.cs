using System.Text.RegularExpressions;

namespace TachoGraphStudio.Core.Updates;

public sealed record ChangelogSection(Version Version, string? Date, string Markdown);

public static partial class ChangelogParser
{
    [GeneratedRegex(
        @"^##\s+\[(?<version>\d+\.\d+\.\d+)\](?:\s*-\s*(?<date>.*?))?\s*$",
        RegexOptions.Multiline | RegexOptions.CultureInvariant)]
    private static partial Regex ReleaseHeaderRegex();

    public static IReadOnlyList<ChangelogSection> Parse(string changelog)
    {
        ArgumentNullException.ThrowIfNull(changelog);

        MatchCollection headers = ReleaseHeaderRegex().Matches(changelog);
        List<ChangelogSection> sections = new(headers.Count);

        for (int index = 0; index < headers.Count; index++)
        {
            Match header = headers[index];
            Match? nextHeader = index + 1 < headers.Count ? headers[index + 1] : null;
            int bodyStart = header.Index + header.Length;
            int bodyLength = (nextHeader?.Index ?? changelog.Length) - bodyStart;

            Version version = Version.Parse(header.Groups["version"].Value);
            string? date = header.Groups["date"].Success
                ? header.Groups["date"].Value.Trim()
                : null;
            string markdown = changelog.Substring(bodyStart, bodyLength).Trim();
            sections.Add(new ChangelogSection(version, date, markdown));
        }

        return sections;
    }

    public static IReadOnlyList<ChangelogSection> SelectSections(
        string changelog,
        Version? lastShownVersion,
        Version currentVersion)
    {
        ArgumentNullException.ThrowIfNull(changelog);
        ArgumentNullException.ThrowIfNull(currentVersion);

        Version normalizedCurrentVersion = Normalize(currentVersion);
        Version? normalizedLastShownVersion = lastShownVersion is null
            ? null
            : Normalize(lastShownVersion);

        return Parse(changelog)
            .Where(section =>
                (normalizedLastShownVersion is null
                    || section.Version > normalizedLastShownVersion)
                && section.Version <= normalizedCurrentVersion)
            .ToArray();
    }

    private static Version Normalize(Version version) => new(
        version.Major,
        Math.Max(version.Minor, 0),
        Math.Max(version.Build, 0));
}
