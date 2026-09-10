using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Sportarr.Api.Endpoints;

namespace Sportarr.Api.Tests.Endpoints;

public class RefreshEventsRequestTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task FullScopeAndSeasonFilterSurviveBothBodyEncodings(bool chunked)
    {
        long? receivedLength = null;
        string? receivedTransferEncoding = null;
        await using var app = CreateServer();
        app.MapPost("/", async (HttpContext context) =>
        {
            receivedLength = context.Request.ContentLength;
            receivedTransferEncoding = context.Request.Headers.TransferEncoding.ToString();
            var request = await LeagueEndpoints.ReadRefreshEventsRequestAsync(context.Request);
            return Results.Json(new { scope = request?.Scope, seasons = request?.Seasons });
        });
        await app.StartAsync();

        using var client = new HttpClient();
        using var message = new HttpRequestMessage(HttpMethod.Post, app.Urls.Single())
        {
            Version = HttpVersion.Version11,
            VersionPolicy = HttpVersionPolicy.RequestVersionExact,
            Content = chunked
                ? JsonContent.Create(new { scope = "full", seasons = new[] { "2024", "2025" } })
                : new StringContent("{\"scope\":\"full\",\"seasons\":[\"2024\",\"2025\"]}", Encoding.UTF8, "application/json")
        };

        using var response = await client.SendAsync(message);
        response.EnsureSuccessStatusCode();
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        body.RootElement.GetProperty("scope").GetString().Should().Be("full");
        body.RootElement.GetProperty("seasons").EnumerateArray().Select(value => value.GetString())
            .Should().Equal("2024", "2025");
        if (chunked)
        {
            receivedLength.Should().BeNull();
            receivedTransferEncoding.Should().Be("chunked");
        }
        else
        {
            receivedLength.Should().BeGreaterThan(0);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task EmptyBodyRetainsDefaultRefreshOptions(bool chunked)
    {
        await using var app = CreateServer();
        app.MapPost("/", async (HttpContext context) =>
        {
            var request = await LeagueEndpoints.ReadRefreshEventsRequestAsync(context.Request);
            return Results.Json(new { scope = request?.Scope ?? "current", seasons = request?.Seasons });
        });
        await app.StartAsync();

        using var client = new HttpClient();
        using var message = new HttpRequestMessage(HttpMethod.Post, app.Urls.Single())
        {
            Version = HttpVersion.Version11,
            VersionPolicy = HttpVersionPolicy.RequestVersionExact,
            Content = chunked ? new StringContent("") : null
        };
        message.Headers.TransferEncodingChunked = chunked;

        using var response = await client.SendAsync(message);
        response.EnsureSuccessStatusCode();
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        body.RootElement.GetProperty("scope").GetString().Should().Be("current");
        body.RootElement.GetProperty("seasons").ValueKind.Should().Be(JsonValueKind.Null);
    }

    private static WebApplication CreateServer()
    {
        var builder = WebApplication.CreateBuilder();
        var app = builder.Build();
        app.Urls.Add("http://127.0.0.1:0");
        return app;
    }
}
