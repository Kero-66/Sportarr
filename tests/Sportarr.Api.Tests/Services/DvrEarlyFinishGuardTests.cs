using Sportarr.Api.Models;
using Sportarr.Api.Services;

namespace Sportarr.Api.Tests.Services;

public class DvrEarlyFinishGuardTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 9, 12, 0, 0, TimeSpan.Zero);
    private static DvrRecording Recording() => new()
    {
        Id = 1, EventId = 2, Title = "Fixture", Status = DvrRecordingStatus.Recording,
        Method = DvrRecordingMethod.Live, ActualStart = Now.AddHours(-1).UtcDateTime,
        ScheduledStart = Now.AddHours(-1).UtcDateTime, ScheduledEnd = Now.AddHours(2).UtcDateTime
    };
    private static DvrLiveScore Score(DateTimeOffset time, string? status = "FT", string? progress = null) => new()
    {
        EventId = "ev-2", LeagueId = "lg-1", Status = status, Progress = progress,
        Source = "thesportsdb:livescore", SourceFetchedAt = time, SourceUpdated = "opaque unchanged token"
    };
    private static bool Observe(DvrEarlyFinishGuard guard, DvrRecording recording,
        DvrLiveScore? score, DateTimeOffset time, int buffer = 0) =>
        guard.Observe(recording, score, "ev-2", "lg-1", time, buffer);

    [Fact]
    public void ChangingMetadataOriginStartsANewConfirmationPeriod()
    {
        var guard = new DvrEarlyFinishGuard();
        var recording = Recording();
        Observe(guard, recording, Score(Now) with { RequestOrigin = "first" }, Now);
        Assert.False(Observe(guard, recording, Score(Now.AddMinutes(1)) with { RequestOrigin = "second" }, Now.AddMinutes(1)));
        Assert.True(Observe(guard, recording, Score(Now.AddMinutes(2)) with { RequestOrigin = "second" }, Now.AddMinutes(2)));
    }

    [Fact]
    public void ZeroBufferStillRequiresSeparateFreshFetchesOneMinuteApart()
    {
        var guard = new DvrEarlyFinishGuard();
        var recording = Recording();
        Assert.False(Observe(guard, recording, Score(Now), Now));
        Assert.False(Observe(guard, recording, Score(Now), Now.AddSeconds(60)));
        Assert.False(Observe(guard, recording, Score(Now.AddSeconds(30)), Now.AddSeconds(60)));
        Assert.True(Observe(guard, recording, Score(Now.AddSeconds(60)), Now.AddSeconds(60)));
    }

    [Fact]
    public void BufferRunsFromFirstObservationAndNeedsCurrentConfirmation()
    {
        var guard = new DvrEarlyFinishGuard();
        var recording = Recording();
        Assert.False(Observe(guard, recording, Score(Now), Now, 5));
        for (var minute = 1; minute < 5; minute++)
            Assert.False(Observe(guard, recording, Score(Now.AddMinutes(minute)), Now.AddMinutes(minute), 5));
        Assert.True(Observe(guard, recording, Score(Now.AddMinutes(5)), Now.AddMinutes(5), 5));
    }

    [Theory]
    [InlineData("FT", null)]
    [InlineData(null, "Final")]
    [InlineData("", "Finished")]
    [InlineData("AET", "AET")]
    [InlineData(" AOT ", null)]
    [InlineData("Completed", null)]
    [InlineData("PEN", null)]
    [InlineData("AP", null)]
    [InlineData("FT", "90+7")]
    [InlineData("FT", "90")]
    [InlineData("FT", "00:00")]
    public void ExplicitRawFinalStatusesAreAccepted(string? status, string? progress)
        => Assert.True(Score(Now, status, progress).IsFinal);

    [Theory]
    [InlineData("Semi Final", null)]
    [InlineData("Not Finished", null)]
    [InlineData("Scheduled", null)]
    [InlineData("Suspended", null)]
    [InlineData("Postponed", null)]
    [InlineData("Cancelled", null)]
    [InlineData("FT", "OT")]
    [InlineData("Live", "Final")]
    [InlineData("Unknown", "FT")]
    [InlineData(null, null)]
    [InlineData(null, "90+7")]
    [InlineData(null, "100%")]
    public void AmbiguousOrConflictingStatusesNeverEndARecording(string? status, string? progress)
        => Assert.False(Score(Now, status, progress).IsFinal);

    [Theory]
    [InlineData("Live", null)]
    [InlineData(null, "13:05 - Q4")]
    [InlineData("IN9", null)]
    [InlineData("P", null)]
    [InlineData("PT", null)]
    [InlineData("HT", null)]
    [InlineData("P1", "6")]
    [InlineData("Q4", "8")]
    [InlineData("2H", "90+6")]
    [InlineData("1H", "45+1")]
    public void PositiveProgressCanExtendRecording(string? status, string? progress)
        => Assert.True(Score(Now, status, progress).IsInProgress);

    [Theory]
    [InlineData("NS", "Q4")]
    [InlineData("FT", "OT")]
    [InlineData("Suspended", null)]
    [InlineData("Unknown", null)]
    [InlineData(null, null)]
    public void ContradictoryOrUnknownProgressDoesNotExtend(string? status, string? progress)
        => Assert.False(Score(Now, status, progress).IsInProgress);

    [Theory]
    [InlineData("missing")]
    [InlineData("stale")]
    [InlineData("future")]
    [InlineData("wrong_event")]
    [InlineData("wrong_league")]
    [InlineData("wrong_source")]
    [InlineData("no_timestamp")]
    [InlineData("live")]
    [InlineData("conflict")]
    public void MissingInvalidOrLiveEvidenceResetsConfirmation(string kind)
    {
        var guard = new DvrEarlyFinishGuard();
        var recording = Recording();
        Observe(guard, recording, Score(Now), Now);
        var later = Now.AddMinutes(1);
        var invalid = kind switch
        {
            "missing" => null,
            "stale" => Score(later.AddMinutes(-3)),
            "future" => Score(later.AddMinutes(1)),
            "wrong_event" => Score(later) with { EventId = "ev-other" },
            "wrong_league" => Score(later) with { LeagueId = "lg-other" },
            "wrong_source" => Score(later) with { Source = "canonical" },
            "no_timestamp" => Score(later) with { SourceFetchedAt = null },
            "live" => Score(later, "Q4"),
            _ => Score(later, "FT", "Live")
        };
        Assert.False(Observe(guard, recording, invalid, later));
        Assert.False(Observe(guard, recording, Score(later.AddMinutes(1)), later.AddMinutes(1)));
        Assert.True(Observe(guard, recording, Score(later.AddMinutes(2)), later.AddMinutes(2)));
    }

    [Theory]
    [InlineData("manual")]
    [InlineData("catchup")]
    [InlineData("completed")]
    [InlineData("not_started")]
    public void OnlyRunningEventLinkedLiveRecordingsQualify(string kind)
    {
        var recording = Recording();
        if (kind == "manual") recording.EventId = null;
        if (kind == "catchup") recording.Method = DvrRecordingMethod.Catchup;
        if (kind == "completed") recording.Status = DvrRecordingStatus.Completed;
        if (kind == "not_started") recording.ActualStart = null;
        var guard = new DvrEarlyFinishGuard();
        Assert.False(Observe(guard, recording, Score(Now), Now));
        Assert.False(Observe(guard, recording, Score(Now.AddMinutes(1)), Now.AddMinutes(1)));
    }

    [Fact]
    public void EvidenceBeforeCaptureAndRestartedRecordingsCannotReuseConfirmation()
    {
        var guard = new DvrEarlyFinishGuard();
        var recording = Recording();
        Assert.False(Observe(guard, recording, Score(Now.AddHours(-2)), Now));
        Observe(guard, recording, Score(Now), Now);
        recording.ActualStart = Now.AddSeconds(30).UtcDateTime;
        Assert.False(Observe(guard, recording, Score(Now.AddMinutes(1)), Now.AddMinutes(1)));
        Assert.True(Observe(guard, recording, Score(Now.AddMinutes(2)), Now.AddMinutes(2)));
    }

    [Fact]
    public void OldUnobservedStateAndExplicitCleanupDoNotShortenLaterRecording()
    {
        var guard = new DvrEarlyFinishGuard();
        var recording = Recording();
        Observe(guard, recording, Score(Now), Now);
        Assert.False(Observe(guard, recording, Score(Now.AddMinutes(10)), Now.AddMinutes(10)));
        guard.Forget(recording.Id);
        Assert.False(Observe(guard, recording, Score(Now.AddMinutes(11)), Now.AddMinutes(11)));
        guard.RetainOnly(Array.Empty<int>());
        Assert.False(Observe(guard, recording, Score(Now.AddMinutes(12)), Now.AddMinutes(12)));
    }
}
