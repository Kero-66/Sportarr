using FluentAssertions;
using Sportarr.Api.Helpers;
using Sportarr.Api.Models;
using Sportarr.Api.Endpoints;
using Sportarr.Api.Services;
using static Sportarr.Api.Helpers.SpecialEventClassifier;

namespace Sportarr.Api.Tests.Services;

public class SeasonFinalClassificationTests
{
    [Theory]
    [InlineData("NFL", "American Football")]
    [InlineData("NBA", "Basketball")]
    [InlineData("AFL Womens", "Australian Football")]
    [InlineData("American AHL", "Ice Hockey")]
    [InlineData("Australian National Rugby League", "Rugby League")]
    [InlineData("Indian Premier League", "Cricket")]
    [InlineData("A new league", "Soccer")]
    public void LaterFinalSeparatesPlayoffsForEveryLeague(string name, string sport)
    {
        var playoff = Game("180", 1);
        var final = Game("200", 10);
        ApplySeasonFinalContext(new[] { playoff, final });
        var league = new League { Name = name, Sport = sport, MonitorFinals = true, MonitorPlayoffs = false };
        var teams = new HashSet<string> { "followed" };
        var stages = new HashSet<int>();

        playoff.HasLaterSeasonFinal.Should().BeTrue();
        LeagueEventSyncService.IsInsideTeamSelection(playoff, league, teams, stages).Should().BeFalse();
        LeagueEventSyncService.IsInsideTeamSelection(final, league, teams, stages).Should().BeTrue();
        league.MonitorFinals = false;
        league.MonitorPlayoffs = true;
        LeagueEventSyncService.IsInsideTeamSelection(playoff, league, teams, stages).Should().BeTrue();
        LeagueEventSyncService.IsInsideTeamSelection(final, league, teams, stages).Should().BeFalse();

        // The reference final may be absent from a team-filtered library.
        LeagueEndpoints.FilterEventsByMonitoredTeams(new List<Event> { playoff }, teams, league)
            .Should().ContainSingle().Which.Should().BeSameAs(playoff);
    }

    [Fact]
    public void EarlierCupFinalDoesNotDemoteLaterChampionshipSeries()
    {
        var cup = Game("200", 1);
        var final = Game("180", 10);
        ApplySeasonFinalContext(new[] { cup, final });
        Classify(final.Round, final.Title, hasLaterSeasonFinal: final.HasLaterSeasonFinal)
            .Should().Be(SpecialTier.Final);
    }

    [Fact]
    public void ContextDoesNotCrossLeaguesOrSeasonsOrUseMissingDates()
    {
        var playoff = Game("180", 1);
        var otherSeason = Game("200", 10); otherSeason.Season = "2025";
        var otherLeague = Game("200", 10); otherLeague.LeagueId = 2;
        var undated = Game("200", 10); undated.EventDate = default;
        ApplySeasonFinalContext(new[] { playoff, otherSeason, otherLeague, undated });
        playoff.HasLaterSeasonFinal.Should().BeFalse();
        playoff.Season = null;
        undated.Season = null;
        undated.EventDate = Game("200", 10).EventDate;
        ApplySeasonFinalContext(new[] { playoff, undated });
        playoff.HasLaterSeasonFinal.Should().BeFalse();
    }

    [Theory]
    [InlineData("ev-playoff", null)]
    [InlineData("123456", null)]
    [InlineData("legacy", "123456")]
    public void RetainedRowsUseSourceDatesAfterRescheduling(string externalId, string? tsdbId)
    {
        var stored = Game("180", 20);
        stored.ExternalId = externalId; stored.TsdbId = tsdbId;
        stored.HasFile = true; stored.ManuallyMonitored = true;
        var incoming = Game("180", 1);
        incoming.ExternalId = "ev-playoff"; incoming.TsdbId = "123456";
        ApplySeasonFinalContext(new[] { stored }, new[] { incoming, Game("200", 10) });
        stored.HasLaterSeasonFinal.Should().BeTrue();
        stored.HasFile.Should().BeTrue();
        stored.ManuallyMonitored.Should().BeTrue();
    }

    [Theory]
    [InlineData("Western Conference Finals Game 7", "180")]
    [InlineData("Team A vs Team B", "Conference Final")]
    public void CleanupUsesCorrectedSourceClassification(string title, string round)
    {
        var stored = Game("180", 1); stored.ExternalId = "old-id"; stored.TsdbId = "123456";
        stored.HasFile = true; stored.ManuallyMonitored = true;
        var incoming = Game(round, 1); incoming.ExternalId = "new-id"; incoming.TsdbId = "123456";
        incoming.Title = title;
        var current = ResolveSourceEvent(stored, IndexSourceEvents(new[] { incoming }));
        BypassesTeamFilter(current.Round, current.Title, true, false).Should().BeFalse();
        stored.Title.Should().Be("Team A vs Team B");
        stored.Round.Should().Be("180");
    }

    [Fact]
    public void FullRefreshClearsObsoleteContext()
    {
        var playoff = Game("180", 1);
        ApplySeasonFinalContext(new[] { playoff, Game("200", 10) });
        playoff.HasLaterSeasonFinal.Should().BeTrue();
        ApplySeasonFinalContext(new[] { playoff });
        playoff.HasLaterSeasonFinal.Should().BeFalse();
    }

    [Theory]
    [InlineData("Conference Final", "Team A vs Team B")]
    [InlineData("Eastern Conference Finals", "Team A vs Team B")]
    [InlineData("Division Final", "Team A vs Team B")]
    [InlineData("Preliminary Final", "Team A vs Team B")]
    [InlineData("Qualifying Final", "Team A vs Team B")]
    [InlineData("Elimination Final", "Team A vs Team B")]
    [InlineData("180", "Western Conference Finals Game 7")]
    [InlineData("Playoff Final", "AFC Championship Game")]
    [InlineData("180", "NFC Championship")]
    public void ExplicitQualifyingStagesDoNotNeedSeasonContext(string round, string title)
    {
        Classify(round, title).Should().Be(SpecialTier.Playoff);
    }

    [Theory]
    [InlineData("Super Bowl XXXVIII")]
    [InlineData("NBA Finals Game 7")]
    [InlineData("Stanley Cup Final Game 6")]
    [InlineData("World Series Game 7")]
    public void NamedChampionshipsRemainFinals(string title)
    {
        Classify("180", title, hasLaterSeasonFinal: true).Should().Be(SpecialTier.Final);
    }

    [Theory]
    [InlineData("180", SpecialTier.Playoff)]
    [InlineData("Playoff Final", SpecialTier.Playoff)]
    [InlineData("Play-off Final", SpecialTier.Playoff)]
    [InlineData("200", SpecialTier.Final)]
    [InlineData("Final", SpecialTier.Final)]
    [InlineData("Grand Final", SpecialTier.Final)]
    [InlineData("17", SpecialTier.None)]
    [InlineData("500", SpecialTier.Preseason)]
    public void ContextOnlyRefinesAmbiguousPlayoffFinalRounds(string round, SpecialTier tier)
    {
        Classify(round, "Team A vs Team B", hasLaterSeasonFinal: true).Should().Be(tier);
    }

    private static Event Game(string round, int day) => new()
    {
        LeagueId = 1, Season = "2024", Round = round, Title = "Team A vs Team B",
        Sport = "Basketball", EventDate = new DateTime(2024, 6, day),
        HomeTeamExternalId = "home", AwayTeamExternalId = "away"
    };
}
