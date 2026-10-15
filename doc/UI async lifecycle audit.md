# UI asynchronous lifecycle audit

Updated: 2026-10-15. Scope: `DimonSmart/CSharpGit` desktop presentation, main branch.

This document records lifecycle ownership, response to errors, re-entry and publication guards. `App` owns the window close sequence; `MainPage` owns the page and its dialogs; ViewModels remain owners of Git mutation gates. A canceled task is **not** by itself proof that a stale UI result cannot publish.

## Inventory and dispositions

Legend: **fixed** means updated under UI Async Lifecycle Hardening; **retain** means original mechanism was not redesigned; **review** means follow-up platform verification/audit remains.

| File / entry point | Owner / operation | Cancellation / error handling | Re-entry / lifetime rule | Decision |
|---|---|---|---|---|
| `App.xaml.cs` / `OnLaunched` | App / startup | Catch and log startup exception; close partially created window | One launch; host stopped by Program after event loop | Fixed |
| `App.xaml.cs` / AppWindow.Closing | Window / close admission | Never throw into the UI event; protected Git changes veto | Confirm once, bounded stop, then single authorized final close | Fixed |
| `App.xaml.cs` / ConfirmAndCloseAsync | Window / draft confirmation | Observe dialog failures; no cleanup before confirmation | Recheck page identity and unsafe-to-close after await | Fixed |
| `App.xaml.cs` / update restart | Window / restart | Same asynchronous teardown, no duplicate confirmation | Requires app update not busy and same page | Fixed |
| `Platforms/Desktop/Program.cs` / StopHost | Process / host teardown | Separate three-second StopAsync timeout after Uno event loop | StopHost interlocked once; not blocked inside UI loop | Retain |
| `App.DesktopCloseCheck.cs` / close check | Desktop integration / test harness | try/catch; dispatcher failure observed | Close-check lifetime isolated to controlled test | Retain; verify platform |
| `MainPage.xaml.cs` / AddMenuItem | Page / menu actions | UiAsyncOperationRunner; Trace/ILogger; safe error presenter | Reject after shutdown; underlying command's CanExecute governs re-entry | Fixed |
| `MainPage.xaml.cs` / ExecuteCommandAsync | Page / direct AsyncCommand | Observe exceptions from direct ExecuteAsync and log once | Preserve command _isExecuting/CanExecute and VM mutation gate | Fixed |
| `MainPage.xaml.cs` / ShowErrorAsync | Dialog / notifications | Catch dialog failures, including a concurrent dialog | Skip after shutdown and without XamlRoot; nonrecursive logging | Fixed |
| `MainPage.xaml.cs` / XAML async void click handlers | Page / commands | Direct ExecuteCommandAsync path guarded; other callbacks audited per feature | Check source operation lifetime | Review remaining |
| `MainPage.Dialogs.cs` / ShowPageDialogAsync | Page / dialogs | Semaphore wait observes shutdown cancellation, finally releases | One modal at a time per page; no new modal after shutdown | Fixed for migrated call sites |
| `MainPage.ConfirmationDialogs.cs` / empty index | Dialog / mutation confirmation | Error observed through runner; safe cancellation cleanup | Per-type single-flight, capture repo identity | Fixed |
| `MainPage.ConfirmationDialogs.cs` / batch discard | Dialog / mutation confirmation | Same, no duplicated command in finally after successful cleanup | Per-type single-flight, identity check after ShowAsync | Fixed |
| `MainPage.CommitActions.cs` / checkout/reset/cherry-pick/revert/fixup | Page / Git mutations | Async event errors observed via runner; VM owns Git failures | Captured repository and mutation availability checked before execution | Fixed |
| `MainPage.About.cs` / About and update click | Page / network read and updater | Main event boundary runner; update check catches cancellation/errors | About dialog scoped to page; update gate remains in VM | Fixed main handlers |
| `MainPage.About.cs` / Dialog_Opened | Dialog / version lookup | Existing try/catch and token cancellation on close | Result shown only while dialog active | Retain |
| `MainPage.FileOpening.cs` / file action state refresh | Page / read | Observe no-await task; FileActionsViewModel owns state | Current-request check and cancel by VM | Fixed |
| `MainPage.FileOpening.cs` / file menu | Page / file request | Existing catch and error presentation | IsCurrent and selection check before flyout | Retain with shutdown fence |
| `MainPage.RepositoryFiles.cs` / activation and deactivation | Page / repository files reading | Async runner observes errors | Activation revision and selected-tab guard | Fixed |
| `MainPage.RepositoryFiles.cs` / search/open events | Page / content search/file open | Async runner owns error boundary | VM selection/session fences; page lifetime | Fixed |
| `MainPage.RepositoryFilePreview.cs` / LoadRepositoryFilePreviewAsync | Selection / canceled file read | Expected cancellation filtered; unexpected errors logged | PreviewPublicationGate plus repository/commit/path and IsShuttingDown | Fixed |
| `MainPage.ImageDiff.cs` / LoadCommitImageDiffAsync | Selected commit / diff read | Existing CTS, runner logs unexpected errors | Generation/commit/file identity plus IsShuttingDown | Fixed |
| `MainPage.Changes.cs` / image diff initiation | Page / preview | Runner observes launch faults | Selection gate in ImageDiff | Fixed |
| `MainPage.RepositoryRefresh.cs` / status check loop | Repository session / read | Token cancellation and loop try/finally, no UI wait | Pending invalidations, generation and repository guard | Retain (concurrent refresh hardening landed separately) |
| `MainPage.RepositoryRefresh.cs` / watcher dispatcher callbacks | Repository session / notification | Failed enqueue drops stale notification; no awaiting TaskCompletionSource | Delivery epoch + repository identity recheck | Retain |
| `MainPage.RepositoryRefresh.cs` / presentation refresh enqueue | Page / UI-only | Rejected dispatcher enqueue clears pending refresh | Coalesced queue; shutdown check | Retain |
| `MainPage.HistoryPerformanceDiagnostics.cs` / start/stop/timers/settings | Page / diagnostics | Startup errors shown safely; stops observed; only Outcome.Success says saved | Single-flight facade; ten-minute max; no dialog after shutdown | Fixed |
| `Diagnostics/HistoryPerformanceDiagnostics.cs` / session writer | Capture / file write | Writer failure observed and output marked Partial/Failed | First stop reason wins; all same session stop callers await same task | Fixed |
| `Diagnostics/HistoryPerformanceDiagnostics.cs` / capture facade | App / diagnostics ownership | Start excluded while previous stop pending | Same Task for overlapping stops, detached callbacks before wait | Fixed |
| `MainPage.Lifecycle.cs` / PrepareForShutdown + BeginShutdown | Page / two-phase cleanup | Diagnostics Task awaited by App with 3s budget | Single early and final stages, no repeated Dispose | Fixed |
| `MainPage.RepositorySwitching.cs` / click handlers | Repository session / navigation + Git flows | Existing VM validations and dialog steps | IsChangingRepository, session guards | Review remaining async void boundaries |
| `MainPage.CommitActions.cs` / short dialogs | Dialog / optional Git mutation | Serialized confirmation and repo recheck | RunPageUiOperation + VM mutation gate | Fixed |
| `MainPage.BranchDragDrop.cs` / Drop | Page / Git mutation | Existing command result and mutation checks | Underlying branch protections | Review async void boundary |
| `MainPage.HistoryInfiniteScroll.cs` / page load | Repository session / history read | Existing ViewModel async command and page lifecycle | Selection and generation guards | Retain |
| `Controls/AuthorAvatarLifecycleState.cs` / avatar changes | Control / cancelable image requests | Existing cancellation and version token | Stale response ignored | Retain |

