using System.Net;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Sportarr.Api.Models;
using Sportarr.Api.Services;

namespace Sportarr.Api.Tests.Services;

/// <summary>
/// How a client reports a failure decides what the monitor does next. A status
/// of "failed" starts the failure route, which blocklists the release and can
/// remove the job. The message that travels with it becomes the blocklist
/// reason, so a stale or empty message misreports why a download failed.
/// </summary>
public class DownloadClientFailureReportingTests
{
    private sealed class FixedResponseHandler(Func<HttpRequestMessage, string> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(respond(request), Encoding.UTF8, "application/json")
            });
        }
    }

    private static DownloadClient SabConfig() => new()
    {
        Name = "Test SABnzbd",
        Type = DownloadClientType.Sabnzbd,
        Host = "localhost",
        Port = 8080,
        Category = "sportarr"
    };

    private static DownloadClient TransmissionConfig() => new()
    {
        Name = "Test Transmission",
        Type = DownloadClientType.Transmission,
        Host = "localhost",
        Port = 9091,
        Category = "sportarr"
    };

    /// <summary>
    /// SABnzbd always sends fail_message and sends it empty when it has no
    /// detail. The property is never null, so a null check never caught the
    /// empty case. The monitor skips an empty message, kept whatever text the
    /// row already had, and the blocklist entry then named the wrong reason.
    /// </summary>
    [Theory]
    [InlineData("", "Download failed")]
    [InlineData("   ", "Download failed")]
    [InlineData("Failed to decode article", "Failed to decode article")]
    public async Task SabnzbdReportsAFailureReasonEvenWhenTheClientSendsNone(
        string failMessage, string expectedMessage)
    {
        using var handler = new FixedResponseHandler(request =>
            request.RequestUri!.Query.Contains("mode=queue")
                ? """{"queue":{"slots":[]}}"""
                : JsonSerializer.Serialize(new
                {
                    history = new
                    {
                        slots = new[]
                        {
                            new
                            {
                                nzo_id = "abc", name = "Formula.1.2026.Italy.Race.1080p.WEB",
                                status = "Failed", category = "sportarr", bytes = 1000,
                                storage = "/downloads/abc", fail_message = failMessage
                            }
                        }
                    }
                }));
        using var http = new HttpClient(handler);
        var client = new SabnzbdClient(http, NullLogger<SabnzbdClient>.Instance);

        var status = await client.GetDownloadStatusAsync(SabConfig(), "abc");

        status.Should().NotBeNull();
        status!.Status.Should().Be("failed");
        status.ErrorMessage.Should().Be(expectedMessage);
    }

    /// <summary>
    /// Transmission reports trouble in a numeric field. Value 3 is a local
    /// error, which the client documents as disk full or a permissions problem.
    /// Values 1 and 2 are tracker warnings and tracker errors, which often clear
    /// on the next announce, so they must not blocklist a good release.
    /// </summary>
    [Theory]
    [InlineData(0, "downloading")]
    [InlineData(1, "downloading")]
    [InlineData(2, "downloading")]
    [InlineData(3, "failed")]
    public async Task TransmissionFailsOnlyOnALocalError(int error, string expectedStatus)
    {
        using var handler = new FixedResponseHandler(_ => JsonSerializer.Serialize(new
        {
            result = "success",
            arguments = new
            {
                torrents = new[]
                {
                    new
                    {
                        id = 1, hashString = "hash-1", name = "Formula.1.2026.Italy.Race.1080p.WEB",
                        totalSize = 1000L, percentDone = 0.5, downloadedEver = 500L,
                        uploadedEver = 0L, status = 4, eta = 60, rateDownload = 100L,
                        rateUpload = 0L, downloadDir = "/downloads", addedDate = 0L,
                        doneDate = 0L, error, errorString = "No data found! Ensure your drives are connected."
                    }
                }
            }
        }));
        using var http = new HttpClient(handler);
        var client = new TransmissionClient(http, NullLogger<TransmissionClient>.Instance);

        var status = await client.GetTorrentStatusAsync(TransmissionConfig(), "hash-1");

        status.Should().NotBeNull();
        status!.Status.Should().Be(expectedStatus);
    }

    /// <summary>
    /// A finished torrent keeps its completed state even with a local error,
    /// because the data is already on disk and import can still judge it.
    /// </summary>
    [Fact]
    public async Task TransmissionKeepsAFinishedTorrentCompletedDespiteALocalError()
    {
        using var handler = new FixedResponseHandler(_ => JsonSerializer.Serialize(new
        {
            result = "success",
            arguments = new
            {
                torrents = new[]
                {
                    new
                    {
                        id = 1, hashString = "hash-1", name = "Formula.1.2026.Italy.Race.1080p.WEB",
                        totalSize = 1000L, percentDone = 1.0, downloadedEver = 1000L,
                        uploadedEver = 0L, status = 6, eta = 0, rateDownload = 0L,
                        rateUpload = 0L, downloadDir = "/downloads", addedDate = 0L,
                        doneDate = 100L, error = 3, errorString = "Permission denied"
                    }
                }
            }
        }));
        using var http = new HttpClient(handler);
        var client = new TransmissionClient(http, NullLogger<TransmissionClient>.Instance);

        var status = await client.GetTorrentStatusAsync(TransmissionConfig(), "hash-1");

        status.Should().NotBeNull();
        status!.Status.Should().Be("completed");
    }

    /// <summary>
    /// The local error text reaches the monitor, which writes it to the row and
    /// then to the blocklist entry.
    /// </summary>
    [Fact]
    public async Task TransmissionCarriesTheLocalErrorText()
    {
        using var handler = new FixedResponseHandler(_ => JsonSerializer.Serialize(new
        {
            result = "success",
            arguments = new
            {
                torrents = new[]
                {
                    new
                    {
                        id = 1, hashString = "hash-1", name = "Formula.1.2026.Italy.Race.1080p.WEB",
                        totalSize = 1000L, percentDone = 0.5, downloadedEver = 500L,
                        uploadedEver = 0L, status = 4, eta = 60, rateDownload = 0L,
                        rateUpload = 0L, downloadDir = "/downloads", addedDate = 0L,
                        doneDate = 0L, error = 3, errorString = "No data found! Ensure your drives are connected."
                    }
                }
            }
        }));
        using var http = new HttpClient(handler);
        var client = new TransmissionClient(http, NullLogger<TransmissionClient>.Instance);

        var status = await client.GetTorrentStatusAsync(TransmissionConfig(), "hash-1");

        status.Should().NotBeNull();
        status!.ErrorMessage.Should().Be("No data found! Ensure your drives are connected.");
    }
}
