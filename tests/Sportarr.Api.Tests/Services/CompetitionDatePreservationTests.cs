using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Sportarr.Api.Models;
using Sportarr.Api.Services;
using Xunit.Abstractions;

namespace Sportarr.Api.Tests.Services;

public sealed class CompetitionDatePreservationTests(ITestOutputHelper output)
{
    private readonly ReleaseMatchingService _matching = new(
        NullLogger<ReleaseMatchingService>.Instance,
        new SportsFileNameParser(NullLogger<SportsFileNameParser>.Instance),
        new EventPartDetector(NullLogger<EventPartDetector>.Instance));

    [Theory]
    [InlineData(false, -1)]
    [InlineData(false, 0)]
    [InlineData(false, 1)]
    [InlineData(true, -1)]
    [InlineData(true, 0)]
    [InlineData(true, 1)]
    public void IndividualFinalKeepsBroadcastDateBasisAndOneDayRollover(bool verified, int offset)
    {
        var evt = AthleticsFinal();
        evt.BroadcastDate = evt.EventDate.Date.AddDays(-1);
        evt.BroadcastDateVerified = verified;
        var release = Release(Title(evt.BroadcastDate.Value.AddDays(offset), evt.Title));
        var result = Match(release, evt);
        Assert.False(result.IsHardRejection);
        Assert.Contains(result.MatchReasons, reason => reason == (offset == 0
            ? "Date matches exactly" : "Date within 1 day (timezone rollover)"));
    }

    [Theory]
    [InlineData(2022, false)]
    [InlineData(2021, true)]
    public void YearOnlyFinalKeepsExistingYearDisposition(int year, bool rejected)
    {
        var evt = AthleticsFinal();
        var release = Release($"Diamond.League.{year}.Womens.100.metres.Final.at.Fir.Meeting.720p.WEB-DL.H264.MULTi-FIELD");
        var parsed = _matching.ParseRelease(release.Title);
        Assert.Null(parsed.EventDate);
        Assert.Equal(year, parsed.EventYear);
        var result = Match(release, evt);
        Assert.Equal(rejected, result.IsHardRejection);
        if (rejected) Assert.Contains(result.Rejections, reason => reason.StartsWith("Year mismatch:", StringComparison.Ordinal));
        else Assert.Contains(result.MatchReasons, reason => reason == "Year matches (2022)");
    }

    [Theory]
    [InlineData(2)]
    [InlineData(3)]
    public void ExplicitPackKeepsItsExistingDateDispositionWithoutGrantingMembership(int offset)
    {
        var evt = AthleticsFinal();
        var release = Release(Title(evt.EventDate.AddDays(offset), evt.Title));
        release.IsPack = true;
        var result = Match(release, evt);
        Assert.True(release.IsPack);
        Assert.False(result.IsHardRejection);
        Assert.Contains(result.MatchReasons, reason => reason == $"Date within {offset} days");
    }

    [Fact]
    public void FinalTokenDoesNotExtendAthleticsRuleToRecurringWrestling()
    {
        var evt = AthleticsFinal();
        evt.Title = "AEW Dynamite Final";
        evt.Sport = "Wrestling";
        evt.League = new League { Id = 1, Name = "AEW", Sport = "Wrestling" };
        var release = Release("AEW.Dynamite.Final.2022.07.18.720p.WEB-DL.H264-GROUP");
        var result = Match(release, evt);
        Assert.False(result.IsHardRejection);
        Assert.Contains(result.MatchReasons, reason => reason == "Date within 3 days");
    }

    [Theory]
    [InlineData("Finalist")]
    [InlineData("Finalé")]
    [InlineData("Final2")]
    [InlineData("Final\u0301")]
    public void MetadataSubstringIsNotAnExplicitFinalProgram(string token)
    {
        var evt = AthleticsFinal();
        evt.Title = evt.Title.Replace("Final", token, StringComparison.Ordinal);
        var release = Release(Title(evt.EventDate.AddDays(2), evt.Title));
        var result = Match(release, evt);
        Assert.False(result.IsHardRejection);
        Assert.Contains(result.MatchReasons, reason => reason == "Date within 2 days");
    }

