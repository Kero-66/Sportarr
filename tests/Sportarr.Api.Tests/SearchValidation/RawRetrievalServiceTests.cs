using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Sportarr.Api.Data;
using Sportarr.Api.Models;
using Sportarr.Api.Services;
using Xunit.Abstractions;

namespace Sportarr.Api.Tests.SearchValidation;

public sealed class RawRetrievalServiceTests(ITestOutputHelper output)
{
    [Theory]
    [InlineData(IndexerType.Newznab)]
    [InlineData(IndexerType.Torznab)]
    public async Task RawHitsPreserveQuotaAndHealthAndRespectNewUnavailability(IndexerType protocol)
    {
        await using var rig = await QuotaIncompleteCacheHarness.CreateAsync(output, quotaConstrained: false);
        await ConfigureProtocol(rig, protocol);
        var service = rig.Services.GetRequiredService<IndexerSearchService>();
        Task<SearchOperationOutcome> Search() => service.SearchAllIndexersDetailedAsync("cache-primary", 100,
            qualityProfileId: rig.Profile.Id, sportarrId: rig.Event.ExternalId, cacheSuccessfulSources: true);
        var cold = await Search();
        var expected = JsonSerializer.Serialize(cold.Releases);
        var before = await ReadStatus(rig);
        var attempts = rig.Transport.Attempts.Length;
        var hit = await Search();
        Assert.Equal(3, cold.Releases.Count);
        Assert.Equal(expected, JsonSerializer.Serialize(hit.Releases));
        Assert.Equal(attempts, rig.Transport.Attempts.Length);
        var after = await ReadStatus(rig);
        Assert.Equal(before.QueriesThisHour, after.QueriesThisHour);
        Assert.Equal(before.LastSuccess, after.LastSuccess);
        Assert.Equal(before.QueryFailures, after.QueryFailures);
        await rig.Services.GetRequiredService<IndexerStatusService>().RecordRateLimitedAsync(rig.Indexer.Id, TimeSpan.FromMinutes(5));
        var unavailable = await Search();
        Assert.Empty(unavailable.Releases);
        Assert.Contains(unavailable.Diagnostics, d => d.Termination == SearchTermination.Unavailable);
        Assert.Equal(attempts, rig.Transport.Attempts.Length);
        var blocked = await ReadStatus(rig);
        Assert.Equal(before.LastSuccess, blocked.LastSuccess);
        Assert.NotNull(blocked.RateLimitedUntil);
        Assert.Empty(rig.Transport.Violations);
    }

    [Theory]
    [InlineData(IndexerType.Newznab)]
    [InlineData(IndexerType.Torznab)]
    public async Task DuplicateRowsStillSharePagesWithRawGatesEnabled(IndexerType protocol)
    {
        await using var rig = await QuotaIncompleteCacheHarness.CreateAsync(output, quotaConstrained: false);
        await ConfigureProtocol(rig, protocol);
        var copy = new Indexer { Name = "Second row", Type = protocol, Url = rig.Indexer.Url,
            ApiPath = rig.Indexer.ApiPath, ApiKey = rig.Indexer.ApiKey, RequestDelayMs = rig.Indexer.RequestDelayMs,
            Categories = rig.Indexer.Categories.ToList(), Enabled = true, EnableAutomaticSearch = true,
            EnableInteractiveSearch = true, EnableRss = false, QueryLimit = 100 };
        rig.Db.Indexers.Add(copy);
        await rig.Db.SaveChangesAsync();
        var service = rig.Services.GetRequiredService<IndexerSearchService>();
        Task<SearchOperationOutcome> Search() => service.SearchAllIndexersDetailedAsync("cache-primary", 100,
            qualityProfileId: rig.Profile.Id, sportarrId: rig.Event.ExternalId, cacheSuccessfulSources: true);
        var cold = await Search().WaitAsync(TimeSpan.FromSeconds(15));
        Assert.Equal(6, cold.Releases.Count);
        Assert.Equal(2, rig.Transport.Attempts.Count(a => a.Mode == "search"));
        Assert.Single(rig.Transport.Attempts.Where(a => a.Mode == "caps"));
        Assert.Equal(3, cold.Releases.Count(r => r.IndexerId == copy.Id && r.Indexer == copy.Name));
        var attempts = rig.Transport.Attempts.Length;
        var hit = await Search();
        Assert.Equal(6, hit.Releases.Count);
        Assert.Equal(attempts, rig.Transport.Attempts.Length);
        await using var db = await rig.Services.GetRequiredService<IDbContextFactory<SportarrDbContext>>().CreateDbContextAsync();
        Assert.Equal(attempts, await db.IndexerStatuses.SumAsync(s => s.QueriesThisHour));
        Assert.Empty(rig.Transport.Violations);
    }

