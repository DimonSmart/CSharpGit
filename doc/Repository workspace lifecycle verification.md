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

Observed platform evidence on commit `5af204e`
([CI run 38725442714](https://github.com/DimonSmart/CSharpGit/actions/runs/38725442714)):
Ubuntu and macOS jobs passed, including the history invalidation regression
and a real macOS UI lifecycle smoke. Windows ran the real desktop lifecycle
test successfully, but the job failed on a platform-sensitive diagnostic
source-contract assertion (newline formatting).
That separate source-contract check was subsequently made newline-independent
in `bb48fa4`. Commit `38a3802`
([CI run 38725929841](https://github.com/DimonSmart/CSharpGit/actions/runs/38725929841))
also completed Ubuntu and macOS jobs successfully, including the extended
mutation contention protocol. Windows again executed the desktop smoke but
retained the preexisting source-contract assertion issue.

| Test layer / environment | Linux headless | Windows GUI CI | macOS GUI CI |
| --- | --- | --- | --- |
| Real Core and Git tests on `38a3802` | PASS | PASS for behavioral tests; one unrelated source-contract FAIL | PASS |
| Single-flight callback and full/local regressions on `38a3802` | PASS | PASS | PASS |
| History invalidation regression on `38a3802` | PASS | PASS | PASS |
| Real UI A→B→A, open error, protected close, mutation-gate retries on `38a3802` | SKIP: no supported compositor | PASS | PASS |
| Added delayed watcher A→B→A delivery check on `4037113` | NOT RUN: current run pending | NOT RUN: current run pending | NOT RUN: current run pending |

A later run cannot be inferred from an earlier successful job.
The newest change has its own
[CI run 38726705090](https://github.com/DimonSmart/CSharpGit/actions/runs/38726705090);
check its conclusion and TRX before declaring a final acceptance.

## Finalization pass: executable scenarios (2026-10-15)

The desktop lifecycle protocol is executed by
`DesktopApplicationTests.LaunchesRealApplicationForRepositoryAndLinkedWorktree`
inside the actual production Uno process. It uses an isolated temporary Git
repository and linked worktree. Its snapshot reply includes the real
`OpenRepositoryViewModel` fields, child lists, baseline, busy and close
availability, error message and commit draft.

| Requirement | Behavior exercised | Evidence |
| --- | --- | --- |
| LIFE-01 | Initial A/worktree load, B→A, reopening same path, coherent HEAD/history/baseline | `RunCheckAsync`, `RunLifecycleCheckAsync` |
| LIFE-02 | Invalid folder, nonexistent target, cancellation before and after assigning B; actual `git status` failure after assigning B, failing B publication, and failing A recovery | `ReadFailureAfterAssignment`, `CancelOpenBeforeAssignment`, `CancelOpenAfterAssignment`, `FailNextPublication` desktop commands |
| LIFE-02 commit drafts | Failed confirmed switch retains draft; successful confirmed switch and close discard it only after success | `ConfirmedSwitch`, `ConfirmedClose` through the real `MainPage` core |
| LIFE-03 | One flight, sticky history/full read, reentrant publication/completion, startup handler exceptions, canceled independent waiter | `RepositoryRefreshFlightTests` |
| LIFE-04 | Real monitor callback captured from A then delivered after B and new A; the refresh-required indicator stays clear | `CaptureMonitorDelivery`, `DeliverCapturedMonitor` |
| LIFE-05 | Running mutation rejects switch, close and competing mutation; failure/cancellation and callback faults return gate to idle | `BeginMutation`, `AttemptSwitchDuringMutation`, `FailMutation`, `FailBeforeMutation`, `FailAfterSuccessfulMutation`, `CancelMutation`, `RetryMutation` |
| LIFE-05/09 | Actual `git commit --allow-empty` executes once, after which refresh callback fails; error identifies completed Git operation, manual refresh exposes changed HEAD | `CommitWithFailedRefresh` and independent `git rev-list --count HEAD` verification |
| LIFE-06 | Cancelable real normal window close, protected mutation close rejection; approved close reaches host shutdown | `RunNormalCloseCheckAsync`, extended desktop lifecycle |
| LIFE-07 | Session disposal, delayed old completions, history cancellation and stale watcher callback fences | `RepositorySessionLifetimeTests`, `HistoryViewModelTests`, `RepositoryChangeMonitorTests`, desktop callback tests |
| LIFE-08/10 | Actual Core and Uno production components; no copied refresh/switch/mutation algorithm | source/behavior tests and CI matrix |

**Fault injection boundaries.** The mandatory Git-state-read failure is
reproduced after the new repository has been assigned by temporarily setting
`GIT_DIR` to a nonexistent directory inside the test process. Its original
value is restored before the rollback read and again in a `finally` block.
The Git worktree's `.git` metadata is not modified. The callback failure
happens in an actual `OpenRepositoryViewModel.PropertyChanged` publication
event, and the two failures from B and A are asserted separately. Failure of
both reads/publications clears the inconsistent B presentation while keeping
A selected and retaining the draft.

The confirmed-choice helper in `MainPage.RepositorySwitching` executes the
same production post-dialog switch/close core. The dialog itself is not
automatically clicked; this test validates the effect *after* the user has
already accepted discarding a draft, not dialog accessibility.

### Verified and pending platform outcomes

- [CI run 38729054250](https://github.com/DimonSmart/CSharpGit/actions/runs/38729054250):
  Linux headless PASS with graphical smoke SKIP; macOS complete PASS including
  real Git fault injection, rollback, commit and lifecycle close.
  Windows executed the UI test but disconnected in the test harness when the
  earlier injection modified a linked worktree's `.git` file. The production
  reader logic was not identified as the cause.
- [Commit 8d580736](https://github.com/DimonSmart/CSharpGit/commit/8d580736435cdc79128922598b1ce2018bb2b2c7)
  replaced that file modification with reversible `GIT_DIR` process
  environment injection. Its own
  [CI run 38730356561](https://github.com/DimonSmart/CSharpGit/actions/runs/38730356561)
  must be checked independently before claiming all three platforms pass.
- Later confirmed switch/close and pre-/post-mutation hooks are covered by
  their own follow-up runs; outcomes must be tied to the *tested commit*,
  not inferred from an earlier success.

### Remaining platform-dependent verification

Desktop smoke now executes real Git Clone/Create success and failure paths,
checking the production close guard during the busy transition and after exit.
Long-running History Rewrite/Maintenance/Update and native dialog interactions
are still platform-dependent manual acceptance scenarios. Their respective production
`IsBusy`/in-progress flags are included in `MainPage.IsUnsafeToClose`,
but structural inspection of those flags is not accepted as runtime proof
by LIFE-06. For each of these workflows, perform the following on a
temporary repository in a graphical Windows or macOS session:

1. Start the real operation, using a deliberately slow rebase,
   maintenance job, or staged application update on a disposable repository.
2. While the operation is active, request normal title-bar close and a switch
   to another repository. Confirm both are rejected where required, without
   terminating the Git process or losing commit drafts.
3. Complete or cancel the operation and verify both normal close and a fresh
   valid operation work again. Review app logs for unobserved exceptions.
4. Re-run for an error path (failed rewrite/maintenance or rejected update).
   Clone/Create success and failure are covered by desktop smoke.
   Do not use a real user repository.

The remaining History Rewrite/Maintenance/Update and native-dialog manual
verification has **not** been performed through the available desktop environment. It is a separately recorded acceptance gap, not
an implicitly passing test. Real dispatcher-queue rejection at shutdown is
also not fault-injected directly; the implemented callback exception path
and stale-session fences are tested instead.

To run the automated tests locally:

```sh
dotnet restore CSharpGit.slnx
dotnet build CSharpGit.slnx --configuration Release
dotnet test CSharpGit.slnx --configuration Release --no-build --logger "trx;LogFilePrefix=lifecycle"
```

**Sign-off rule:** LIFE-01..10 may be described as fully accepted only
after the newest CI run succeeds for all supported platforms and the
unautomated protected-operation scenarios above are either executed and
recorded or explicitly accepted as residual risk. Never count SKIP as PASS.
