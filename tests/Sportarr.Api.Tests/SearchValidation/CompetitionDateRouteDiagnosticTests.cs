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
}
