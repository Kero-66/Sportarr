using System.Net;
using System.Text;
using System.Web;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Sportarr.Api.Models;
using Sportarr.Api.Services;

namespace Sportarr.Api.Tests.Services;

public class SabnzbdClientDeletionTests
{
    [Theory]
    [InlineData("{\"status\":false}")]
    [InlineData("invalid json")]
    [InlineData("{\"status\":true,\"nzo_ids\":[\"unrelated\"]}")]
    public async Task EmptyQueueAcknowledgementCannotHideFailedHistoryRemoval(string response)
    {
        var rpc = new RemovalApi { History = Target, HistoryResponse = response };
        (await Delete(rpc)).Should().BeFalse();
        rpc.Edits.Should().Equal("history:delete");
    }

    [Theory]
    [InlineData("{\"status\":true}")]
    [InlineData("{\"status\":true,\"nzo_ids\":[]}")]
    [InlineData("{\"status\":true,\"nzo_ids\":[\"job-42\"]}")]
    public async Task KnownHistoryRemovalAcceptsSupportedAcknowledgements(string response)
    {
        var rpc = new RemovalApi { History = Target, HistoryResponse = response };
        (await Delete(rpc)).Should().BeTrue();
        rpc.Edits.Should().Equal("history:delete");
        rpc.Queries.Should().Contain("history:job-42");
    }

    [Fact]
    public async Task AQueueRemovalThatMovesIntoHistoryRequiresHistorySuccess()
    {
        var rpc = new RemovalApi { Queue = Target, History = Target, HistoryResponse = "{\"status\":false}" };
        (await Delete(rpc)).Should().BeFalse();
        rpc.Edits.Should().Equal("queue:delete", "history:delete");
    }

    [Fact]
    public async Task ARejectedQueueRemovalCanFinishInHistory()
    {
        var rpc = new RemovalApi { Queue = Target, QueueResponse = "{\"status\":false}", History = Target };
        (await Delete(rpc)).Should().BeTrue();
        rpc.Edits.Should().Equal("queue:delete", "history:delete");
    }

    [Fact]
    public async Task ConfirmedQueueRemovalWithNoHistorySucceeds()
    {
        var rpc = new RemovalApi { Queue = Target, HistoryResponse = "{\"status\":false}" };
        (await Delete(rpc)).Should().BeTrue();
        rpc.Edits.Should().Equal("queue:delete");
    }

    [Fact]
    public async Task RejectedQueueRemovalIsNotReportedAsAlreadyAbsent()
    {
        var rpc = new RemovalApi { Queue = Target, QueueResponse = "{\"status\":false}" };
        (await Delete(rpc)).Should().BeFalse();
    }

    [Fact]
    public async Task AlreadyAbsentJobSucceedsWithoutMutations()
    {
        var rpc = new RemovalApi();
        (await Delete(rpc)).Should().BeTrue();
        rpc.Edits.Should().BeEmpty();
    }

    [Fact]
    public async Task UnrelatedQueueEntriesDoNotSelectQueueRemoval()
    {
        var rpc = new RemovalApi { Queue = "[{\"nzo_id\":\"unrelated\"}]", History = Target };
        (await Delete(rpc)).Should().BeTrue();
        rpc.Edits.Should().Equal("history:delete");
    }

    [Theory]
    [InlineData("queue")]
    [InlineData("history")]
    public async Task FailedLookupPreventsUnconfirmedRemoval(string mode)
    {
        var rpc = new RemovalApi { FailedLookup = mode };
        (await Delete(rpc)).Should().BeFalse();
        rpc.Edits.Should().BeEmpty();
    }

    [Fact]
    public async Task HistoryLookupFailureAfterQueueRemovalDoesNotConfirmFullCleanup()
    {
        var rpc = new RemovalApi { Queue = Target, FailedLookup = "history" };
        (await Delete(rpc)).Should().BeFalse();
        rpc.Edits.Should().Equal("queue:delete");
    }

    [Fact]
    public async Task HistoryHttpFailureDoesNotCountAsRemoval()
    {
        var rpc = new RemovalApi { History = Target, FailHistoryEdit = true };
        (await Delete(rpc)).Should().BeFalse();
    }

    [Fact]
    public async Task KeepFilesDoesNotSendHistoryFileDeletionFlag()
    {
        var rpc = new RemovalApi { History = Target };
        (await Delete(rpc, false)).Should().BeTrue();
        rpc.DeleteFiles.Should().Equal(false);
    }

    [Fact]
    public async Task QueuedSabnzbdJobUsesDeleteWithoutDeletingFiles()
    {
        var rpc = new RemovalApi { Queue = Target };
        (await Delete(rpc, false, DownloadClientType.Sabnzbd)).Should().BeTrue();
        rpc.Edits.Should().Equal("queue:delete");
        rpc.DeleteFiles.Should().Equal(false);
    }

