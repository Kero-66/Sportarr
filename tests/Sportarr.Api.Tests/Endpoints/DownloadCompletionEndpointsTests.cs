using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using FluentValidation;
using Microsoft.AspNetCore.Builder;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Sportarr.Api.Data;
using Sportarr.Api.Endpoints;
using Sportarr.Api.Models;
using Sportarr.Api.Services;
using Sportarr.Api.Validators;

namespace Sportarr.Api.Tests.Endpoints;

public class DownloadCompletionEndpointsTests
{
    [Fact]
    public async Task CompletedDownloadMatchesCaseInsensitivelyAndTrimsInput()
    {
        await using var server = await CompletionServer.StartAsync(Item("AbC123", DownloadStatus.Completed));

        using var response = await server.Client.PostAsJsonAsync("api/download/completed", new { downloadId = "  aBc123  " });

        await AssertResponseAsync(response, matched: true, queued: true);
        (await server.Signal.WaitAsync(TimeSpan.Zero, CancellationToken.None)).Should().BeTrue();
        await using var db = server.CreateDb();
        (await db.DownloadQueue.SingleAsync()).Status.Should().Be(DownloadStatus.Completed);
    }

    [Fact]
    public async Task UnknownDownloadDoesNotWakeMonitor()
    {
        await using var server = await CompletionServer.StartAsync(Item("known", DownloadStatus.Completed));

        using var response = await server.Client.PostAsJsonAsync("api/download/completed", new { downloadId = "unknown" });

        await AssertResponseAsync(response, matched: false, queued: false);
        (await server.Signal.WaitAsync(TimeSpan.Zero, CancellationToken.None)).Should().BeFalse();
    }

    [Theory]
    [InlineData(DownloadStatus.Imported, 0, 0, false)]
    [InlineData(DownloadStatus.Failed, 3, 0, false)]
    [InlineData(DownloadStatus.Failed, 0, 3, false)]
    [InlineData(DownloadStatus.Failed, null, null, false)]
    [InlineData(DownloadStatus.Failed, 2, 2, true)]
    [InlineData(DownloadStatus.Failed, 0, null, true)]
    [InlineData(DownloadStatus.Completed, 3, 3, true)]
    [InlineData(DownloadStatus.Paused, 0, 0, true)]
    public async Task MatchingRowsFollowMonitorEligibility(DownloadStatus status, int? retries, int? importRetries, bool eligible)
    {
        var item = Item("job", status);
        item.RetryCount = retries;
        item.ImportRetryCount = importRetries;
        await using var server = await CompletionServer.StartAsync(item);

        using var response = await server.Client.PostAsJsonAsync("api/download/completed", new { downloadId = "JOB" });

        await AssertResponseAsync(response, matched: true, queued: eligible);
        (await server.Signal.WaitAsync(TimeSpan.Zero, CancellationToken.None)).Should().Be(eligible);
        await using var db = server.CreateDb();
        var stored = await db.DownloadQueue.SingleAsync();
        stored.Status.Should().Be(status);
        stored.RetryCount.Should().Be(retries);
        stored.ImportRetryCount.Should().Be(importRetries);
    }

    [Theory]
    [InlineData(null, true, true)]
    [InlineData(101, true, false)]
    [InlineData(102, true, true)]
    [InlineData(999, false, false)]
    public async Task OptionalClientFilterSelectsAmongDuplicateDownloadIds(int? clientId, bool matched, bool queued)
    {
        await using var server = await CompletionServer.StartAsync(
            Item("shared", DownloadStatus.Imported, 101),
            Item("SHARED", DownloadStatus.Completed, 102));

        using var response = await server.Client.PostAsJsonAsync("api/download/completed", new { downloadId = "Shared", downloadClientId = clientId });

        await AssertResponseAsync(response, matched, queued);
        (await server.Signal.WaitAsync(TimeSpan.Zero, CancellationToken.None)).Should().Be(queued);
    }