## Confirmed defects addressed

1. Unprotected async menu events and direct AsyncCommand.ExecuteAsync exception boundary.
2. Unobserved confirmation tasks and cleanup after the visible flag was released, plus stale-repository mutations after modal confirmation.
3. Concurrent dialogs attempting to present over the same XamlRoot in migrated paths.
4. Blocking writer shutdown in UI callback and duplicate close sequences.
5. Diagnostics race: Start could overlap Stop while old writer remained live; repeated stop did not await the first one; writer and summary failure could still claim saved.
6. Unobserved file preview, file-selection, image-diff and Git commit action event boundaries.

## Intentionally retained mechanics

- `AsyncCommand` remains the command implementation: `ICommand.Execute` catches errors whereas direct `ExecuteAsync` throws to its awaiter.
- Git mutations are not automatically cancelled by a Close request. Existing mutation gate and protected-close checks take precedence.
- The process's `StopHost()` blocking join remains after the Uno event loop exits, not inside an active UI callback.
- Existing repository refresh coalescing/generation, preview publication gate and avatar lifecycle are reused instead of creating a second task scheduler or global CTS.

## Shutdown and error invariants

First normal Close checks protected operations; if necessary, confirms unsaved draft; otherwise prevents the first close event, transitions page to closing and stops accepting new UI work. The UI waits for diagnostics writer/summary for **up to three seconds**, finalizes page exactly once and authorizes a second Close. Timeouts/errors are traced and do not open new dialogs. A timed-out writer may leave partial output. Direct UI command errors are observed by the event boundary and may not be rethrown as async void exceptions. Expected cancellation is silent only when its lifetime guard actually indicates cancellation.

## Validation and outstanding risks

GitHub Actions builds and unit/contract suites provide automated validation. Local dotnet and manual Windows/macOS/Linux desktop test sessions are unavailable to this executor. Record platform-specific CI outcomes from the commit's Actions run, not from unrelated or earlier commits.

Residual work: Several older async-void event handlers and standalone ContentDialogs elsewhere in MainPage partials have not yet been individually converted; they require follow-up failure-injection testing. Close/confirmation behavior needs real Uno desktop validation on each supported platform; a compiling CI run is **not** proof of window close behavior. These gaps must not be reported as accepted without execution evidence.
