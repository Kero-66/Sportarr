using FluentAssertions;
using Sportarr.Api.Helpers;
using Sportarr.Api.Models;
using Xunit;

namespace Sportarr.Api.Tests.Helpers;

public class QualityProfileRankerTests
{
    [Fact]
    public void ProfileOrderOverridesResolutionOrder()
    {
        var profile = Profile(
            Item("HDTV-1080p", 6),
            Item("WEBDL-2160p", 19));

        QualityProfileRanker.Compare(profile, "HDTV-1080p", "WEBDL-2160p")
            .Should().BePositive();
    }

    [Fact]
    public void QualitiesInOneGroupHaveEqualRank()
    {
        var profile = Profile(Group("Preferred", Item("HDTV-1080p", 6), Item("WEBDL-2160p", 19)));

        QualityProfileRanker.Compare(profile, "HDTV-1080p", "WEBDL-2160p")
            .Should().Be(0);
    }

    [Fact]
    public void QualityNamesMatchDespiteDifferentParserAndProfileIds()
    {
        var profile = Profile(Item("HDTV-1080p", 6), Item("WEBDL-1080p", 15));

        QualityProfileRanker.GetRank(profile, "HDTV-1080p").Should().Be(2);
        QualityProfileRanker.GetRank(profile, "WEBDL-1080p").Should().Be(1);
    }

    [Fact]
    public void UnlistedQualityRanksBelowConfiguredQuality()
    {
        var profile = Profile(Item("HDTV-1080p", 6));

        QualityProfileRanker.Compare(profile, "HDTV-1080p", "WEBDL-2160p")
            .Should().BePositive();
    }

    [Fact]
    public void CutoffChildUsesItsGroupsRank()
    {
        var profile = Profile(
            Group("WEB 2160p", Item("WEBDL-2160p", 19), Item("WEBRip-2160p", 18)),
            Item("HDTV-1080p", 6));

        QualityProfileRanker.GetCutoffRank(profile, 18).Should().Be(2);
    }

    [Fact]
    public void MissingProfileKeepsDeterministicFallbackOrder()
    {
        QualityProfileRanker.Compare(null, "WEBDL-2160p", "HDTV-1080p")
            .Should().BePositive();
    }

    [Fact]
    public void GroupedQualityMeetsAChildCutoff()
    {
        var profile = Profile(
            Group("WEB 2160p", Item("WEBDL-2160p", 19), Item("WEBRip-2160p", 18)),
            Item("HDTV-1080p", 6));
        profile.CutoffQuality = 19;

        QualityProfileRanker.IsBelowCutoff(profile, "WEBRip-2160p").Should().BeFalse();
        QualityProfileRanker.IsBelowCutoff(profile, "HDTV-1080p").Should().BeTrue();
    }

    private static QualityProfile Profile(params QualityItem[] items) => new()
    {
        Name = "Test",
        Items = items.ToList(),
    };

    private static QualityItem Item(string name, int quality) => new()
    {
        Name = name,
        Quality = quality,
        Allowed = true,
    };

    private static QualityItem Group(string name, params QualityItem[] items) => new()
    {
        Name = name,
        Quality = 0,
        Allowed = true,
        Items = items.ToList(),
    };
}
