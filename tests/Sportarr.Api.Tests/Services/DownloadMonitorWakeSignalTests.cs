using FluentAssertions;
using Sportarr.Api.Models;
using Sportarr.Api.Services;

namespace Sportarr.Api.Tests.Services;

public class DownloadMonitorWakeSignalTests
{
    [Fact]
    public async Task ConcurrentNotificationsLeaveOnlyOnePendingCheck()
    {
        using var signal = new DownloadMonitorWakeSignal();
        await Task.WhenAll(Enumerable.Range(0, 100).Select(_ => Task.Run(signal.RequestCheck)));
        (await signal.WaitAsync(TimeSpan.Zero, CancellationToken.None)).Should().BeTrue();
        (await signal.WaitAsync(TimeSpan.Zero, CancellationToken.None)).Should().BeFalse();
    }

    [Fact]
    public async Task NotificationWakesAnExistingWaiter()
    {
        using var signal = new DownloadMonitorWakeSignal();
        var wait = signal.WaitAsync(Timeout.InfiniteTimeSpan, CancellationToken.None);
        signal.RequestCheck();
        (await wait.WaitAsync(TimeSpan.FromSeconds(2))).Should().BeTrue();
    }

    [Fact]
    public async Task TimedOutWaitDoesNotConsumeALaterNotification()
    {
        using var signal = new DownloadMonitorWakeSignal();
        (await signal.WaitAsync(TimeSpan.FromMilliseconds(10), CancellationToken.None)).Should().BeFalse();
        signal.RequestCheck();
        (await signal.WaitAsync(TimeSpan.Zero, CancellationToken.None)).Should().BeTrue();
    }

    [Fact]
    public async Task CancelledWaitDoesNotConsumeALaterNotification()
    {
        using var signal = new DownloadMonitorWakeSignal();
        using var cancellation = new CancellationTokenSource();
        var wait = signal.WaitAsync(Timeout.InfiniteTimeSpan, cancellation.Token);
        cancellation.Cancel();
        Func<Task> awaitCancelled = async () => await wait;
        await awaitCancelled.Should().ThrowAsync<OperationCanceledException>();
        signal.RequestCheck();
        (await signal.WaitAsync(TimeSpan.Zero, CancellationToken.None)).Should().BeTrue();
    }

    [Fact]
    public async Task NotificationsBeforeTheNextPassAreCoveredByThatPass()
    {
        using var signal = new DownloadMonitorWakeSignal();
        signal.RequestCheck();
        signal.ClearPending();
        (await signal.WaitAsync(TimeSpan.Zero, CancellationToken.None)).Should().BeFalse();
        signal.RequestCheck();
        (await signal.WaitAsync(TimeSpan.Zero, CancellationToken.None)).Should().BeTrue();
    }

    [Theory]
    [InlineData(DownloadStatus.Downloading, 0, 0, true)]
    [InlineData(DownloadStatus.ImportPending, 0, 0, true)]
    [InlineData(DownloadStatus.Imported, 0, 0, false)]
    [InlineData(DownloadStatus.Failed, 2, 2, true)]
    [InlineData(DownloadStatus.Failed, 3, 0, false)]
    [InlineData(DownloadStatus.Failed, 0, 3, false)]
    [InlineData(DownloadStatus.Failed, null, 0, false)]
    [InlineData(DownloadStatus.Failed, 0, null, true)]
    public void CompletionNotificationsUseExistingMonitorRetryEligibility(
        DownloadStatus status, int? retries, int? importRetries, bool eligible)
    {
        var item = new DownloadQueueItem
        {
            Title = "Fixture", DownloadId = "fixture-id", Status = status,
            RetryCount = retries, ImportRetryCount = importRetries
        };
        DownloadMonitorEligibility.ActiveDownloads.Compile()(item).Should().Be(eligible);
    }
}
