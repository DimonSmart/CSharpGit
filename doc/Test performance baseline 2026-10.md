# Test performance baseline — 2026-10-08

## Reproducible starting point

Baseline commit: [556db01](https://github.com/DimonSmart/CSharpGit/commit/556db01d72083f55141d3aa78f45887623f44565), which adds TRX collection and reporting **before** Git fixture optimizations.

GitHub Actions run: [37741331812](https://github.com/DimonSmart/CSharpGit/actions/runs/37741331812). Full TRX files are uploaded as separate per-OS artifacts. The test suite ran in Release / .NET 10, using the same dotnet test CSharpGit.slnx --configuration Release --no-build --logger "trx;LogFilePrefix=test-results" command on each hosted runner.

The historical run [37733411611](https://github.com/DimonSmart/CSharpGit/actions/runs/37733411611) quoted approximately 17/259/407 seconds for Git.Tests (Ubuntu/macOS/Windows). It is **not** substituted for the newly recorded per-test baseline.

## Measured baseline

| Project | Passed | Ubuntu TRX wall | macOS TRX wall | Windows TRX wall |
|---|---:|---:|---:|---:|
| Application.Tests | 403 | 1.84 s | 4.68 s | 3.00 s |
| Desktop.Tests | 424 | 8.05 s | 68.31 s | 66.35 s |
| Git.Tests | 427 | 15.11 s | 405.11 s | 453.91 s |

There were zero failures and zero skipped tests on all three runners. Wall durations are read from each TRX Times element; they are **not** sums of per-test durations or full build/restore durations. The Git.Tests sums of per-test durations were 31.89 s (Ubuntu), 917.15 s (macOS) and 792.50 s (Windows). Parallel execution explains why these totals can exceed wall time.

### Top costly classes

| Class | Ubuntu test duration sum | macOS test duration sum | Windows test duration sum |
|---|---:|---:|---:|
| InteractiveRebaseTests | 3.20 s | 156.97 s | 76.17 s |
| GitToolConfigurationServiceTests | 4.73 s | 72.68 s | 175.00 s |
| StashTests | 1.38 s | 41.46 s | 32.32 s |
| PublishBranchTests | outside top 10 | 32.89 s | 30.92 s |
| ForcePushWithLeaseTests | outside top 10 | 45.72 s | 20.77 s |
| GitReferenceNavigationTests | 0.677 s (individual test) | see TRX | 21.52 s (individual test) |

“Outside top 10” means the baseline summary did not publish an exact class total, **not** zero. For complete per-test measurements, download the corresponding TRX artifact.

### Instrumentation limitations

The baseline workflow measured all individual test durations using TRX but had not yet introduced the shared TestGitRunner / GitTestMeasurements test-helper instrumentation. Accordingly, *actual baseline Git process counts, fixture copy/setup times and cleanup times were not recorded*. The static source review counted roughly 522 base-history setup Git invocations in the original 29-case InteractiveRebaseTests and 110 separate git commit calls in GitReferenceNavigationTests. These are source-based estimates, not runtime measurements, and must not be presented as observed process counts.

This baseline consists of one run per OS. Hosted-runner load, filesystem performance and scheduling vary considerably; comparisons are indicative and not three-run medians. In particular the Ubuntu report should not be used in isolation to infer a reliable cross-run performance difference.
