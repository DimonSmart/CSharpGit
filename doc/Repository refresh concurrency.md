# Repository refresh concurrency and lifecycle

## Single-flight state refresh

Each repository session owns one production `RepositoryRefreshFlight` in
`CSharpGit.Presentation.Core`. All repository state refresh entry points in
`OpenRepositoryViewModel` use that same flight. At most one
`IRepositoryStateService.ReadWithWorkingTreeStatusAsync` pass runs per session.
Additional requests are coalesced into one pending pass, not an unbounded queue.
`includeHistory` is sticky: if any request requires history, the newest pass
also refreshes it. A full refresh is stronger than `localOnly`; a pending
full request upgrades the next pass, and a local-only request cannot downgrade
an already-full flight.

A request arriving while an older pass is still reading suppresses that older
pass's publication. Only the newest successful pass may apply the related HEAD,
branches, remotes, tags, stashes, operation, working tree and baseline. All
these assignments run as one synchronous callback on `IUiDispatcher`, without
interleaved awaits. History refresh may follow independently, after publication.

## Await, error and cancellation

- All overlapping callers await the same active flight's completion. A caller
  cancellation ends that caller's `WaitAsync` but does not cancel the refresh
  requested by other callers or remove the coalesced pending request.
- The repository session's cancellation token owns cancellation of Git reads.
  A repository switch, close, or application dispose ends the session and
  prevents publication of its late results.
- If a pass fails but a newer request arrived, the flight continues with the
  newest request. If the newest pass fails without a replacement, the flight
  completes with an error. Failed passes never publish a baseline.
- One `RepositoryStateRefreshStarting` and one
  `RepositoryStateRefreshCompleted` surround the entire flight, including
  any coalesced reruns. `MainPage` keeps its file-invalidation window open
  until the final completion rather than closing it at baseline publication.
- Refreshes invoked by a Git mutation execute while the existing mutation
  gate is already held; the refresh coordinator never reacquires that gate.

File watching continues to **detect** external invalidations only. It does
not trigger an automatic full repository refresh. `Refresh Required` remains
latched until a successful new baseline; changes arriving during refresh are
examined when the window finishes.

## Production classes and coverage

| Invariant | Production owner | Headless test |
| --- | --- | --- |
| Single active pass, coalescing, history flag | `RepositoryRefreshFlight` | `RepositoryRefreshFlightTests.ConcurrentRequestsRunOneReadAtATimeAndPublishOnlyLatest` |
| Failure does not end a newer request | `RepositoryRefreshFlight` | `RepositoryRefreshFlightTests.NewRequestAfterFailedReadCanStillPublish` |
| Publication gate rejects an outdated pass | `RepositoryRefreshFlight` | `RepositoryRefreshFlightTests.ConcurrentRequestsRunOneReadAtATimeAndPublishOnlyLatest` |
| Session switch prevents obsolete completion | `RepositorySessionLifetime` | `RepositorySessionLifetimeTests.ReopeningSameRepositoryRejectsEarlierGenerationPublication` |
| Sticky invalidation until new baseline | `RepositoryRefreshLifecycleState` | `RepositoryRefreshLifecycleStateTests` |
| Synchronous, session-fenced application to Uno fields | `OpenRepositoryViewModel` | Source integration contract plus desktop smoke; **UI-level race behavior still needs a real desktop smoke check** |
| Normal window close during protected operations | `MainPage.IsUnsafeToClose`, `App.xaml.cs` | Source integration contracts; **real desktop close checks still pending** |

The Uno adapter owns only mapping a successfully read repository state to view
models, the dispatcher callback and the existing mutation gate. It does not
implement another concurrent refresh queue. This design does not introduce
an additional mutation lock or automatic watcher-triggered full refresh.

## Lifecycle integration hardening (2026-10-15)

- `RepositoryRefreshFlight` detaches the finished task under its lock before invoking `completed` outside the lock. Callback reentry starts a new flight; subscriber failures fault only the old flight without stranding awaiters. Publication callbacks also execute outside the lock; a request arriving during a synchronous apply receives a subsequent pass.
- `OpenRepositoryViewModel` passes both history and full/local requirements into the production flight. It restores the previous repository on failed initial publication and reports any additional rollback-refresh error. A completed Git mutation is distinguished from its failed follow-up presentation refresh.
- `MainPage` tracks queued watcher deliveries by monitor restart epoch and reference identity, rejecting notifications emitted by an older watcher even when a repository is reopened at the same path. Value equality does not stand in for session identity.
- Headless tests cover the coalescing strength, reentrant callbacks, and subscriber failure. Desktop tests without a graphical session report SKIP, not PASS. Runtime UI race coverage requires an actual desktop test host.

The desktop test uses `Xunit.SkippableFact` because xUnit v2's standard `[Fact]` does not interpret dynamically raised skip exceptions as skipped test results.
