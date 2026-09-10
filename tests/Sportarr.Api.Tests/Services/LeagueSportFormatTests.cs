using FluentAssertions;
using Sportarr.Api.Models;
using Sportarr.Api.Services;
using System.Text.Json;

namespace Sportarr.Api.Tests.Services;

public class LeagueSportFormatTests
{
    [Theory]
    [InlineData("EventSport", true)]
    [InlineData(" eventsport ", true)]
    [InlineData("TeamvsTeam", false)]
    [InlineData(" teamvsteam ", false)]
    public void MetadataClassifiesSportsWithoutNameExceptions(string format, bool teamless)
        => LeagueSportRules.IsTeamlessSport("A newly supported sport", "A new league", format).Should().Be(teamless);

    [Fact]
    public void LeagueFormatCanOverrideTheSportsDefault()
    {
        LeagueSportRules.IsTeamlessSport("Athletics", "A head-to-head competition", "TeamvsTeam").Should().BeFalse();
        LeagueSportRules.IsTeamlessSport("Tennis", "A new individual tour", "EventSport").Should().BeTrue();
        LeagueSportRules.IsTeamlessSport("Tennis", "Davis Cup", "TeamvsTeam").Should().BeFalse();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("unknown")]
    public void MissingFormatRetainsCompatibility(string? format)
    {
        LeagueSportRules.IsTeamlessSport("Athletics", "Diamond League", format).Should().BeTrue();
        LeagueSportRules.IsTeamlessSport("Soccer", "Premier League", format).Should().BeFalse();
        LeagueSportRules.IsTeamlessSport("Tennis", "Davis Cup", format).Should().BeFalse();
    }

    [Fact]
    public void MetadataFormatSurvivesTheAddAndReadContracts()
    {
        var metadata = JsonSerializer.Deserialize<League>("""{"idLeague":"lg-000893","strLeague":"Diamond League","strSport":"Athletics","strSportFormat":"EventSport"}""")!;
        metadata.SportFormat.Should().Be("EventSport");
        var stored = new AddLeagueRequest { Name = metadata.Name, Sport = metadata.Sport, SportFormat = metadata.SportFormat }.ToLeague();
        LeagueResponse.FromLeague(stored).SportFormat.Should().Be("EventSport");
        SportarrLeagueDto.FromLeague(stored).StrSportFormat.Should().Be("EventSport");
    }
}
