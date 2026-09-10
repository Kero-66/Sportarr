using FluentAssertions;
using Sportarr.Api.Endpoints;
using Sportarr.Api.Models;
using Sportarr.Api.Services;

namespace Sportarr.Api.Tests.Services;

public class AthleticsTeamFilteringTests
{
    [Theory]
    [InlineData("Athletics")]
    [InlineData("athletics")]
    public void AthleticsDoesNotUseHomeAwayTeamFiltering(string sport)
        => LeagueSportRules.IsTeamlessSport(sport, "Diamond League").Should().BeTrue();

    [Theory]
    [InlineData("2025", 72)]
    [InlineData("2024", 26)]
    [InlineData("2023", 42)]
    [InlineData("2022", 89)]
    [InlineData("2021", 7)]
    public void CountrySelectionsDoNotHideIndividualEvents(string season, int count)
    {
        var league = new League
        {
            ExternalId = "lg-000893", Name = "Diamond League", Sport = "Athletics",
            KeepAllEvents = false, MonitorFinals = false, MonitorPlayoffs = false,
            MonitoredTeams = Enumerable.Range(1, 39).Select(i => new LeagueTeam
            {
                Monitored = true, Team = new Team { ExternalId = $"country-{i}", Name = $"Country {i}" }
            }).ToList()
        };
        var events = Enumerable.Range(1, count).Select(i => new Event
        {
            Title = "Mens 100 metres Final", Sport = "Athletics", Season = season,
            EventDate = new DateTime(int.Parse(season), 6, 1)
        }).ToList();

        LeagueEndpoints.SelectVisibleEvents(events, league, showAll: false).Should().HaveCount(count);
    }

    [Theory]
    [InlineData("Soccer", "English Premier League")]
    [InlineData("American Football", "NFL")]
    [InlineData("Tennis", "Davis Cup")]
    public void TeamCompetitionsStillUseTeamFiltering(string sport, string name)
        => LeagueSportRules.IsTeamlessSport(sport, name).Should().BeFalse();
}
