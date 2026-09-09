using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Sportarr.Api.Models;
using Sportarr.Api.Services;

namespace Sportarr.Api.Tests.Services;

public class MotorsportSessionIdentityTests
{
    [Theory]
    [InlineData("Aragón - Qualifying 1", "MotoGP.2026.Aragon.Qualifying.One.1080p.WEB.H264-GROUP")]
    [InlineData("Aragón - Qualifying 2", "MotoGP.2026.Aragon.Qualifying.Two.1080p.WEB.H264-GROUP")]
    [InlineData("Aragón - Q1", "MotoGP.2026.Aragon.Q1.1080p.WEB.H264-GROUP")]
    [InlineData("Aragón - Q2", "MotoGP.2026.Aragon.Q2.1080p.WEB.H264-GROUP")]
    public async Task NumberedMotoGpQualifyingReleaseMatchesItsEvent(string eventTitle, string releaseTitle)
    {
        await using var rig = await PartIdentityIntegrationHarness.CreateAsync(
            title: eventTitle, sport: "Motorsport", leagueName: "MotoGP", relational: true);
        rig.Event.Round = "13";
        rig.Event.Season = "2026";
        rig.Event.SeasonNumber = 2026;
        rig.Event.EventDate = new DateTime(2026, 8, 29, 9, 15, 0, DateTimeKind.Utc);

        var result = rig.Services.GetRequiredService<ReleaseMatchingService>()
            .ValidateRelease(Release(releaseTitle), rig.Event, null, true);

        result.IsHardRejection.Should().BeFalse(string.Join("; ", result.Rejections));
        result.MatchReasons.Should().Contain(x => x.StartsWith("Session type matches:"));
    }

    [Fact]
    public async Task NumberedMotoGpQualifyingStillRejectsTheOtherSession()
    {
        await using var rig = await PartIdentityIntegrationHarness.CreateAsync(
            title: "Aragón - Qualifying 2", sport: "Motorsport", leagueName: "MotoGP", relational: true);
        rig.Event.Round = "13";
        rig.Event.Season = "2026";
        rig.Event.SeasonNumber = 2026;
        rig.Event.EventDate = new DateTime(2026, 8, 29, 9, 15, 0, DateTimeKind.Utc);

        var result = rig.Services.GetRequiredService<ReleaseMatchingService>()
            .ValidateRelease(Release("MotoGP.2026.Aragon.Qualifying.One.1080p.WEB.H264-GROUP"), rig.Event, null, true);

        result.IsHardRejection.Should().BeTrue();
        result.Rejections.Should().Contain(x => x.StartsWith("Session mismatch:"));
    }

    [Fact]
    public async Task IndyCarFpAliasMatchesItsPracticeEvent()
    {
        await using var rig = await PartIdentityIntegrationHarness.CreateAsync(
            title: "Practice 1",
            sport: "Motorsport", leagueName: "IndyCar Series", relational: true);
        rig.Event.Season = "2026";
        rig.Event.SeasonNumber = 2026;
        rig.Event.EventDate = new DateTime(2026, 3, 1, 18, 0, 0, DateTimeKind.Utc);

        var result = rig.Services.GetRequiredService<ReleaseMatchingService>()
            .ValidateRelease(
                Release("IndyCar.2026.FP1.1080p.WEB.H264-GROUP"),
                rig.Event, null, true);

        result.IsHardRejection.Should().BeFalse(string.Join("; ", result.Rejections));
        result.MatchReasons.Should().Contain("Session type matches: Practice 1");
    }

    [Fact]
    public async Task IndyCarFpAliasDoesNotMatchTheRaceEvent()
    {
        await using var rig = await PartIdentityIntegrationHarness.CreateAsync(
            title: "Race",
            sport: "Motorsport", leagueName: "IndyCar Series", relational: true);
        rig.Event.Season = "2026";
        rig.Event.SeasonNumber = 2026;
        rig.Event.EventDate = new DateTime(2026, 3, 1, 18, 0, 0, DateTimeKind.Utc);

        var result = rig.Services.GetRequiredService<ReleaseMatchingService>()
            .ValidateRelease(
                Release("IndyCar.2026.FP1.1080p.WEB.H264-GROUP"),
                rig.Event, null, true);

        result.IsHardRejection.Should().BeTrue();
        result.Rejections.Should().Contain("Session mismatch: release is 'Practice 1', event is 'Race'");
    }

    private static ReleaseSearchResult Release(string title) => new()
    {
        Title = title,
        Guid = "motogp-session-identity",
        DownloadUrl = "http://part-source.invalid/motogp.nzb",
        Indexer = "Fixture",
        Protocol = "Usenet",
        Size = 1500000000,
        PublishDate = new DateTime(2026, 8, 29, 10, 45, 0, DateTimeKind.Utc)
    };
}