    [Fact]
    public void ReleaseGroupFinalCannotChangeMetadataHeatClassification()
    {
        var evt = AthleticsFinal();
        evt.Title = evt.Title.Replace("Final", "Heat", StringComparison.Ordinal);
        var release = Release(Title(evt.EventDate.AddDays(2), evt.Title).Replace("MULTi-FIELD", "FINAL", StringComparison.Ordinal));
        release.ReleaseGroup = "FINAL";
        var result = Match(release, evt);
        Assert.False(result.IsHardRejection);
        Assert.Contains(result.MatchReasons, reason => reason == "Date within 2 days");
    }

    [Theory]
    [InlineData(2, false)]
    [InlineData(0, true)]
    public void MatchingCanonicalIdKeepsAuthorityBeforeDateAndYearScoring(int offset, bool wrongYear)
    {
        var evt = AthleticsFinal();
        evt.ExternalId = "ev-2336155";
        var date = wrongYear ? evt.EventDate.AddYears(-1) : evt.EventDate.AddDays(offset);
        var release = Release(Title(date, evt.Title));
        release.SportarrEventId = evt.ExternalId;
        var result = Match(release, evt);
        Assert.True(result.IsMatch);
        Assert.False(result.IsHardRejection);
        Assert.Equal(100, result.Confidence);
        Assert.Equal("Sportarr id token match (ev-2336155)", Assert.Single(result.MatchReasons));
    }

    [Fact]
    public void ContradictoryCanonicalIdStillRejectsAnOtherwiseExactDatedFinal()
    {
        var evt = AthleticsFinal();
        evt.ExternalId = "ev-2336155";
        var release = Release(Title(evt.EventDate, evt.Title));
        release.SportarrEventId = "ev-2336156";
        var result = Match(release, evt);
        Assert.False(result.IsMatch);
        Assert.True(result.IsHardRejection);
        Assert.Equal(0, result.Confidence);
        Assert.Contains(result.Rejections, reason => reason.Contains("different event (ev-2336156", StringComparison.Ordinal));
    }

    private ReleaseMatchResult Match(ReleaseSearchResult release, Event evt)
    {
        var parsed = _matching.ParseRelease(release.Title);
        var result = _matching.ValidateRelease(release, evt, enableMultiPartEpisodes: false);
        output.WriteLine(JsonSerializer.Serialize(new { release.Title, release.IsPack, release.ReleaseGroup,
            SourceEventId = release.SportarrEventId, EventTitle = evt.Title, evt.ExternalId, evt.Sport,
            evt.EventDate, evt.BroadcastDate, evt.BroadcastDateVerified, parsed.EventYear,
            ParsedDate = parsed.EventDate, result.IsMatch, result.IsHardRejection, result.Confidence,
            result.MatchReasons, result.Rejections }));
        return result;
    }

    private static ReleaseSearchResult Release(string title) => new()
    {
        Title = title, Guid = "date-preservation-offer", Indexer = "Fixture",
        DownloadUrl = "http://fixture.invalid/descriptor"
    };

    private static string Title(DateTime date, string eventTitle) =>
        $"Diamond.League.{date:yyyy.MM.dd}.{eventTitle.Replace(' ', '.')}.720p.WEB-DL.H264.MULTi-FIELD";

    private static Event AthleticsFinal() => new()
    {
        Id = 1, ExternalId = "fixture:71fd4260b242d98918fbc534c47bbd53",
        Title = "Womens 100 metres Final at Fir Meeting", Sport = "Athletics", Season = "2022",
        EventDate = new DateTime(2022, 7, 15, 18, 0, 0, DateTimeKind.Utc),
        BroadcastDate = null, HomeTeamName = null, AwayTeamName = null,
        League = new League { Id = 1, Name = "Diamond League", Sport = "Athletics", AllowHighlights = false }
    };
}
