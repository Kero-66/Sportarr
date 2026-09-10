using System.Diagnostics;
using System.Reflection;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Sportarr.Api.Models;
using Sportarr.Api.Services;
using Xunit.Abstractions;

namespace Sportarr.Api.Tests.Services;

[CollectionDefinition("RSS matching measurements", DisableParallelization = true)]
public class RssMatchingMeasurementCollection;

[Collection("RSS matching measurements")]
public class RssMatchingBenchmarkTests(ITestOutputHelper output)
{
    private delegate Event? FindMatch(ReleaseSearchResult release, List<Event> events,
        ReleaseMatchingService matcher, bool multiPart, IReadOnlyDictionary<int, int?> earlyLimits);

    [Fact]
    public void MixedFeed_SelectsExpectedEvents_OnFirstAndRepeatPass()
    {
        var fullSize = Environment.GetEnvironmentVariable("SPORTARR_RSS_BENCHMARK") == "1";
        var events = CreateEvents();
        var releases = CreateReleases(fullSize ? 900 : 20);
        using var services = new ServiceCollection().BuildServiceProvider();
        using var rss = new RssSyncService(services, NullLogger<RssSyncService>.Instance);
        var matcher = new ReleaseMatchingService(NullLogger<ReleaseMatchingService>.Instance,
            new SportsFileNameParser(NullLogger<SportsFileNameParser>.Instance),
            new EventPartDetector(NullLogger<EventPartDetector>.Instance));
        var findMatch = typeof(RssSyncService).GetMethod("FindMatchingEvent",
            BindingFlags.Instance | BindingFlags.NonPublic)!.CreateDelegate<FindMatch>(rss);
        var earlyLimits = new Dictionary<int, int?>();

        output.WriteLine($"Runtime: {System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription}; " +
            $"OS: {System.Runtime.InteropServices.RuntimeInformation.OSDescription}; " +
            $"architecture: {System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture}");
        output.WriteLine($"Releases: {releases.Count}; events: {events.Count}; pairs/pass: {releases.Count * events.Count}");

        var first = Measure("first", findMatch, releases, events, matcher, earlyLimits);
        var repeat = Measure("repeat", findMatch, releases, events, matcher, earlyLimits);

        first.Should().Equal(releases.Select(release => release.ExpectedId));
        repeat.Should().Equal(first);
    }

    private int?[] Measure(string pass, FindMatch findMatch,
        List<(ReleaseSearchResult Release, int? ExpectedId)> releases, List<Event> events,
        ReleaseMatchingService matcher, IReadOnlyDictionary<int, int?> earlyLimits)
    {
        using var process = Process.GetCurrentProcess();
        var results = new int?[releases.Count];
        var allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
        var cpuBefore = process.TotalProcessorTime;
        var timer = Stopwatch.StartNew();
        for (var index = 0; index < releases.Count; index++)
        {
            results[index] = findMatch(releases[index].Release, events, matcher, true, earlyLimits)?.Id;
        }
        timer.Stop();
        var allocated = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;
        var cpu = process.TotalProcessorTime - cpuBefore;
        output.WriteLine($"{pass}: elapsed={timer.Elapsed.TotalMilliseconds:F1} ms; " +
            $"process CPU={cpu.TotalMilliseconds:F1} ms; thread allocations={allocated:N0} bytes; " +
            $"matches={results.Count(result => result.HasValue)}");
        return results;
    }

    private static List<Event> CreateEvents()
    {
        var venues = new[]
        {
            ("China", "Chinese"), ("Canada", "Canadian"), ("Bahrain", "Bahrain"),
            ("Australia", "Australian"), ("Japan", "Japanese"), ("Miami", "Miami"),
            ("Monaco", "Monaco"), ("Spain", "Spanish"), ("Austria", "Austrian"),
            ("Britain", "British"), ("Hungary", "Hungarian"), ("Belgium", "Belgian"),
            ("Netherlands", "Dutch"), ("Italy", "Italian"), ("Azerbaijan", "Azerbaijan"),
            ("Singapore", "Singapore"), ("Mexico", "Mexican"), ("Brazil", "Brazilian"),
            ("Qatar", "Qatar"), ("Abu Dhabi", "Abu Dhabi"), ("Las Vegas", "Las Vegas"),
            ("Saudi Arabia", "Saudi Arabian"), ("United States", "United States")
        };
        var league = new League { Id = 1, Name = "Formula 1", Sport = "Motorsport" };
        var events = new List<Event>();
        foreach (var (location, adjective) in venues)
        {
            foreach (var session in new[] { "Practice 1", "Qualifying", "Race" })
            {
                events.Add(new Event
                {
                    Id = events.Count + 1, Title = $"{adjective} Grand Prix - {session}",
                    Sport = "Motorsport", League = league, LeagueId = league.Id,
                    Location = location, Season = "2024", Monitored = true,
                    EventDate = new DateTime(2024, 3, 1, 12, 0, 0, DateTimeKind.Utc).AddDays(events.Count)
                });
            }
        }
        foreach (var session in new[] { "Practice 2", "Practice 3" })
        {
            events.Add(new Event
            {
                Id = events.Count + 1, Title = $"Bahrain Grand Prix - {session}",
                Sport = "Motorsport", League = league, LeagueId = league.Id,
                Location = "Bahrain", Season = "2024", Monitored = true,
                EventDate = new DateTime(2024, 3, 2, 12, 0, 0, DateTimeKind.Utc)
            });
        }
        return events;
    }

    private static List<(ReleaseSearchResult Release, int? ExpectedId)> CreateReleases(int count)
    {
        var samples = new (string Title, int? ExpectedId)[]
        {
            ("Formula1.2024.China.Grand.Prix.Qualifying.1080p.WEB.h264", 2),
            ("Formula1.2024.Canada.Grand.Prix.Race.2160p.WEB.h265", 6),
            ("NBA.2024.03.02.Lakers.vs.Celtics.1080p.WEB.h264", null),
            ("NHL.2024.03.02.Bruins.vs.Canadiens.720p.WEB.h264", null),
            ("UFC.299.Main.Card.1080p.WEB.h264", null),
            ("MotoGP.2024.Qatar.Race.1080p.WEB.h264", null),
            ("The.Example.Show.S02E03.1080p.WEB.h264", null),
            ("Example.Movie.2024.1080p.BluRay.x264", null),
            ("Formula1.2023.China.Grand.Prix.Qualifying.1080p.WEB.h264", null),
            ("Formula1.2024.Canada.Grand.Prix.Race.720p.WEB.h264", 6)
        };
        return Enumerable.Range(0, count).Select(index =>
        {
            var sample = samples[index % samples.Length];
            return (new ReleaseSearchResult
            {
                Title = $"{sample.Title}-BENCH{index:D4}", Guid = $"benchmark-{index}",
                DownloadUrl = $"https://example.invalid/releases/{index}", Indexer = "Synthetic"
            }, sample.ExpectedId);
        }).ToList();
    }
}