    [Fact]
    public async Task RawEvidenceKeepsItsDeadlineThroughSourceRecoveryAndManualHttpCache()
    {
        var clock = new ManualClock();
        await using var rig = await QuotaIncompleteCacheHarness.CreateAsync(output, quotaConstrained: false, cacheClock: clock);
        rig.Indexer.QueryLimit = 100;
        await rig.Db.SaveChangesAsync();
        rig.Transport.RequestCeiling = 30;
        var service = rig.Services.GetRequiredService<IndexerSearchService>();
        foreach (var query in QuotaIncompleteCacheHarness.Queries)
            await service.SearchAllIndexersDetailedAsync(query, 10000, sportarrId: rig.Event.ExternalId,
                useCategoryFilter: false, cacheSuccessfulSources: true);
        var unavailable = await MixedSourceCacheTests.AddUnavailableSourceAsync(rig);
        clock.Advance(290);
        rig.StartMeasurement();
        var partial = await rig.RunAsync("raw-promotion-partial", false);
        Assert.Equal(5, partial.Found);
        Assert.Empty(partial.Attempts);
        Assert.All(partial.CacheEntries, e => Assert.Null(e.Guids));
        clock.Advance(5);
        await MixedSourceCacheTests.SetAvailableAsync(rig, unavailable.Id);
        var recovered = await rig.RunAsync("raw-promotion-recovered", false);
        Assert.Equal(10, recovered.Found);
        Assert.All(recovered.Attempts, a => Assert.Equal(unavailable.Id.ToString(), a.RowId));
        Assert.All(recovered.CacheEntries, e => Assert.InRange(e.LifetimeSeconds!.Value, 1, 5));
        var warm = await rig.RunAsync("raw-promotion-warm", false);
        Assert.Empty(warm.Attempts);
        clock.Advance(6);
        var expired = await rig.RunAsync("raw-promotion-expired", false);
        Assert.Contains(expired.Attempts, a => a.Mode == "search" && a.RowId == rig.Indexer.Id.ToString());
        Assert.Equal(10, expired.Found);
        Assert.Empty(rig.Transport.Violations);
    }

    private static async Task ConfigureProtocol(QuotaIncompleteCacheHarness rig, IndexerType protocol)
    {
        rig.Indexer.Type = protocol;
        if (protocol == IndexerType.Torznab)
            rig.Db.DownloadClients.Add(new DownloadClient { Name = "Fixture torrent", Type = DownloadClientType.TorrentBlackhole,
                Host = "localhost", Enabled = true, ReadOnly = true });
        await rig.Db.SaveChangesAsync();
    }

    private static async Task<IndexerStatus> ReadStatus(QuotaIncompleteCacheHarness rig)
    {
        await using var db = await rig.Services.GetRequiredService<IDbContextFactory<SportarrDbContext>>().CreateDbContextAsync();
        return await db.IndexerStatuses.AsNoTracking().SingleAsync(s => s.IndexerId == rig.Indexer.Id);
    }

    private sealed class ManualClock : TimeProvider
    {
        private DateTimeOffset now = DateTimeOffset.UtcNow;
        public override DateTimeOffset GetUtcNow() => now;
        internal void Advance(int seconds) => now += TimeSpan.FromSeconds(seconds);
    }
}
