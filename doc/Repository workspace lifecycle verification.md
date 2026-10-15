# Repository workspace lifecycle verification

Updated: 2026-10-15. Scope: the single-workspace lifecycle in `main`, with the
existing `RepositorySessionLifetime`, `RepositoryRefreshFlight` and mutation gate.

## Findings and changes

| Area | Reproduced issue or risk | Production change | Evidence |
| --- | --- | --- | --- |
| Single-flight completion | `completed` executed under the coordinator lock while `_active` still referenced the previous task. A synchronously reentrant request could be lost. | Detach the finished task while holding the lock; run callbacks outside it. Failures always settle the original awaiters. | `RepositoryRefreshFlightTests.CompletedCallbackCanReenterWithoutLosingNewFlight`, `CompletedHandlerFailureDoesNotStrandAwaitersOrNextFlight` |
| Full/local coalescing | The first `localOnly` closure applied to all follow-up passes and could downgrade a later full refresh. | Combine full/local with sticky full requirement in the existing production flight. | `CoalescesFullAndHistoryRequirementsFromLaterRequests`, `LocalRequestCannotDowngradeAnActiveFullRefresh` |
| Watcher delivery | Value equality between different `Repository` records could skip watcher restart; a queued event could arrive after changing workspaces. | Restart on reference change and fence dispatcher callbacks by owner reference and monitor delivery epoch. | Source-level review and real desktop switching test; queued A/B/A event race remains a dedicated manual/extended automation target |
| Failed open / rollback | On a failed initial snapshot the previous workspace must be restored; errors in the restoration must not hide the first failure. | Restore the old instance, preserve the original error, report both failures and retain existing draft semantics. | Real desktop `RunLifecycleCheckAsync`: rejected path preserves the previous workspace/draft |
| Git side effects | An already completed mutation and failed follow-up refresh had the same generic user-facing error. | Explicitly classify successful mutation with failed presentation refresh and advise refresh rather than blindly rerun. | Source review and existing mutation-level tests; more injected-failure UI cases are needed |
| History CTS | An observer could invalidate a request before its async read accessed `CancellationTokenSource.Token`; loading could stay latched. | Cache the token before exposing the CTS, and set loading state before notifying busy observers. | `HistoryViewModelTests.InvalidationDuringBusyCallbackDoesNotAccessDisposedTokenOrRetainLoading` |
| Desktop closing | Closing through `Window.Close()` during Win32 asynchronous shutdown could leave the native message loop running. macOS termination could bypass the managed host shutdown report. | Final Win32 close uses authorized `WM_CLOSE`. On macOS, stop managed host from the closed-window callback after approved shutdown. | `DesktopApplicationTests` normal close and lifecycle protocol; see actual CI results |
| Desktop absent | xUnit v2 treated a raised dynamic skip exception as a failure. | `SkippableFact` uses a real SKIP result for unsupported runtime. | Ubuntu Desktop.Tests TRX reports one SKIP while headless tests execute |

The observable intent in `.idd/intent/IDD-0039.spec-repository-switching.md`
and `.idd/intent/IDD-0018.spec-manual-refresh-and-external-change-indicator.md`
remains unchanged. No extra workspace model, mutation semaphore, or refresh coordinator
was introduced.

## Production resource ownership

| Resource | Owner | End / cancel / dispose contract |
| --- | --- | --- |
| Session CTS and reader count | `RepositorySessionLifetime` | `End` disallows new readers, cancels on session end; source is disposed after the last `Release`. Repeated `End` is harmless. |
| State-flight task / pending requests | `RepositoryRefreshFlight` owned by `OpenRepositoryViewModel` session | Each caller shares the active task; pending requirements coalesce. Completion callbacks do not execute under its lock. New session gets a different flight. |
| Git mutations and repository switching | `OpenRepositoryViewModel._mutationGate` | Switch and side-effecting operations reject conflicting calls rather than queueing. The gate is released in `finally`. |
| History query CTS | `HistoryViewModel` | On new request, invalidation and dispose, cancel previous CTS; the already-extracted token remains safe to hand to async service after CTS disposal. Generation and repository reference gate results. |
| Working-tree status probe linked CTS | `MainPage.RepositoryRefresh` | Restarted when monitored repository reference changes; shutdown cancels and disposes; background completion checks repository reference and baseline revision. |
| Watchers, debounce timer and recent invalidations | `RepositoryChangeMonitor` | Stop and restart invalidate old watcher epochs; `Dispose` disposes watchers/timer and rejects new starts. |
| UI monitor queued callback | `MainPage.RepositoryRefresh` | Callback carries owner reference and delivery epoch, checks both before touching the active workspace; shutdown increments epoch and unsubscribes. |
| MainPage subscriptions and child view models | `MainPage.BeginShutdown` / `OpenRepositoryViewModel.Dispose` | Idempotent entry; detach observers, cancel monitoring, dispose child state and reject late publication. |
| Window/host lifecycle | `App` and `Program` | Protected operations forbid normal closure; asynchronous finalization is idempotent, host stop is guarded against repeated invocation. |

