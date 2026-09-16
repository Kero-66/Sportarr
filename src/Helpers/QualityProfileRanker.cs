using Sportarr.Api.Models;
using Sportarr.Api.Services;

namespace Sportarr.Api.Helpers;

public static class QualityProfileRanker
{
    public static int GetRank(QualityProfile? profile, string? qualityName)
    {
        if (profile?.Items == null || profile.Items.Count == 0)
        {
            return ReleaseEvaluator.CalculateQualityScoreFromName(qualityName);
        }

        var quality = QualityParser.ParseQuality(qualityName ?? string.Empty).Quality;
        for (var index = 0; index < profile.Items.Count; index++)
        {
            if (Matches(profile.Items[index], quality))
            {
                return profile.Items.Count - index;
            }
        }

        return 0;
    }

    public static int Compare(QualityProfile? profile, string? leftQuality, string? rightQuality)
    {
        return GetRank(profile, leftQuality).CompareTo(GetRank(profile, rightQuality));
    }

    public static int GetCutoffRank(QualityProfile profile, int qualityId)
    {
        for (var index = 0; index < profile.Items.Count; index++)
        {
            if (ContainsQualityId(profile.Items[index], qualityId))
            {
                return profile.Items.Count - index;
            }
        }

        return 0;
    }

    public static bool IsBelowCutoff(QualityProfile profile, string? qualityName)
    {
        if (!profile.UpgradesAllowed || !profile.CutoffQuality.HasValue)
        {
            return false;
        }

        var currentRank = GetRank(profile, qualityName);
        var cutoffRank = GetCutoffRank(profile, profile.CutoffQuality.Value);
        return currentRank > 0 && cutoffRank > 0 && currentRank < cutoffRank;
    }

    private static bool Matches(QualityItem item, QualityParser.QualityDefinition quality)
    {
        if (item.IsGroup)
        {
            return item.Items!.Any(child => Matches(child, quality));
        }

        return QualityParser.MatchesProfileItem(quality, item.Name);
    }

    private static bool ContainsQualityId(QualityItem item, int qualityId)
    {
        return item.Quality == qualityId
            || item.Items?.Any(child => ContainsQualityId(child, qualityId)) == true;
    }
}
