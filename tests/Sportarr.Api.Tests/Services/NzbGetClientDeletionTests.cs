using System.Net;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Sportarr.Api.Models;
using Sportarr.Api.Services;

namespace Sportarr.Api.Tests.Services;

public class NzbGetClientDeletionTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task CompletedHistoryUsesHistoryRemovalAndKeepsDuplicateProtection(bool deleteFiles)
    {
        var rpc = new RemovalRpc { History = SuccessfulHistory };

        (await Delete(rpc, deleteFiles)).Should().BeTrue();

        rpc.Edits.Should().ContainSingle().Which.Should().Be("HistoryDelete:42");
    }

    [Theory]
    [InlineData(true, "GroupFinalDelete:42")]
    [InlineData(false, "GroupParkDelete:42")]
    public async Task ActiveJobUsesTheRequestedFileRemovalPolicy(bool deleteFiles, string expected)
    {
        var rpc = new RemovalRpc { Queue = "[{\"NZBID\":42}]" };

        (await Delete(rpc, deleteFiles)).Should().BeTrue();

        rpc.Edits.Should().ContainSingle().Which.Should().Be(expected);
    }

    [Theory]
    [InlineData("{\"result\":false}")]
    [InlineData("{\"error\":{\"code\":3,\"message\":\"Invalid action\"}}")]
    [InlineData("{\"result\":true,\"error\":{\"code\":3}}")]
    [InlineData("{\"result\":null}")]
    [InlineData("{\"result\":1}")]
    [InlineData("{\"result\":\"true\"}")]
    [InlineData("{}")]
    [InlineData("not json")]
    public async Task RejectedOrMalformedRpcResponseIsNotSuccessfulRemoval(string response)
    {
        var rpc = new RemovalRpc { History = SuccessfulHistory, EditResponse = response };

        (await Delete(rpc, true)).Should().BeFalse();
    }

    [Theory]
    [InlineData("MANUAL", "NONE", "NONE")]
    [InlineData("NONE", "FAILURE", "NONE")]
    [InlineData("NONE", "NONE", "FAILURE")]
    [InlineData("NONE", "NONE", "PASSWORD")]
    [InlineData(null, null, null)]
    public async Task KeepingFilesRefusesHistoryActionsThatCouldDeleteTheDestination(
        string? deleteStatus, string? parStatus, string? unpackStatus)
    {
        var rpc = new RemovalRpc
        {
            History = JsonSerializer.Serialize(new[] { new
            {
                NZBID = 42, Status = "SUCCESS/GOOD", DeleteStatus = deleteStatus,
                ParStatus = parStatus, UnpackStatus = unpackStatus
            } })
        };

        (await Delete(rpc, false)).Should().BeFalse();

        rpc.Edits.Should().BeEmpty();
    }

    [Fact]
    public async Task FailedHistoryCanBeRemovedWhenFileDeletionWasRequested()
    {
        var rpc = new RemovalRpc { History = "[{\"NZBID\":42,\"Status\":\"FAILURE/UNPACK\"}]" };

        (await Delete(rpc, true)).Should().BeTrue();

        rpc.Edits.Should().ContainSingle().Which.Should().Be("HistoryDelete:42");
    }

    [Theory]
    [InlineData("listgroups")]
    [InlineData("history")]
    public async Task FailedLookupDoesNotIssueADeletion(string method)
    {
        var rpc = new RemovalRpc { FailedMethod = method };

        (await Delete(rpc, true)).Should().BeFalse();

        rpc.Edits.Should().BeEmpty();
    }

    [Fact]
    public async Task AlreadyAbsentJobIsAnIdempotentSuccess()
    {
        var rpc = new RemovalRpc();

        (await Delete(rpc, true)).Should().BeTrue();

        rpc.Edits.Should().BeEmpty();
    }

    [Fact]
    public async Task AJobFinishingDuringQueueRemovalIsRemovedFromHistory()
    {
        var rpc = new RemovalRpc
        {
            Queue = "[{\"NZBID\":42}]", History = SuccessfulHistory,
            QueueEditResponse = "{\"result\":false}"
        };

        (await Delete(rpc, true)).Should().BeTrue();

        rpc.Edits.Should().Equal("GroupFinalDelete:42", "HistoryDelete:42");
    }

    [Fact]
    public async Task ARejectedQueueRemovalIsNotReportedAsAlreadyAbsent()
    {
        var rpc = new RemovalRpc
        {
            Queue = "[{\"NZBID\":42}]", QueueEditResponse = "{\"result\":false}"
        };

        (await Delete(rpc, true)).Should().BeFalse();
    }

    private const string SuccessfulHistory = """
        [{"NZBID":7,"Status":"SUCCESS/ALL"},
         {"NZBID":42,"Status":"SUCCESS/ALL","DeleteStatus":"NONE","ParStatus":"SUCCESS","UnpackStatus":"SUCCESS"}]
        """;

    private static async Task<bool> Delete(RemovalRpc rpc, bool deleteFiles)
    {
        using var http = new HttpClient(rpc);
        var client = new NzbGetClient(http, NullLogger<NzbGetClient>.Instance);
        return await client.DeleteDownloadAsync(new DownloadClient
        {
            Name = "Isolated NZBGet", Type = DownloadClientType.NzbGet,
            Host = "localhost", Port = 6789
        }, 42, deleteFiles);
    }

    private sealed class RemovalRpc : HttpMessageHandler
    {
        public string Queue { get; init; } = "[]";
        public string History { get; init; } = "[]";
        public string EditResponse { get; init; } = "{\"result\":true,\"error\":null}";
        public string? QueueEditResponse { get; init; }
        public string? FailedMethod { get; init; }
        public List<string> Edits { get; } = new();

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            using var json = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(token));
            var method = json.RootElement.GetProperty("method").GetString();
            if (method == FailedMethod)
                return new HttpResponseMessage(HttpStatusCode.ServiceUnavailable);

            string response;
            if (method == "editqueue")
            {
                var args = json.RootElement.GetProperty("params");
                var action = args[0].GetString()!;
                var ids = args[args.GetArrayLength() - 1].EnumerateArray().Select(x => x.GetInt32());
                Edits.Add(action + ":" + string.Join(",", ids));
                response = action.StartsWith("Group", StringComparison.Ordinal)
                    ? QueueEditResponse ?? EditResponse : EditResponse;
            }
            else
            {
                response = "{\"result\":" + (method == "listgroups" ? Queue : History) + "}";
            }
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(response, Encoding.UTF8, "application/json")
            };
        }
    }
}
