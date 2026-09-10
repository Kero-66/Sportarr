using System.Net;
using System.Reflection;
using System.Text;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Sportarr.Api.Data;
using Sportarr.Api.Models;
using Sportarr.Api.Services;

namespace Sportarr.Api.Tests.Services;

public class TvScheduleSyncTests
{
    [Theory]
    [InlineData("event")]
    [InlineData("daily")]
    [InlineData("overlap")]
    [InlineData("split-days")]
    [InlineData("legacy-id")]
    public async Task SyncPreservesAllBroadcastersAndMatchesALaterChannel(string mode)
    {
        await using var fixture = await Fixture.CreateAsync(mode);
        await fixture.SyncAsync();

        Assert.Equal("First Network / Second Network / Stream Plus", fixture.Event.Broadcast);
        var resolver = new EventChannelResolverService(fixture.Db, NullLogger<EventChannelResolverService>.Instance);
        var channels = await resolver.ResolveAsync(fixture.Event.Id);
        Assert.Contains(channels, channel => channel.ChannelId == 1 && channel.Source == "broadcast");
    }

    [Theory]
    [InlineData("empty")]
    [InlineData("wrong-event")]
    [InlineData("unavailable")]
    public async Task MissingOrUnrelatedListingsPreserveExistingBroadcast(string mode)
    {
        await using var fixture = await Fixture.CreateAsync(mode);
        fixture.Event.Broadcast = "Existing Network";
        await fixture.Db.SaveChangesAsync();

        await fixture.SyncAsync();

        Assert.Equal("Existing Network", fixture.Event.Broadcast);
    }

    private sealed class Fixture : IAsyncDisposable
    {
        private readonly string _directory;
        private readonly SqliteConnection _connection;
        private readonly ServiceProvider _provider;
        private readonly HttpClient _http;
        private readonly MemoryCache _cache;
        public SportarrDbContext Db { get; }
        public Event Event { get; }

        private Fixture(string directory, SqliteConnection connection, ServiceProvider provider,
            HttpClient http, MemoryCache cache, SportarrDbContext db, Event evt)
        {
            _directory = directory;
            _connection = connection;
            _provider = provider;
            _http = http;
            _cache = cache;
            Db = db;
            Event = evt;
        }

        public static async Task<Fixture> CreateAsync(string mode)
        {
            var directory = Path.Combine(Path.GetTempPath(), $"tv-sync-{Guid.NewGuid():N}");
            var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Sportarr:DataPath"] = directory,
                ["SportarrApi:BaseUrl"] = "https://metadata.invalid/api/v2/json"
            }).Build();
            var config = new ConfigService(configuration, NullLogger<ConfigService>.Instance);
            var connection = new SqliteConnection("Data Source=:memory:");
            await connection.OpenAsync();
            var db = new SportarrDbContext(new DbContextOptionsBuilder<SportarrDbContext>().UseSqlite(connection).Options);
            await db.Database.EnsureCreatedAsync();
            db.Leagues.Add(new League { Id = 1, Name = "Fixture League", Sport = "Soccer" });
            var evt = new Event
            {
                Id = 1, ExternalId = mode == "legacy-id" ? "12345" : "ev-000001", LeagueId = 1, Title = "Fixture Match",
                Sport = "Soccer", EventDate = DateTime.UtcNow.AddHours(2), Monitored = true
            };
            db.Events.Add(evt);
            db.IptvSources.Add(new IptvSource { Id = 1, Name = "Fixture", Url = "https://iptv.invalid/list", IsActive = true });
            db.IptvChannels.Add(new IptvChannel
            {
                Id = 1, SourceId = 1, Name = "Second Network", IsEnabled = true,
                StreamUrl = "https://iptv.invalid/stream"
            });
            await db.SaveChangesAsync();
            var http = new HttpClient(new ListingsHandler(mode));
            var cache = new MemoryCache(new MemoryCacheOptions());
            var api = new SportarrApiClient(http, NullLogger<SportarrApiClient>.Instance, configuration, config, cache);
            var provider = new ServiceCollection().AddSingleton(db).AddSingleton(api).BuildServiceProvider();
            return new Fixture(directory, connection, provider, http, cache, db, evt);
        }

        public Task SyncAsync()
        {
            var service = new TvScheduleSyncService(_provider, NullLogger<TvScheduleSyncService>.Instance);
            return (Task)typeof(TvScheduleSyncService)
                .GetMethod("PerformScheduleSyncAsync", BindingFlags.Instance | BindingFlags.NonPublic)!
                .Invoke(service, new object[] { CancellationToken.None })!;
        }

        public async ValueTask DisposeAsync()
        {
            await _provider.DisposeAsync();
            await _connection.DisposeAsync();
            _http.Dispose();
            _cache.Dispose();
            Directory.Delete(_directory, true);
        }
    }

    private sealed class ListingsHandler(string mode) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            const string listings = """
                [{"eventId":"ev-000001","network":" First Network ","channel":"First Network"},
                 {"eventId":"ev-000001","channel":"Second Network","streamingService":"Stream Plus"},
                 {"eventId":"ev-000001","channel":"second network"}]
                """;
            var isEvent = request.RequestUri!.AbsolutePath.Contains("/tv/event/");
            var rows = "[]";
            if (isEvent && mode is "event" or "overlap") rows = listings;
            if (isEvent && mode == "legacy-id") rows = listings.Replace("\"eventId\":", "\"tsdbEventId\":\"12345\",\"eventId\":");
            if (!isEvent && mode == "daily") rows = listings;
            if (!isEvent && mode == "split-days")
            {
                if (request.RequestUri.AbsolutePath.EndsWith(DateTime.UtcNow.Date.ToString("yyyy-MM-dd")))
                    rows = "[{\"eventId\":\"ev-000001\",\"channel\":\"First Network\"}]";
                if (request.RequestUri.AbsolutePath.EndsWith(DateTime.UtcNow.Date.AddDays(1).ToString("yyyy-MM-dd")))
                    rows = "[{\"eventId\":\"ev-000001\",\"channel\":\"Second Network\",\"streamingService\":\"Stream Plus\"}]";
            }
            if (!isEvent && mode == "overlap") rows = "[{\"eventId\":\"ev-000001\",\"channel\":\"First Network\"}]";
            if (mode == "wrong-event") rows = "[{\"eventId\":\"ev-999999\",\"channel\":\"Unrelated Network\"}]";
            return Task.FromResult(new HttpResponseMessage(mode == "unavailable" ? HttpStatusCode.ServiceUnavailable : HttpStatusCode.OK)
            {
                Content = new StringContent("{\"data\":{\"tvschedule\":" + rows + "}}", Encoding.UTF8, "application/json")
            });
        }
    }
}