    [Fact]
    public async Task QueueListingIsNotASuccessfulRemovalAcknowledgement()
    {
        var rpc = new RemovalApi { Queue = Target, QueueResponse = "{\"queue\":{\"slots\":[]}}" };
        (await Delete(rpc, false)).Should().BeFalse();
    }

    [Fact]
    public async Task QueuedNzbDavJobUsesSupportedDeleteWhenKeepingFiles()
    {
        var rpc = new RemovalApi { Queue = Target };
        (await Delete(rpc, false)).Should().BeTrue();
        rpc.Edits.Should().Equal("queue:delete");
        rpc.DeleteFiles.Should().Equal(false);
    }

    [Fact]
    public async Task NzbDavJobFinishingDuringRemovalPreservesMountedFiles()
    {
        var rpc = new RemovalApi { Queue = Target, History = Target };
        (await Delete(rpc, false)).Should().BeTrue();
        rpc.Edits.Should().Equal("queue:delete", "history:delete");
        rpc.DeleteFiles.Should().Equal(false, false);
        rpc.DeleteMountedFiles.Should().OnlyContain(value => !value);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task NzbDavHistoryCleanupDoesNotRemoveMountedLibraryContent(bool deleteFiles)
    {
        var rpc = new RemovalApi { History = Target };
        (await Delete(rpc, deleteFiles)).Should().BeTrue();
        rpc.Edits.Should().Equal("history:delete");
        rpc.DeleteMountedFiles.Should().Equal(false);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task DecypharrKeepingFilesRefusesDestructiveRemoval(bool queued)
    {
        var rpc = new RemovalApi { Queue = queued ? Target : "[]", History = queued ? "[]" : Target };
        (await Delete(rpc, false, DownloadClientType.DecypharrUsenet)).Should().BeFalse();
        rpc.Edits.Should().BeEmpty();
    }

    [Fact]
    public async Task AbsentDecypharrJobNeedsNoDestructiveRemoval()
    {
        var rpc = new RemovalApi();
        (await Delete(rpc, false, DownloadClientType.DecypharrUsenet)).Should().BeTrue();
        rpc.Edits.Should().BeEmpty();
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task DecypharrStillRemovesJobsWhenFileDeletionIsRequested(bool queued)
    {
        var rpc = new RemovalApi { Queue = queued ? Target : "[]", History = queued ? "[]" : Target };
        (await Delete(rpc, true, DownloadClientType.DecypharrUsenet)).Should().BeTrue();
        rpc.Edits.Should().Equal(queued ? "queue:delete" : "history:delete");
        rpc.DeleteFiles.Should().Equal(true);
    }

    private const string Target = "[{\"nzo_id\":\"job-42\"}]";

    private static async Task<bool> Delete(RemovalApi rpc, bool deleteFiles = true, DownloadClientType type = DownloadClientType.NZBdav)
    {
        using var http = new HttpClient(rpc);
        var client = new SabnzbdClient(http, NullLogger<SabnzbdClient>.Instance);
        return await client.DeleteDownloadAsync(new DownloadClient
        {
            Name = "Isolated compatible client", Type = type,
            Host = "localhost", Port = 8080, ApiKey = "fixture-key"
        }, "job-42", deleteFiles);
    }

    private sealed class RemovalApi : HttpMessageHandler
    {
        public string Queue { get; init; } = "[]";
        public string History { get; init; } = "[]";
        public string QueueResponse { get; init; } = "{\"status\":true}";
        public string HistoryResponse { get; init; } = "{\"status\":true}";
        public string? FailedLookup { get; init; }
        public bool FailHistoryEdit { get; init; }
        public List<string> Edits { get; } = new();
        public List<string> Queries { get; } = new();
        public List<bool> DeleteFiles { get; } = new();
        public List<bool> DeleteMountedFiles { get; } = new();

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            var query = HttpUtility.ParseQueryString(request.RequestUri!.Query);
            var mode = query["mode"]!;
            var action = query["name"];
            string response;
            if (action != null)
            {
                Edits.Add(mode + ":" + action);
                DeleteFiles.Add(query["del_files"] == "1");
                DeleteMountedFiles.Add(query["del_completed_files"] == "1");
                if (mode == "history" && FailHistoryEdit)
                    return Task.FromResult(new HttpResponseMessage(HttpStatusCode.InternalServerError));
                response = mode == "queue" ? QueueResponse : HistoryResponse;
            }
            else
            {
                Queries.Add(mode + ":" + query["nzo_ids"]);
                if (mode == FailedLookup)
                    return Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
                response = "{\"" + mode + "\":{\"slots\":" + (mode == "queue" ? Queue : History) + "}}";
            }
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(response, Encoding.UTF8, "application/json")
            });
        }
    }
}