    [Fact]
    public async Task DuplicateCallbacksCoalesceIntoOnePendingCheck()
    {
        await using var server = await CompletionServer.StartAsync(Item("job", DownloadStatus.Completed));

        var responses = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ =>
            server.Client.PostAsJsonAsync("api/download/completed", new { downloadId = "job" })));

        foreach (var response in responses)
        {
            using (response)
                await AssertResponseAsync(response, matched: true, queued: true);
        }
        (await server.Signal.WaitAsync(TimeSpan.Zero, CancellationToken.None)).Should().BeTrue();
        (await server.Signal.WaitAsync(TimeSpan.Zero, CancellationToken.None)).Should().BeFalse();
        await using var db = server.CreateDb();
        (await db.DownloadQueue.CountAsync()).Should().Be(1);
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("{\"downloadId\":null}")]
    [InlineData("{\"downloadId\":\"\"}")]
    [InlineData("{\"downloadId\":\"   \"}")]
    [InlineData("{\"downloadId\":42}")]
    [InlineData("{\"downloadId\":\"job\",\"downloadClientId\":0}")]
    [InlineData("{\"downloadId\":\"job\",\"downloadClientId\":-1}")]
    [InlineData("{\"downloadId\":\"job\",\"downloadClientId\":\"bad\"}")]
    [InlineData("{")]
    [InlineData("null")]
    [InlineData("")]
    public async Task InvalidRequestsReturnBadRequestWithoutWakingMonitor(string json)
    {
        await using var server = await CompletionServer.StartAsync(Item("job", DownloadStatus.Completed));

        using var response = await server.Client.PostAsync("api/download/completed", new StringContent(json, Encoding.UTF8, "application/json"));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await server.Signal.WaitAsync(TimeSpan.Zero, CancellationToken.None)).Should().BeFalse();
    }

    [Theory]
    [InlineData(512, HttpStatusCode.OK)]
    [InlineData(513, HttpStatusCode.BadRequest)]
    public async Task DownloadIdLengthIsBounded(int length, HttpStatusCode expectedStatus)
    {
        await using var server = await CompletionServer.StartAsync();

        using var response = await server.Client.PostAsJsonAsync("api/download/completed", new { downloadId = new string('a', length) });

        response.StatusCode.Should().Be(expectedStatus);
        (await server.Signal.WaitAsync(TimeSpan.Zero, CancellationToken.None)).Should().BeFalse();
    }

    private static DownloadQueueItem Item(string downloadId, DownloadStatus status, int clientId = 101) => new()
    {
        EventId = 201,
        DownloadClientId = clientId,
        DownloadId = downloadId,
        Title = "Test download",
        Status = status
    };

    private static async Task AssertResponseAsync(HttpResponseMessage response, bool matched, bool queued)
    {
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        body.RootElement.GetProperty("matched").GetBoolean().Should().Be(matched);
        body.RootElement.GetProperty("queued").GetBoolean().Should().Be(queued);
    }

    private sealed class CompletionServer : IAsyncDisposable
    {
        private readonly SqliteConnection _keeper;
        private readonly WebApplication _app;
        private readonly string _connectionString;

        public HttpClient Client { get; }
        public DownloadMonitorWakeSignal Signal => _app.Services.GetRequiredService<DownloadMonitorWakeSignal>();

        private CompletionServer(SqliteConnection keeper, WebApplication app, string connectionString)
        {
            _keeper = keeper;
            _app = app;
            _connectionString = connectionString;
            Client = new HttpClient { BaseAddress = new Uri(app.Urls.Single()) };
        }

        public SportarrDbContext CreateDb() => new(new DbContextOptionsBuilder<SportarrDbContext>()
            .UseSqlite(_connectionString).Options);

        public static async Task<CompletionServer> StartAsync(params DownloadQueueItem[] items)
        {
            var connectionString = $"Data Source=completion-{Guid.NewGuid():N};Mode=Memory;Cache=Shared;Pooling=False";
            var keeper = new SqliteConnection(connectionString);
            await keeper.OpenAsync();
            var builder = WebApplication.CreateBuilder();
            builder.Logging.ClearProviders();
            builder.Services.AddDbContext<SportarrDbContext>(options => options.UseSqlite(connectionString));
            builder.Services.AddSingleton<DownloadMonitorWakeSignal>();
            builder.Services.AddValidatorsFromAssemblyContaining<TaskRequestValidator>();
            var app = builder.Build();
            app.Urls.Add("http://127.0.0.1:0");
            app.MapDownloadCompletionEndpoints();
            await using (var scope = app.Services.CreateAsyncScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<SportarrDbContext>();
                await db.Database.EnsureCreatedAsync();
                db.Events.Add(new Event { Id = 201, Title = "Test event", Sport = "Soccer", EventDate = DateTime.UtcNow });
                db.DownloadClients.AddRange(
                    new DownloadClient { Id = 101, Name = "Client A", Host = "client-a.invalid" },
                    new DownloadClient { Id = 102, Name = "Client B", Host = "client-b.invalid" });
                db.DownloadQueue.AddRange(items);
                await db.SaveChangesAsync();
            }
            await app.StartAsync();
            return new CompletionServer(keeper, app, connectionString);
        }

        public async ValueTask DisposeAsync()
        {
            Client.Dispose();
            await _app.DisposeAsync();
            await _keeper.DisposeAsync();
        }
    }
}
