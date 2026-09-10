# RSS Matching Benchmark and Regex Reuse

`RssMatchingBenchmarkTests` calls the existing private `RssSyncService.FindMatchingEvent`
method through a bound delegate. It exercises title parsing, per-event validation,
and best-event selection without changing production visibility or implementing a
second matching loop. A selector rename/signature change requires updating the binding.

## Run

From the repository root, run the small correctness case (20 releases, 71 events):

```sh
dotnet test tests/Sportarr.Api.Tests/Sportarr.Api.Tests.csproj -c Release -p:SkipFrontendBuild=true --filter FullyQualifiedName~RssMatchingBenchmarkTests --logger 'console;verbosity=detailed'
```

For the full 900-release, 71-event baseline on macOS/Linux:

```sh
SPORTARR_RSS_BENCHMARK=1 dotnet test tests/Sportarr.Api.Tests/Sportarr.Api.Tests.csproj -c Release -p:SkipFrontendBuild=true --filter FullyQualifiedName~RssMatchingBenchmarkTests --logger 'console;verbosity=detailed'
```

On PowerShell, set `$env:SPORTARR_RSS_BENCHMARK = '1'` before the same `dotnet test`
command. Remove that variable afterward to restore the smaller default workload.
Run in a fresh test process for comparisons, on the same hardware/build/runtime.
Use the narrow filter above; the collection disables concurrent test collections.

## Workload and Assertions

- 71 synthetic monitored F1 session events, using fixed historical dates (not a real calendar).
- 900 unique release titles in full mode, using ten repeating naming patterns with distinct groups.
- 30% expected F1 matches, including country/demonym naming and quality variants.
- 70% unrelated sport/TV/movie or wrong-year releases with no expected event.
- 63,900 release/event comparisons per full pass; both passes assert all expected event IDs.
- First and repeat passes share existing parser/normalization caches. "First" is not a
  guaranteed cold-cache measurement if other matching tests ran in the process.
- No timing threshold: the test gates correctness, while emitted measurements support
  before/after comparisons without flaky machine-dependent assertions.

## Initial Measurement

Measured on macOS x64, .NET SDK 8.0.424/runtime 8.0.30, Release configuration,
against upstream main `3cfbf7077`. One run, not a statistical benchmark:

| Pass | Wall Time | Process CPU Time | Thread Allocated Bytes | Matches |
| --- | ---: | ---: | ---: | ---: |
| First | 62.451 s | 62.085 s | 28,332,635,688 | 270 |
| Repeat | 57.991 s | 56.140 s | 28,279,910,752 | 270 |

Allocated bytes are cumulative allocations on the synchronous matching thread,
not retained heap size or peak memory. Process CPU includes runtime/GC work and
any other activity in the test process. No forced garbage collection is performed.

This is a selector workload, not the entire RSS sync: it excludes HTTP/feed parsing,
database candidate queries, publication-age filtering, quality/upgrade decisions,
pending releases, download submission, and production logging. The 900-by-71 shape
matches the investigated workload size; the synthetic titles are not a production
feed replay. These numbers alone do not establish individual method hotspots or
predict ARM VPS timings.

## Root Cause and Fix

An observed RSS pass processed about 900 releases against 71 events in 128 seconds,
with Sportarr peaking near 208% process CPU on a four-core host. This motivated the
synthetic workload; it does not prove that all production CPU had the same cause.

Managed profiling (`dotnet-trace` 8.0.547301, CPU sampling plus CLR allocation ticks,
analyzed with TraceEvent 3.1.21) attributed 80.8% of CPU samples and 92.7% of sampled
allocation bytes to three motorsport session methods in `EventPartDetector`.
Stacks showed repeated regex parsing/construction. Those methods cycle through
more fixed patterns than the framework's default 15-entry static regex cache holds.
Attribution used the nearest Sportarr service frame; sampled allocation estimates
are not exact per-method counters, and inclusive CPU percentages are not additive.

The fix reuses regex instances in a private concurrent cache keyed by pattern,
options and culture. Only the three motorsport methods change. Pattern text,
matching order, options and fallback behavior stay the same; the global framework
cache is untouched. Keys come from internal literals/tables, never release titles.
This is not a cache of matching decisions or database state.

## Before and After

Same macOS x64 host/runtime, Release build, untraced 900-by-71 workload:

| Metric | Baseline First | Optimized First | Baseline Repeat | Optimized Repeat |
| --- | ---: | ---: | ---: | ---: |
| Wall time | 62.451 s | 12.171 s | 57.991 s | 9.741 s |
| Process CPU | 62.085 s | 12.930 s | 56.140 s | 9.548 s |
| Thread allocated bytes | 28,332,635,688 | 2,067,482,920 | 28,279,910,752 | 2,025,720,032 |
| Expected matches | 270 | 270 | 270 | 270 |

Approximately 5-6x faster and 93% fewer cumulative allocated bytes in these single
measurements, not statistical confidence intervals or a production guarantee.
The follow-up trace reduced filename-session detector allocation attribution from
38.7 GB to 46.6 MB across both passes. Traced timings are not used in the table.

## Regression Evidence

- The independent upstream branch passed all five benchmark/cache tests, including
  en-US, tr-TR and fr-FR culture switches and warm-cache allocation checks after
  unrelated framework-cache churn. Full-workload rerun: first 12.5772s, repeat
  9.1930s, 270 expected matches each; allocations about 2.067/2.026 GB.
- Earlier broad matching/parser/session regressions passed 507 tests. The local
  full suite had 11 macOS filesystem failures, reproduced on the unoptimized base.
  Timing-sensitive failures also occurred during combined/emulated runs; isolated
  reruns passed. No thresholds or tests were disabled in this change.
- A combined fork build containing the import fix and this optimization passed
  [1,912 tests on native ARM64](https://github.com/jasjeetsuri/Sportarr/actions/runs/34064382419)
  plus image startup/restart checks. That is supporting combined-build evidence,
  not a native run of this standalone branch or a same-host speedup comparison.

Run the cache regression with the command above, replacing the filter with
`FullyQualifiedName~MotorsportRegexReuseTests`. No CI, deployment, scheduler,
acquisition-policy, or unrelated parser changes are included.