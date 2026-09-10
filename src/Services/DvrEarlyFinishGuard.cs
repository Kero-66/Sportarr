using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Sportarr.Api.Models;

namespace Sportarr.Api.Services;

public sealed record DvrLiveScore
{
    [JsonPropertyName("idEvent")]
    public string? EventId { get; init; }
    [JsonPropertyName("idLeague")]
    public string? LeagueId { get; init; }
    [JsonPropertyName("strStatus")]
    public string? Status { get; init; }
    [JsonPropertyName("strProgress")]
    public string? Progress { get; init; }
    public string? Source { get; init; }
    public DateTimeOffset? SourceFetchedAt { get; init; }
    public string? SourceUpdated { get; init; }
    [JsonIgnore]
    public string? RequestOrigin { get; init; }

    public bool IsFresh(DateTimeOffset now) => Source == "thesportsdb:livescore" &&
        SourceFetchedAt.HasValue && SourceFetchedAt <= now.AddSeconds(5) &&
        SourceFetchedAt >= now.AddSeconds(-120);

    public bool IsFinal => HasOnlyPhase(Phase.Final);
    public bool IsInProgress => HasOnlyPhase(Phase.Live);

    private bool HasOnlyPhase(Phase phase)
    {
        // Progress can be a game clock, including stoppage time, rather than a status.
        if (!string.IsNullOrWhiteSpace(Status) && Progress is { Length: <= 100 } &&
            Regex.IsMatch(Progress.Trim(), @"^\d{1,3}(?:\+\d{1,2}|:\d{2}|(?:\.\d+)?%)?$", RegexOptions.CultureInvariant))
            return Classify(Status) == phase;
        var values = new[] { Status, Progress }.Where(value => !string.IsNullOrWhiteSpace(value));
        return values.Any() && values.All(value => Classify(value!) == phase);
    }

    private enum Phase { Unknown, Live, Final }

    private static Phase Classify(string value)
    {
        var status = value.Trim().ToLowerInvariant();
        if (status is "ft" or "aet" or "aot" or "ap" or "pen" or "final" or "finished" or
            "completed" or "ended" or "match finished" or "game finished" or "game ended" or
            "match ended" or "full time" or "full-time" or "after extra time" or
            "after overtime" or "after over time" or "after penalties")
            return Phase.Final;

        if (status is "live" or "in progress" or "in_progress" or "1h" or "2h" or "ht" or
            "bt" or "ot" or "et" or "p" or "pt" or "1st half" or "2nd half")
            return Phase.Live;

        if (status.Length <= 100 && Regex.IsMatch(status,
            @"^(?:(?:q|in|p|s)[1-9]\d*|\d{1,3}:\d{2}(?: - (?:q?[1-9]\d*(?:st|nd|rd|th)?|ot|et))?|[1-9]\d*(?:st|nd|rd|th) (?:quarter|period|inning|set)|(?:top|bottom) [1-9]\d*(?:st|nd|rd|th)?)$",
            RegexOptions.CultureInvariant))
            return Phase.Live;

        return Phase.Unknown;
    }
}

public sealed class DvrEarlyFinishGuard
{
    private sealed record Confirmation(int EventId, DateTime ActualStart, string ExternalId, string? RequestOrigin,
        string LeagueId, DateTimeOffset FirstSeen, DateTimeOffset FirstFetch,
        DateTimeOffset LastFetch, DateTimeOffset LastSeen);

    private readonly Dictionary<int, Confirmation> _confirmations = new();
    private readonly object _gate = new();

    public bool Observe(DvrRecording recording, DvrLiveScore? score, string eventId,
        string leagueId, DateTimeOffset now, int bufferMinutes)
    {
        lock (_gate)
        {
            if (recording.Status != DvrRecordingStatus.Recording ||
                recording.Method != DvrRecordingMethod.Live || !recording.EventId.HasValue ||
                !recording.ActualStart.HasValue || recording.ScheduledStart > now.UtcDateTime ||
                score == null || !score.IsFresh(now) || !score.IsFinal ||
                score.EventId != eventId || score.LeagueId != leagueId ||
                score.SourceFetchedAt!.Value.UtcDateTime < recording.ActualStart.Value)
            {
                _confirmations.Remove(recording.Id);
                return false;
            }

            var fetchedAt = score.SourceFetchedAt.Value;
            if (!_confirmations.TryGetValue(recording.Id, out var state) ||
                state.EventId != recording.EventId.Value || state.ActualStart != recording.ActualStart ||
                state.RequestOrigin != score.RequestOrigin ||
                state.ExternalId != eventId || state.LeagueId != leagueId ||
                now - state.LastSeen > TimeSpan.FromSeconds(120) || fetchedAt < state.LastFetch)
            {
                _confirmations[recording.Id] = new(recording.EventId.Value, recording.ActualStart.Value,
                    eventId, score.RequestOrigin, leagueId, now, fetchedAt, fetchedAt, now);
                return false;
            }

            _confirmations[recording.Id] = state with { LastFetch = fetchedAt, LastSeen = now };
            return fetchedAt - state.FirstFetch >= TimeSpan.FromSeconds(60) &&
                now - state.FirstSeen >= TimeSpan.FromMinutes(Math.Clamp(bufferMinutes, 0, 60));
        }
    }

    public void Forget(int recordingId)
    {
        lock (_gate) _confirmations.Remove(recordingId);
    }

    public void RetainOnly(IEnumerable<int> recordingIds)
    {
        var active = recordingIds.ToHashSet();
        lock (_gate)
        {
            foreach (var id in _confirmations.Keys.Where(id => !active.Contains(id)).ToArray())
                _confirmations.Remove(id);
        }
    }
}
