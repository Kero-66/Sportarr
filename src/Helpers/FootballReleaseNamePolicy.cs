using System.Text.RegularExpressions;
using Sportarr.Api.Models;

namespace Sportarr.Api.Helpers;

internal static class FootballReleaseNamePolicy
{
    private static readonly Regex WomensTeamSuffix = new(
        @"\s+(?:WFC|FC\s+Women|Women)$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex ChampionshipTeamSuffix = new(
        @"\s+Wanderers$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex UnitedCupIdentity = new(
        @"(?<![\p{L}\p{N}])United[\s\.\-_]+Cup(?![\p{L}\p{N}])",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    internal static bool IsEnglishChampionship(string? leagueName)
        => leagueName?.Trim().Equals("English League Championship", StringComparison.OrdinalIgnoreCase) == true;

    internal static bool IsEnglishWomensSuperLeague(string? leagueName)
        => leagueName?.Trim().Equals("English Womens Super League", StringComparison.OrdinalIgnoreCase) == true;

    internal static bool IsFaCup(string? leagueName)
        => leagueName?.Trim().Equals("FA Cup", StringComparison.OrdinalIgnoreCase) == true;

    internal static bool IsEuropaLeague(string? leagueName)
        => leagueName?.Trim().Equals("UEFA Europa League", StringComparison.OrdinalIgnoreCase) == true;

    internal static bool IsFifaWorldCup(string? leagueName)
        => leagueName?.Trim().Equals("FIFA World Cup", StringComparison.OrdinalIgnoreCase) == true;

    internal static bool NamesDifferentCompetition(string releaseTitle, string? leagueName)
        => IsFifaWorldCup(leagueName) && UnitedCupIdentity.IsMatch(releaseTitle);

    internal static string BaseParticipantName(string teamName, string? leagueName)
    {
        var trimmed = teamName.Trim();
        if (IsEnglishWomensSuperLeague(leagueName))
            return WomensTeamSuffix.Replace(trimmed, "").Trim();
        if (IsEnglishChampionship(leagueName))
            return ChampionshipTeamSuffix.Replace(trimmed, "").Trim();
        return trimmed;
    }

    internal static IEnumerable<string> LeagueAliases(League league)
    {
        if (IsEnglishChampionship(league.Name))
        {
            yield return "EFL Championship";
            yield return "English Championship";
        }
        else if (IsEnglishWomensSuperLeague(league.Name))
        {
            yield return "WSL";
            yield return "BWSL";
            yield return "FA WSL";
            yield return "Barclays WSL";
            yield return "England Womens Super League";
            yield return "Women's Super League";
        }
        else if (IsEuropaLeague(league.Name))
        {
            yield return "UEL";
        }
        else if (IsFifaWorldCup(league.Name))
        {
            yield return "World Cup";
            yield return "FIFA WC";
        }
    }
}
