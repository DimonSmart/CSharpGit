# Git integration test performance optimization

## Scope and method

This change optimizes test-only Git setup without replacing real Git CLI integration or changing production services. Baseline was recorded on the original functional tests after enabling TRX reporting (commit 556db01; [run 37741331812](https://github.com/DimonSmart/CSharpGit/actions/runs/37741331812)). The first optimized revision was 3e2e920 ([run 37741718352](https://github.com/DimonSmart/CSharpGit/actions/runs/37741718352)). That revision exposed a Windows-specific newline error in fast-import; the protocol was corrected to LF-only at b7be652.

See [Test performance baseline 2026-10.md](Test%20performance%20baseline%202026-10.md) for source and per-class starting measurements. TRX report script and its GitHub Actions Summary display the 20 slowest test cases, 10 most expensive classes in each assembly, counts, and per-project elapsed versus summed test durations.

## Why setup was expensive

- InteractiveRebaseTests reconstructed the same A → B → C → D → E history in every one of 29 test cases, with roughly 522 Git CLI starts for initial setup alone (static code-derived count).
- GitReferenceNavigationTests prepared 110 real, empty, sequential commits by spawning 110 Git processes.
- StashTests, PublishBranchTests and ForcePushWithLeaseTests repeated init, identity, staging and initial commit. Publish and force-push tests also created bare remotes.
- TestDirectory always traversed files and reset attributes before attempting to delete a directory, and silently abandoned cleanup after exhausting retries.

## Implementation

- New GitRepositoryFixtureFactory creates an immutable, real Git repository once per xUnit IClassFixture. Each test receives an independent recursive filesystem copy, including objects, refs, HEAD, index, config and reflog. No hardlinks and no shared mutable state. Source templates do not contain remote URLs or Git worktree links to the original template directory.
- RebaseHistoryFixture shares the original five-commit history; each test starts with the same A–E commit IDs, but writes only to its own copy. The next generated file index starts at six to prevent filename collisions.
- StashHistoryFixture shares the initial tracked file and commit. The staging, untracked-file and stash-conflict cases still use native Git on their private copies.
- PublishHistoryFixture and ForcePushHistoryFixture share only the local initial history. Every test initializes its own bare remote under its own isolated root and configures its own origin URL; remotes and upstream refs never point at a shared mutable repository.
- GitReferenceNavigationTests uses a single native git fast-import process to produce 110 linked commits with subject commit-0 through commit-109. It explicitly resets the worktree/index, preserves main and creates stale/old pointing at commit-5. The stream uses protocol LF separators on every OS, not platform-specific AppendLine.
- TestGitRunner centralizes test-helper Git execution, argument escaping through ProcessStartInfo.ArgumentList, non-interactive config isolation, concurrent draining of both output streams and a 120-second timeout with process-tree termination. Diagnostic events distinguish template setup from ordinary test-helper Git calls. The instrumentation **does not** include production GitCommandExecutor or direct Process.Start in untouched test helpers.
- TestDirectory now attempts ordinary recursive deletion first, normalizes attributes only after a real error, retries only upon an OS error with bounded backoff, and raises an error if cleanup ultimately fails. Cleanup duration is measured while diagnostic mode is enabled.
- GitTestMeasurements buffers JSONL events, flushing in batches and on testhost exit to reduce filesystem overhead during CI profiling. The optional mode is off by default.

### Isolation invariants

Never mutate a template after creating it. Do not add remote URLs, absolute core.worktree or references to an external directory to templates. Never use hardlinks for Git internals, and do not share a bare remote that receives pushes. Tests that run rebase, stash, reset, checkout, merge, push or worktree modifications must operate on their own disposable copy. Construct additional history inside the individual test, not by modifying a common fixture.

To add a new fixture, create a public xUnit fixture class that calls GitRepositoryFixtureFactory.CreateTemplate once, then GitRepositoryFixtureFactory.CopyTemplate for each test and TestDirectory.Delete from Dispose. Seed it with TestGitRunner; keep setup minimal.

## Local profiling

Install .NET 10, Git, and Python 3. From the repository root (commands below work in PowerShell on all three operating systems):

```powershell
$env:CSHARPGIT_TEST_METRICS = "1"
$env:CSHARPGIT_TEST_METRICS_DIR = Join-Path (Get-Location) "TestDiagnostics"
dotnet restore CSharpGit.slnx -p:Configuration=Release
dotnet build CSharpGit.slnx -c Release --no-restore
dotnet test CSharpGit.slnx -c Release --no-build --logger "trx;LogFilePrefix=test-results"
python .github/scripts/report-test-timings.py --root tests --metrics TestDiagnostics
```

On a Unix shell use CSHARPGIT_TEST_METRICS=1 and CSHARPGIT_TEST_METRICS_DIR=<absolute path> as exported variables, and python3 if python is not installed as an alias. Unset the diagnostic variables for normal runs. A test's duration includes test operations, but is not the same as test-project wall time when tests run concurrently.

## Profiling in GitHub Actions

The Build workflow keeps its original Windows, Ubuntu and macOS matrix and full Release suite. Every run uploads OS- and attempt-qualified test-timings-<os>-attempt-<n> artifacts containing all TRX outputs and any JSONL helper metrics. View **Summary** on the run page for counts, top cases and classes; download the TRX artifacts for the complete case-level data. The reporting and artifact steps run even when tests fail and do not override dotnet test's failure exit code.

Git process counts in JSONL apply **only** to instrumented test helpers; they cannot be treated as a whole-suite process count. Baseline TRX predates helper instrumentation, so actual baseline Git-process count and cleanup time are unavailable. Distinguish source-derived counts from observed numbers.

## Results

| Metric | Before | First optimized run | Notes |
|---|---:|---:|---|
| Git.Tests Ubuntu TRX wall | 15.11 s | 20.52 s | Single runs; Application and Desktop also slowed, indicating runtime variability |
| Git.Tests macOS TRX wall | 405.11 s | 275.44 s | 32.0% shorter |
| Git.Tests Windows TRX wall | 453.91 s | 406.52 s | First optimized run had a Windows fast-import newline failure; **not** a passing result |
| InteractiveRebaseTests macOS, summed cases | 156.97 s | 68.36 s | 56.4% shorter |
| InteractiveRebaseTests Ubuntu, summed cases | 3.20 s | 2.65 s | 17.2% shorter |
| Fixture setup Git processes, first optimized macOS run | not captured | 36 | Partial (instrumented setup only) |
| Fixture clone time, first optimized macOS run | not captured | 3.34 s / 76 copies | Partial |
| Fixture cleanup time, first optimized macOS run | not captured | 5.64 s / 222 calls | Partial |

There are 427 Git.Tests, 424 Desktop.Tests, and 403 Application.Tests in each initial and first optimized run; no scenarios were deleted or skipped. Ubuntu and macOS completed successfully. The first Windows after-run failed only GitReferenceNavigationTests because fast-import was given CRLF instead of LF; corrected in the next revision.

### Remaining hotspots and next steps

Initial Windows class-duration sums: GitToolConfigurationServiceTests 175.0 s, InteractiveRebaseTests 76.17 s, StashTests 32.32 s, PublishBranchTests 30.92 s, GitReferenceNavigationTests 21.52 s and ForcePushWithLeaseTests 20.77 s. On macOS after optimization, InteractiveRebaseTests (68.36 s), GitToolConfigurationServiceTests (46.56 s), WorkingTreeDiscardTests (39.70 s), CommitActionsTests (39.08 s), WorkingTreeDiffServiceTests (38.45 s) and PublishBranchTests (37.56 s) remain among the most expensive.

The Windows <180 s and macOS <120 s goals have not yet been demonstrated. Next focus should be tracing the number of real Git config reads/writes and process lifetimes in GitToolConfigurationServiceTests and the other measured hotspots. Preserve their config precedence and environment-isolation assertions. Before further refactors, collect three sequential runs on comparable runner categories (or disclose fewer) and compare medians and spread, including durations of setup, real service operations and cleanup. Avoid mass conversions or replacing Git with mocks solely to meet time targets.

## Corrected fast-import protocol and subsequent verification

The corrected, buffered-diagnostics revision [b7be652](https://github.com/DimonSmart/CSharpGit/commit/b7be65269db073de3d53354d24018337c323047f) was tested in [run 37742738668](https://github.com/DimonSmart/CSharpGit/actions/runs/37742738668).

| Metric | Baseline | Corrected revision | Relative result |
|---|---:|---:|---:|
| Git.Tests Ubuntu TRX wall | 15.11 s | 20.45 s | 35.3% slower in a single noisy run |
| Git.Tests macOS TRX wall | 405.11 s | 218.30 s | 46.1% shorter |
| Git.Tests Windows TRX wall | 453.91 s | 295.90 s | 34.8% shorter |
| InteractiveRebaseTests macOS case-time sum | 156.97 s | 53.11 s | 66.2% shorter |
| InteractiveRebaseTests Windows case-time sum | 76.17 s | 43.35 s | 43.1% shorter |
| StashTests Windows case-time sum | 32.32 s | 25.40 s | 21.4% shorter |

The Git assembly reports **427 passed, 0 failed, 0 skipped on all three platforms** in this corrected run. Application.Tests also reports 403 passed everywhere. The Windows job is marked unsuccessful due to one failure in an **unmodified** Desktop test: RepositoryChangeMonitorTests.MultipleWorkingTreeEventsAreDebouncedAndPathsAreAccumulated (expected one notification, observed two). This watcher/debounce timing sensitivity is not part of the fixture refactor. A rerun was requested for the Windows job to assess intermittency; see that workflow run's attempts for its outcome.

The corrected macOS run measured 76 template copies (2.78 s total), four template builds (2.56 s total), 36 Git helper processes during template setup (2.38 s total), 365 Git helper processes while running migrated tests (25.35 s total), and 222 cleanup calls (4.32 s total). The corresponding corrected Windows values were 76 copies (3.70 s), four template builds (4.28 s), 36 setup Git processes (4.13 s), 365 other instrumented Git processes (30.10 s), and 222 cleanup calls (12.35 s). These figures do not include GitCommandExecutor invocations by production services.

Neither target of Git.Tests <180 s on Windows or <120 s on macOS has been achieved. The shortfall is now visible and attributable to remaining classes. The first paired observations are not sufficient to establish statistical confidence; repeat comparable runner measurements and report medians and dispersion before drawing a strong conclusion about Ubuntu.
