using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Sportarr.Api.Endpoints;
using Sportarr.Api.Models;
using Sportarr.Api.Services;

namespace Sportarr.Api.Tests.Services;

public class PartIdentityPendingContractV2Tests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task RefusedPendingImport_CannotCompleteOrEditSurvivingFile(bool fullCollision)
    {
        await using var rig = await PartIdentityIntegrationHarness.CreateAsync();
        if (fullCollision) { rig.Settings.StandardFileFormat = "same-name"; await rig.Db.SaveChangesAsync(); }
        var held = fullCollision
            ? await PartIdentityContractV2Tests.CreateFull(rig)
            : await rig.ImportAsync("UFC.9999.Prelims", "held-prelims.1080p.WEB-DL.mkv", "Prelims");
        held.Quality = "WEBDL-1080p"; held.ReleaseGroup = "held-group"; rig.Event.Quality = held.Quality;
        await rig.Db.SaveChangesAsync();
        var bytes = await File.ReadAllBytesAsync(held.FilePath);
        var directory = NewDirectory();
        try
        {
            var pending = await AddPending(rig, directory);
            await using var app = await CreatePendingHost(rig);
            using var client = app.GetTestClient();
            using var response = await client.PostAsJsonAsync($"/api/pending-imports/{pending.Id}/accept",
                new { importMode = "copy", metadataOverrides = new { releaseGroup = "must-not-touch-survivor" } });
            var body = await response.Content.ReadAsStringAsync();
            response.StatusCode.Should().Be(HttpStatusCode.BadRequest, body);
            pending.Status.Should().Be(PendingImportStatus.Pending); pending.ResolvedAt.Should().BeNull();
            pending.ErrorMessage.Should().NotBeNullOrWhiteSpace();
            var survivor = await rig.Db.EventFiles.SingleAsync(); survivor.Id.Should().Be(held.Id);
            survivor.ReleaseGroup.Should().Be("held-group"); survivor.Quality.Should().Be("WEBDL-1080p");
            (await File.ReadAllBytesAsync(held.FilePath)).Should().Equal(bytes);
            rig.Event.FilePath.Should().Be(held.FilePath); rig.Event.Quality.Should().Be("WEBDL-1080p");
            File.Exists(pending.FilePath).Should().BeTrue();
        }
        finally { Directory.Delete(directory, true); }
    }

    [Fact]
    public async Task AcceptedDistinctPendingPart_UpdatesOnlyItsOwnFileAndPreservesFullSummary()
    {
        await using var rig = await PartIdentityIntegrationHarness.CreateAsync();
        var full = await PartIdentityContractV2Tests.CreateFull(rig);
        full.ReleaseGroup = "full-group"; await rig.Db.SaveChangesAsync();
        var bytes = await File.ReadAllBytesAsync(full.FilePath);
        var directory = NewDirectory();
        try
        {
            var pending = await AddPending(rig, directory);
            await using var app = await CreatePendingHost(rig);
            using var client = app.GetTestClient();
            using var response = await client.PostAsJsonAsync($"/api/pending-imports/{pending.Id}/accept",
                new { importMode = "copy", metadataOverrides = new { releaseGroup = "new-part-group" } });
            var body = await response.Content.ReadAsStringAsync();
            response.IsSuccessStatusCode.Should().BeTrue(body);
            pending.Status.Should().Be(PendingImportStatus.Completed); pending.ResolvedAt.Should().NotBeNull();
            var files = await rig.Db.EventFiles.ToListAsync(); files.Should().HaveCount(2);
            var part = files.Single(f => f.Id != full.Id);
            part.PartName.Should().Be("Prelims"); part.PartNumber.Should().Be(2);
            part.ReleaseGroup.Should().Be("new-part-group"); full.ReleaseGroup.Should().Be("full-group");
            (await File.ReadAllBytesAsync(full.FilePath)).Should().Equal(bytes);
            rig.Event.FilePath.Should().Be(full.FilePath); rig.Event.Quality.Should().Be("WEBDL-1080p");
            rig.Event.HasFile.Should().BeTrue();
        }
        finally { Directory.Delete(directory, true); }
    }

    [Theory]
    [InlineData("known-length")]
    [InlineData("unknown-length")]
    [InlineData("empty-body")]
    public async Task PendingAccept_ReadsPresentJsonAndPreservesEmptyBodyCompatibility(string bodyMode)
    {
        await using var rig = await PartIdentityIntegrationHarness.CreateAsync();
        var directory = NewDirectory();
        try
        {
            var pending = await AddPending(rig, directory);
            await using var app = await CreatePendingHost(rig);
            using var client = app.GetTestClient();
            var payload = new { importMode = "copy", metadataOverrides = new { releaseGroup = "body-control-group" } };
            using HttpContent content = bodyMode switch
            {
                "known-length" => new StringContent(System.Text.Json.JsonSerializer.Serialize(payload),
                    System.Text.Encoding.UTF8, "application/json"),
                "unknown-length" => JsonContent.Create(payload),
                _ => new ByteArrayContent(Array.Empty<byte>())
            };
            if (bodyMode == "known-length") content.Headers.ContentLength.Should().BeGreaterThan(0);
            else if (bodyMode == "unknown-length") content.Headers.ContentLength.Should().BeNull();
            else content.Headers.ContentLength.Should().Be(0);
            using var response = await client.PostAsync($"/api/pending-imports/{pending.Id}/accept", content);
            var body = await response.Content.ReadAsStringAsync();
            response.IsSuccessStatusCode.Should().BeTrue(body);
            pending.Status.Should().Be(PendingImportStatus.Completed);
            var file = await rig.Db.EventFiles.SingleAsync();
            file.PartName.Should().Be("Prelims"); file.PartNumber.Should().Be(2);
            if (bodyMode == "empty-body") file.ReleaseGroup.Should().BeNull();
            else file.ReleaseGroup.Should().Be("body-control-group");
        }
        finally { Directory.Delete(directory, true); }
    }

    [Fact]
    public async Task DiskPendingAccept_PreservesTheSelectedPartInFileAndHistory()
    {
        await using var rig = await PartIdentityIntegrationHarness.CreateAsync();
        var directory = NewDirectory();
        try
        {
            var path = Path.Combine(directory, "UFC.9999.2020.09.01.720p.WEB-DL.mkv");
            await File.WriteAllBytesAsync(path, Enumerable.Repeat((byte)'d', 4096).ToArray());
            var pending = new PendingImport
            {
                DownloadId = "disk-selected-part",
                Title = Path.GetFileNameWithoutExtension(path),
                FilePath = path,
                Size = 4096,
                Quality = "WEBDL-720p",
                Protocol = "Usenet",
                SuggestedEventId = rig.Event.Id,
                SuggestedPart = "Main Card"
            };
            rig.Db.PendingImports.Add(pending);
            await rig.Db.SaveChangesAsync();

            await using var app = await CreatePendingHost(rig);
            using var client = app.GetTestClient();
            using var response = await client.PostAsync($"/api/pending-imports/{pending.Id}/accept", null);
            var body = await response.Content.ReadAsStringAsync();

            response.IsSuccessStatusCode.Should().BeTrue(body);
            (await rig.Db.EventFiles.SingleAsync()).PartName.Should().Be("Main Card");
            (await rig.Db.ImportHistories.SingleAsync()).Part.Should().Be("Main Card");
        }
        finally { Directory.Delete(directory, true); }
    }

    [Theory]
    [InlineData("reject")]
    [InlineData("remove-from-client")]
    public async Task PendingRemoval_PreservesTheSelectedEventAndPartInBlocklist(string action)
    {
        await using var rig = await PartIdentityIntegrationHarness.CreateAsync();
        var pending = new PendingImport
        {
            DownloadId = "pending-removal-part",
            Title = "UFC.9999.Prelims.720p.WEB-DL",
            FilePath = "/data/e2e-lib/downloads/UFC.9999.Prelims.720p.WEB-DL.mkv",
            Size = 4096,
            Quality = "WEBDL-720p",
            Protocol = "Usenet",
            SuggestedEventId = rig.Event.Id,
            SuggestedPart = "Prelims"
        };
        rig.Db.PendingImports.Add(pending);
        await rig.Db.SaveChangesAsync();

        await using var app = await CreatePendingHost(rig);
        using var client = app.GetTestClient();
        using var response = await client.PostAsync($"/api/pending-imports/{pending.Id}/{action}", null);

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
        var blocked = await rig.Db.Blocklist.SingleAsync();
        blocked.EventId.Should().Be(rig.Event.Id);
        blocked.Part.Should().Be("Prelims");
    }

    private static string NewDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "sportarr-pending-part-v2-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path); return path;
    }

    private static async Task<PendingImport> AddPending(PartIdentityIntegrationHarness rig, string directory)
    {
        var path = Path.Combine(directory, "UFC.9999.2020.09.01.Prelims.720p.WEB-DL.mkv");
        await File.WriteAllBytesAsync(path, Enumerable.Repeat((byte)'p', 4096).ToArray());
        var pending = new PendingImport { DownloadId = "pending-part-v2", Title = Path.GetFileNameWithoutExtension(path),
            FilePath = path, Size = 4096, Quality = "WEBDL-720p", Protocol = "Usenet", SuggestedEventId = rig.Event.Id };
        rig.Db.PendingImports.Add(pending); await rig.Db.SaveChangesAsync(); return pending;
    }

    private static async Task<WebApplication> CreatePendingHost(PartIdentityIntegrationHarness rig)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Testing" });
        builder.WebHost.UseTestServer(); builder.Logging.ClearProviders();
        builder.Services.AddSingleton(rig.Db);
        builder.Services.AddSingleton(rig.Services.GetRequiredService<FileImportService>());
        builder.Services.AddSingleton(rig.Services.GetRequiredService<ConfigService>());
        builder.Services.AddSingleton(rig.Services.GetRequiredService<DownloadClientService>());
        builder.Services.AddSingleton(rig.Services.GetRequiredService<PackImportService>());
        // The mapped sibling routes must resolve as services and must never run here.
        builder.Services.AddSingleton<QueueRemovalService>(_ => throw new InvalidOperationException("Unexpected queue removal route"));
        builder.Services.AddSingleton<ImportMatchingService>(_ => throw new InvalidOperationException("Unexpected matching route"));
        builder.Services.ConfigureHttpJsonOptions(options =>
            options.SerializerOptions.ReferenceHandler = System.Text.Json.Serialization.ReferenceHandler.IgnoreCycles);
        var app = builder.Build(); app.MapQueueAndImportEndpoints(); await app.StartAsync(); return app;
    }
}
