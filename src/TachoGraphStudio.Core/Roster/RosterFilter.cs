using System.Globalization;

namespace TachoGraphStudio.Core.Roster;

public static class RosterFilter
{
    public static IReadOnlyList<RosterEntry> Apply(
        IEnumerable<RosterEntry> entries,
        RosterFilterSettings settings,
        string? keyword = null)
    {
        ArgumentNullException.ThrowIfNull(entries);
        ArgumentNullException.ThrowIfNull(settings);

        string normalizedKeyword = keyword?.Trim() ?? string.Empty;

        return entries
            .Where(entry => !settings.TachoTargetsOnly || entry.IsTachoTarget)
            .Where(entry => MatchesSeason(entry, settings.Season))
            .Where(entry => MatchesKeyword(entry, normalizedKeyword))
            .ToArray();
    }

    public static RosterEntry? FindByControlNumberPrefix(
        IEnumerable<RosterEntry> entries,
        string? input)
    {
        ArgumentNullException.ThrowIfNull(entries);

        string normalizedInput = NormalizeControlNumberInput(input).Trim();
        if (normalizedInput.Length == 0)
        {
            return null;
        }

        return entries.FirstOrDefault(entry => entry.ControlNumber
            .ToString(CultureInfo.InvariantCulture)
            .StartsWith(normalizedInput, StringComparison.Ordinal));
    }

    public static string NormalizeControlNumberInput(string? input)
    {
        if (string.IsNullOrEmpty(input))
        {
            return string.Empty;
        }

        char[] characters = input.ToCharArray();
        for (int index = 0; index < characters.Length; index++)
        {
            char character = characters[index];
            if (character is >= '０' and <= '９')
            {
                characters[index] = (char)(character - '０' + '0');
            }
        }

        return new string(characters);
    }

    private static bool MatchesSeason(RosterEntry entry, RosterSeason season)
    {
        return season switch
        {
            RosterSeason.All => true,
            RosterSeason.Winter => entry.WorkPeriod is "winter" or "both",
            RosterSeason.Summer => entry.WorkPeriod is "summer" or "both",
            RosterSeason.YearRound => entry.WorkPeriod is "both",
            _ => throw new ArgumentOutOfRangeException(nameof(season), season, "未対応のシーズンです。"),
        };
    }

    private static bool MatchesKeyword(RosterEntry entry, string keyword)
    {
        if (keyword.Length == 0)
        {
            return true;
        }

        return entry.ControlNumber.ToString(CultureInfo.InvariantCulture).Contains(keyword, StringComparison.OrdinalIgnoreCase)
            || entry.Detail.Contains(keyword, StringComparison.OrdinalIgnoreCase)
            || entry.Specification.Contains(keyword, StringComparison.OrdinalIgnoreCase)
            || entry.RegistrationNumber.Contains(keyword, StringComparison.OrdinalIgnoreCase)
            || entry.Driver.Contains(keyword, StringComparison.OrdinalIgnoreCase);
    }
}
