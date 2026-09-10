using System.Globalization;
using System.Text.RegularExpressions;
using FluentAssertions;
using Sportarr.Api.Services;
using Xunit.Abstractions;

namespace Sportarr.Api.Tests.Services;

[Collection("RSS matching measurements")]
public class MotorsportRegexReuseTests(ITestOutputHelper output)
{
    [Fact]
    public void WarmDetection_DoesNotRebuildPatternsAfterGlobalCacheChurn()
    {
        const string filename = "Unrelated.Series.S01E01.1080p.WEB-GROUP";
        const string eventTitle = "Monaco Grand Prix - Qualifying";
        const string sessionName = "Testing 2 Day 3";
        var originalCacheSize = Regex.CacheSize;

        EventPartDetector.DetectMotorsportSessionFromFilename(filename).Should().BeNull();
        EventPartDetector.DetectMotorsportSessionType(eventTitle, "Formula 1").Should().Be("Qualifying");
        EventPartDetector.NormalizeMotorsportSession(sessionName).Should().Be(sessionName);

        long allocated = 0;
        for (var iteration = 0; iteration < 20; iteration++)
        {
            for (var patternIndex = 0; patternIndex < originalCacheSize + 1; patternIndex++)
                Regex.IsMatch("unrelated", $"^unrelated{patternIndex}$");

            var before = GC.GetAllocatedBytesForCurrentThread();
            var releaseSession = EventPartDetector.DetectMotorsportSessionFromFilename(filename);
            var eventSession = EventPartDetector.DetectMotorsportSessionType(eventTitle, "Formula 1");
            var normalized = EventPartDetector.NormalizeMotorsportSession(sessionName);
            allocated += GC.GetAllocatedBytesForCurrentThread() - before;

            releaseSession.Should().BeNull();
            eventSession.Should().Be("Qualifying");
            normalized.Should().Be(sessionName);
        }

        output.WriteLine($"Warm detector allocation across 20 cache-churn iterations: {allocated:N0} bytes");
        allocated.Should().BeLessThan(128 * 1024,
            "fixed regexes must survive unrelated use of the global regex cache");
        Regex.CacheSize.Should().Be(originalCacheSize);
    }

    [Theory]
    [InlineData("en-US")]
    [InlineData("tr-TR")]
    [InlineData("fr-FR")]
    public void Detection_RemainsStableAcrossCultureChanges(string cultureName)
    {
        var originalCulture = CultureInfo.CurrentCulture;
        try
        {
            foreach (var culture in new[] { "en-US", cultureName, "en-US" })
            {
                CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(culture);
                EventPartDetector.DetectMotorsportSessionFromFilename("Formula1.2024.Monaco.Sprint.Qualifying")
                    .Should().Be("Sprint Qualifying");
                EventPartDetector.DetectMotorsportSessionType("Monaco Grand Prix - Practice 2", "Formula 1")
                    .Should().Be("Practice 2");
                EventPartDetector.NormalizeMotorsportSession("Test Two Day Three")
                    .Should().Be("Testing 2 Day 3");
            }
        }
        finally
        {
            CultureInfo.CurrentCulture = originalCulture;
        }
    }
}