## Automated coverage

- Headless real Core tests: `RepositoryRefreshFlightTests`,
  `RepositorySessionLifetimeTests`, `HistoryViewModelTests`,
  `RepositoryChangeMonitorTests`, `RepositoryRefreshLifecycleStateTests`.
  These run on `net10.0` without a GUI, with `TaskCompletionSource`
  to control read ordering.
- `DesktopApplicationTests.LaunchesRealApplicationForRepositoryAndLinkedWorktree`
  invokes an actual Uno desktop process and production MainPage/VM. Besides
  the existing repository/worktree load and normal close checks, the extended
  named-pipe protocol checks A→B, failed open with retained draft, B→A,
  protected mutation/close rejection, close repository, reopen, and final close.
  The UI test requires a working graphical session.
- `RepositorySwitchingContractTests`, `PresentationLifecycleContractTests`
  and other source contracts complement runtime tests but do not prove
  multithreaded race safety.

### Platform evidence and limits

The workflow `.github/workflows/build.yml` runs Release restore/build/test
on `ubuntu-latest`, `windows-latest` and `macos-latest`.
A CI run is only a successful *desktop* smoke if the named-pipe UI protocol
finishes and asserts the lifecycle milestones; a TRX SKIP is **not** a PASS.

At the first evaluation of this hardening, Ubuntu completed the headless
Core test classes and reported desktop smoke as SKIP
([CI run 38725087498](https://github.com/DimonSmart/CSharpGit/actions/runs/38725087498)).
On a later version, an added history invalidation test exposed a stuck
loading flag; the implementation was corrected subsequently.
The final CI result for the newest commit must be verified independently;
earlier green runs do not establish success for later commits.

| Test layer / environment | Linux headless | Windows GUI CI | macOS GUI CI |
| --- | --- | --- | --- |
| Core unit/integration and real Git tests | PASS on the pre-history-fix run; final revision requires retest | NOT RUN/awaiting latest run | NOT RUN/awaiting latest run |
| Single-flight callback and full/local regression | PASS on earlier Ubuntu CI | NOT RUN/awaiting latest run | NOT RUN/awaiting latest run |
| History invalidation regression | FAIL before latest fix, latest NOT RUN | NOT RUN | NOT RUN |
| Real UI A→B→A, open error, protected close | SKIP: no supported compositor | NOT RUN/awaiting latest run | NOT RUN/awaiting latest run |

All statuses describe observed outcomes at the time of writing, not
predictions of a running workflow.

## Residual gaps / manual verification

1. Force a real UI dispatcher callback for A to be delivered *after* B and
   then after a new A instance. Check indicator, watcher ownership and baseline.
   The owner/epoch fence is present, but an automated queued-dispatcher
   end-to-end race has not been demonstrated.
2. Inject failure into the initial `IRepositoryStateService` snapshot after B
   was assigned; also fail A rollback refresh, confirm both errors are shown
   and A's draft is retained. A missing folder is covered by real UI smoke,
   but not every injection boundary.
3. Test a sustained mutation with a real Git side effect (commit/maintenance),
   failed post-mutation refresh, and a second user command. Synthetic held
   mutation currently proves window-close mutual exclusion.
4. Validate cancellation during history load and after a queued UI callback
   with a real desktop dispatcher, not only Core tests.
5. Repeat desktop smoke in a valid macOS session and Windows graphics session.
   If an environment cannot launch Uno normally, report SKIP/FAIL with reason
   and do not replace it with a successful headless assertion.

To run manually:
```sh
dotnet restore CSharpGit.slnx
dotnet build CSharpGit.slnx --configuration Release
dotnet test CSharpGit.slnx --configuration Release --logger "trx;LogFilePrefix=lifecycle"
```

A complete LIFE-01..10 sign-off requires all remaining real VM/desktop
failure-injection and watcher races to be demonstrated; the current code
and tests should not be described as full proof until they are.
