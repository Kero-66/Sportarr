using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Sportarr.Api.Data;
using Sportarr.Api.Models;
using Sportarr.Api.Services;

namespace Sportarr.Api.Tests.Services;

public class ChannelBroadcastMappingTests
{
    [Theory]
    [InlineData("Harbor Sports", null)]
    [InlineData("UK: Harbor Sports | FHD", null)]
    [InlineData("Provider 017", "Harbor Sports HD")]
    public async Task EventBroadcasterCreatesLeagueSuggestionWithoutOtherSignals(string name, string? tvgName)
    {
        await using var db = CreateDb();
        Seed(db, name, tvgName);
        await db.SaveChangesAsync();

        var mapper = Mapper(db);
        await mapper.AutoMapAllChannelsAsync();

        var mapping = Assert.Single(db.ChannelLeagueMappings);
        Assert.Equal(1, mapping.LeagueId);
        Assert.False(mapping.IsPreferred);
        Assert.False(mapping.IsManual);
        Assert.Equal(50, mapping.Confidence);
        using var signals = JsonDocument.Parse(mapping.MappingSignals!);
        Assert.Contains(signals.RootElement.EnumerateArray(), signal => signal.GetProperty("Kind").GetString() == "event_broadcasts");
        Assert.Single(await mapper.GetChannelsForLeagueByQualityAsync(1));
        await mapper.AutoMapAllChannelsAsync();
        Assert.Single(db.ChannelLeagueMappings);
    }

    [Theory]
    [InlineData("Harbor Sports 2", "Harbor Sports")]
    [InlineData("Harbor Sports+", "Harbor Sports")]
    [InlineData("Harbor Sports", "Harbor Sports 2")]
    [InlineData("Harbor Sports News", "Harbor Sports")]
    [InlineData("Sports HD", "Sports")]
    [InlineData("Provider Harbor Sports", "Harbor Sports")]
    public async Task BroadcasterEvidenceDoesNotCollapseDifferentChannels(string name, string broadcast)
    {
        await using var db = CreateDb();
        Seed(db, name, broadcast: broadcast);
        await db.SaveChangesAsync();
        await Mapper(db).AutoMapAllChannelsAsync();
        Assert.Empty(db.ChannelLeagueMappings);
    }

    [Theory]
    [InlineData(-8)]
    [InlineData(15)]
    public async Task DistantEventsDoNotSuggestChannels(int days)
    {
        await using var db = CreateDb();
        Seed(db);
        db.Events.Local.Single().EventDate = DateTime.UtcNow.AddDays(days);
        await db.SaveChangesAsync();
        await Mapper(db).AutoMapAllChannelsAsync();
        Assert.Empty(db.ChannelLeagueMappings);
    }

    [Fact]
    public async Task RepeatedBroadcasterNamesCountOncePerEvent()
    {
        await using var db = CreateDb();
        Seed(db, broadcast: "Harbor Sports / HARBOR SPORTS HD / Harbor Sports");
        db.Events.Add(new Event { Id = 2, LeagueId = 1, Title = "Second fixture", Sport = "Athletics",
            EventDate = DateTime.UtcNow.AddDays(2), Broadcast = "Harbor Sports" });
        await db.SaveChangesAsync();
        await Mapper(db).AutoMapAllChannelsAsync();
        Assert.Equal(55, Assert.Single(db.ChannelLeagueMappings).Confidence);
    }

