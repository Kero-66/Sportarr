using System.Net;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Sportarr.Api.Data;
using Sportarr.Api.Models;
using Sportarr.Api.Services;

namespace Sportarr.Api.Tests.Services;

public class NotificationServiceTriggerTests : IDisposable
{
    private readonly SqliteConnection _connection = new("DataSource=:memory:");

    public NotificationServiceTriggerTests()
    {
        _connection.Open();
    }

    public void Dispose() => _connection.Dispose();

    [Fact]
    public async Task EpgSyncCompleted_OnlySendsWhenTheConnectionEnablesIt()
    {
        var options = new DbContextOptionsBuilder<SportarrDbContext>()
            .UseSqlite(_connection)
            .Options;
        await using (var setupDb = new SportarrDbContext(options))
        {
            await setupDb.Database.EnsureCreatedAsync();
            setupDb.Notifications.Add(new Notification
            {
                Name = "EPG webhook",
                Implementation = "Webhook",
                Enabled = true,
                ConfigJson = "{\"webhook\":\"http://example.com/hook\",\"onEpgSyncCompleted\":false}"
            });
            await setupDb.SaveChangesAsync();
        }

        var handler = new CountingHandler();
        using var services = new ServiceCollection()
            .AddScoped(_ => new SportarrDbContext(options))
            .AddSingleton(new ConfigService(new ConfigurationBuilder().Build(), NullLogger<ConfigService>.Instance))
            .BuildServiceProvider();
        var httpClient = new HttpClient(handler);
        var httpClientFactory = new FixedHttpClientFactory(httpClient);
        var service = new NotificationService(
            services,
            NullLogger<NotificationService>.Instance,
            httpClient,
            httpClientFactory);

        var delivered = await service.SendNotificationAsync(
            NotificationTrigger.OnEpgSyncCompleted,
            "EPG sync completed",
            "Sports Guide synced successfully.");

        delivered.Should().BeFalse();
        handler.RequestCount.Should().Be(0);
    }

    private sealed class FixedHttpClientFactory(HttpClient client) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => client;
    }

    private sealed class CountingHandler : HttpMessageHandler
    {
        public int RequestCount { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            RequestCount++;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
        }
    }
}
