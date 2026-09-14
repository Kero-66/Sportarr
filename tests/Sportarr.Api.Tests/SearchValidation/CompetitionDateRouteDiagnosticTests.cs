using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Sportarr.Api.Data;
using Sportarr.Api.Models;
using Sportarr.Api.Services;
using Xunit.Abstractions;

namespace Sportarr.Api.Tests.SearchValidation;

[Collection(IndexerStatusFixtureCollection.Name)]
public sealed class CompetitionDateRouteDiagnosticTests(ITestOutputHelper output)
{
    private const string LigueOneTitle = "Ligue 1 2026 Paris Saint Germain vs AS Monaco 04 09 1080p30fps EN beIN";

    [Theory]
    [InlineData("manual", 0)]
    [InlineData("automatic", 0)]
    [InlineData("rss", 0)]
    [InlineData("manual", 1)]
    [InlineData("automatic", 1)]
    [InlineData("rss", 1)]
    [InlineData("manual", 2)]
    [InlineData("automatic", 2)]
    [InlineData("rss", 2)]
    [InlineData("manual", 3)]
    [InlineData("automatic", 3)]
    [InlineData("rss", 3)]
    public async Task CapturedCompetitionOfferTraversesOwnedRouteWithIdentityControls(string route, int variant)
    {
        await using var rig = await CompetitionDateRouteHarness.CreateAsync(variant);
        var release = rig.Release(variant);
        rig.Transport.Results = _ => new[] { release };
        Assert.Empty(rig.Transport.Searches);
        Assert.Equal(0, rig.Transport.DescriptorAttempts);
        Assert.Empty(await rig.Db.Tasks.ToListAsync());
        Assert.Empty(await rig.Db.DownloadQueue.ToListAsync());
        var initialEvent = new { rig.Event.Id, rig.Event.Title, rig.Event.Sport, rig.Event.ExternalId,
            rig.Event.EventDate, rig.Event.BroadcastDate, rig.Event.Season, rig.Event.HomeTeamName,
            rig.Event.AwayTeamName, rig.Event.Monitored, League = rig.Event.League!.Name,
            rig.Event.League.SearchQueryTemplate };
        List<ReleaseSearchResult>? manual = null;
        AppTask? task = null;
        try
        {
            if (route == "manual")
            {
                var response = await rig.RequestAsync("POST", $"/api/event/{rig.Event.Id}/search");
                manual = response.GetProperty("results").Deserialize<List<ReleaseSearchResult>>(new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
            }
            else task = await rig.RunTaskAsync(route);
        }
        finally
        {
            output.WriteLine(JsonSerializer.Serialize(new { phase = "route-capture", route, variant,
                initialEvent, pinnedOffer = release, fixtureNow = rig.Now, publicationAccommodation = rig.Publication,
                rig.Http, SourceResponses = rig.Transport.Responses, SourceQueries = rig.Transport.Searches,
                rig.Transport.DescriptorAttempts, task, manual, logs = rig.Logs.Lines.ToArray() }));
        }
        Assert.NotEmpty(rig.Transport.Searches);
        Assert.All(rig.Transport.Searches, query => Assert.Equal("search", query["t"]));
        if (route == "rss") Assert.All(rig.Transport.Searches, query => Assert.False(query.ContainsKey("q")));
        else Assert.All(rig.Transport.Searches, query => Assert.Equal(rig.Event.Title, query["q"]));
        Assert.Empty(rig.Transport.Unexpected);
        using var scope = rig.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SportarrDbContext>();
        var matching = scope.ServiceProvider.GetRequiredService<ReleaseMatchingService>();
        var parsed = matching.ParseRelease(release.Title);
        var disposition = matching.ValidateRelease(release, rig.Event, enableMultiPartEpisodes: false);
        var score = scope.ServiceProvider.GetRequiredService<ReleaseMatchScorer>().CalculateMatchScore(release.Title, rig.Event);
        var evaluation = scope.ServiceProvider.GetRequiredService<ReleaseEvaluator>().EvaluateRelease(
            release, rig.Profile, sport: rig.Event.Sport, enableMultiPartEpisodes: false);
        var queue = await db.DownloadQueue.AsNoTracking().ToListAsync();
        var pending = await db.PendingReleases.AsNoTracking().ToListAsync();
        var files = await db.EventFiles.AsNoTracking().ToListAsync();
        output.WriteLine(JsonSerializer.Serialize(new { phase = "diagnostic-disposition", route, variant,
            ParsedDate = parsed.EventDate, ComponentProbe = new { disposition.IsMatch, disposition.IsHardRejection,
                disposition.Confidence, disposition.MatchReasons, disposition.Rejections, ManualScore = score,
                evaluation.Approved, QualityRejections = evaluation.Rejections },
            ActualManual = manual, ActualTask = task, rig.Transport.DescriptorAttempts, queue, pending, files }));
        Assert.Equal(release.Title, variant == 1 ? CompetitionDateRouteHarness.ExactTitle.Replace("2022.07.15", "2022.07.17") : CompetitionDateRouteHarness.ExactTitle);
        Assert.Equal(rig.Event.EventDate.Date.AddDays(variant == 1 ? 2 : 0), parsed.EventDate);
        Assert.True(evaluation.Approved, string.Join("; ", evaluation.Rejections));
        Assert.Empty(queue);
        Assert.Empty(pending);
        Assert.Empty(files);
        if (task != null)
        {
            Assert.Equal(Sportarr.Api.Models.TaskStatus.Completed, task.Status);
            Assert.NotNull(task.Ended);
            Assert.Null(task.Exception);
        }
        if (manual != null)
        {
            var row = Assert.Single(manual);
            Assert.Equal(release.Guid, row.Guid);
            Assert.Equal(release.Title, row.Title);
            Assert.Equal(release.SportarrEventId, row.SportarrEventId);
            Assert.Equal(0, rig.Transport.DescriptorAttempts);
            if (variant == 2) Assert.True(row.Approved, string.Join("; ", row.Rejections));
            if (variant == 3) Assert.False(row.Approved);
        }
        else if (variant == 2) Assert.Equal(1, rig.Transport.DescriptorAttempts);
        else if (variant == 3) Assert.Equal(0, rig.Transport.DescriptorAttempts);
        if (variant == 2) Assert.True(disposition.IsMatch);
        if (variant == 3) Assert.True(disposition.IsHardRejection);
    }

    [Theory]
    [InlineData("manual")]
    [InlineData("automatic")]
    [InlineData("rss")]
    public async Task LigueOneOfferTraversesEverySearchRouteWithOneCompactQuery(string route)
    {
        await using var rig = await CompetitionDateRouteHarness.CreateAsync(0);
        rig.Event.Title = "Paris Saint-Germain vs Monaco";
        rig.Event.Sport = "Soccer";
        rig.Event.ExternalId = "ev-2339466";
        rig.Event.EventDate = new DateTime(2026, 9, 4, 19, 5, 0, DateTimeKind.Utc);
        rig.Event.BroadcastDate = new DateTime(2026, 9, 4);
        rig.Event.BroadcastDateVerified = true;
        rig.Event.HomeTeamId = 30;
        rig.Event.AwayTeamId = 40;
        rig.Event.HomeTeamName = "Paris SG";
        rig.Event.AwayTeamName = "Monaco";
        rig.Event.League!.Name = "French Ligue 1";
        rig.Event.League.Sport = "Soccer";
        rig.Event.League.ExternalId = "lg-000006";
        rig.Event.League.AlternateName = "Ligue 1 Conforama France";
        rig.Event.League.SearchQueryTemplate = null;
        rig.Profile.Items = new List<QualityItem>
        {
            new() { Name = "HDTV-1080p", Quality = 3, Allowed = true }
        };
        await rig.Db.SaveChangesAsync();

        var release = new ReleaseSearchResult
        {
            Title = LigueOneTitle,
            Guid = "ligue-one-psg-monaco",
            DownloadUrl = "http://" + rig.Transport.Host + "/payload/athletics-date-offer",
            Indexer = rig.Indexer.Name,
            IndexerId = rig.Indexer.Id,
            Protocol = "Torrent",
            Seeders = 20,
            PublishDate = rig.Publication,
            Size = 4_294_967_296
        };
        rig.Transport.Results = query => route == "rss" ||
            query.GetValueOrDefault("q") == "Monaco Paris Saint-Germain"
                ? new[] { release }
                : Array.Empty<ReleaseSearchResult>();

        List<ReleaseSearchResult>? manual = null;
        AppTask? task = null;
        if (route == "manual")
        {
            var response = await rig.RequestAsync("POST", $"/api/event/{rig.Event.Id}/search");
            manual = response.GetProperty("results")
                .Deserialize<List<ReleaseSearchResult>>(new JsonSerializerOptions(JsonSerializerDefaults.Web));
        }
        else
        {
            task = await rig.RunTaskAsync(route);
        }

        var query = Assert.Single(rig.Transport.Searches);
        Assert.Equal("search", query["t"]);
        if (route == "rss") Assert.False(query.ContainsKey("q"));
        else Assert.Equal("Monaco Paris Saint-Germain", query["q"]);
        Assert.Empty(rig.Transport.Unexpected);

        if (manual != null)
        {
            var result = Assert.Single(manual);
            Assert.Equal(LigueOneTitle, result.Title);
            Assert.True(result.Approved, string.Join("; ", result.Rejections));
            Assert.Equal(0, rig.Transport.DescriptorAttempts);
        }
        else
        {
            Assert.NotNull(task);
            Assert.Equal(Sportarr.Api.Models.TaskStatus.Completed, task.Status);
            Assert.Null(task.Exception);
            Assert.Equal(1, rig.Transport.DescriptorAttempts);
        }
    }
}