    [Theory]
    [InlineData(true, 42, true)]
    [InlineData(true, -1, true)]
    [InlineData(false, -1, true)]
    [InlineData(false, -1, false)]
    public async Task ExplicitMappingsAndExclusionsRemainUnchanged(bool manual, int priority, bool supported)
    {
        await using var db = CreateDb();
        Seed(db, broadcast: supported ? "Harbor Sports" : "Different Network");
        db.ChannelLeagueMappings.Add(new ChannelLeagueMapping { Id = 9, ChannelId = 1, LeagueId = 1,
            IsManual = manual, IsPreferred = true, Priority = priority, Confidence = 17,
            MappingSignals = "[]", LastAutoMapped = DateTime.UtcNow.AddDays(-2) });
        await db.SaveChangesAsync();
        var original = db.ChannelLeagueMappings.Local.Single().LastAutoMapped;

        await Mapper(db).AutoMapAllChannelsAsync();

        var mapping = Assert.Single(db.ChannelLeagueMappings);
        Assert.Equal(priority, mapping.Priority);
        Assert.Equal(17, mapping.Confidence);
        Assert.Equal(original, mapping.LastAutoMapped);
        Assert.True(mapping.IsPreferred);
        if (priority < 0) Assert.Empty(await Mapper(db).GetChannelsForLeagueByQualityAsync(1));
    }

    [Fact]
    public async Task MultipleChannelNamesAndDuplicateSourceRowsCountOneEvent()
    {
        await using var db = CreateDb();
        Seed(db, tvgName: "Atlas One");
        db.Events.Local.Single().ExternalId = "ev-000001";
        db.Events.Add(new Event { Id = 2, ExternalId = "ev-000001", LeagueId = 1, Title = "Duplicate fixture",
            Sport = "Athletics", EventDate = DateTime.UtcNow.AddDays(1), Broadcast = "Harbor Sports" });
        await db.SaveChangesAsync();
        await Mapper(db).AutoMapAllChannelsAsync();
        Assert.Equal(50, Assert.Single(db.ChannelLeagueMappings).Confidence);
    }

    [Fact]
    public async Task RepeatedEventEvidenceHasACappedWeight()
    {
        await using var db = CreateDb();
        Seed(db);
        for (var id = 2; id <= 10; id++)
            db.Events.Add(new Event { Id = id, LeagueId = 1, Title = $"Fixture {id}", Sport = "Athletics",
                EventDate = DateTime.UtcNow.AddDays(1), Broadcast = "Harbor Sports" });
        await db.SaveChangesAsync();
        await Mapper(db).AutoMapAllChannelsAsync();
        Assert.Equal(65, Assert.Single(db.ChannelLeagueMappings).Confidence);
    }

    [Fact]
    public async Task CollidingLeagueNamesKeepTheirOwnEventEvidence()
    {
        await using var db = CreateDb();
        Seed(db);
        db.Leagues.Add(new League { Id = 2, Name = "Fixture Circuit", Sport = "Athletics" });
        db.Events.Add(new Event { Id = 2, LeagueId = 2, Title = "Other competition", Sport = "Athletics",
            EventDate = DateTime.UtcNow.AddDays(1), Broadcast = "Different Network" });
        await db.SaveChangesAsync();
        await Mapper(db).AutoMapAllChannelsAsync();
        Assert.Equal(1, Assert.Single(db.ChannelLeagueMappings).LeagueId);
    }

    private static SportarrDbContext CreateDb() => new(new DbContextOptionsBuilder<SportarrDbContext>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
    private static ChannelAutoMappingService Mapper(SportarrDbContext db) => new(NullLogger<ChannelAutoMappingService>.Instance, db);

    private static void Seed(SportarrDbContext db, string name = "Harbor Sports", string? tvgName = null,
        string broadcast = "Atlas One / Harbor Sports")
    {
        db.Leagues.Add(new League { Id = 1, Name = "Fixture Circuit", Sport = "Athletics" });
        db.IptvSources.Add(new IptvSource { Id = 1, Name = "Fixture", Url = "https://iptv.invalid/list", IsActive = true });
        db.IptvChannels.Add(new IptvChannel { Id = 1, Name = name, TvgName = tvgName, SourceId = 1,
            IsEnabled = true, StreamUrl = "https://iptv.invalid/stream" });
        db.Events.Add(new Event { Id = 1, LeagueId = 1, Title = "Fixture meet", Sport = "Athletics",
            EventDate = DateTime.UtcNow.AddDays(1), Broadcast = broadcast });
    }
}
