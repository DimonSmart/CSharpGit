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
| `MainPage.xaml.cs` / XAML async void click handlers | Page / commands | Direct ExecuteCommandAsync path guarded; feature-specific callbacks use own fences | Remaining direct XAML event handlers still require error injection | Review remaining |
| `MainPage.Dialogs.cs` / ShowPageDialogAsync | Page / dialogs | UiPageDialogGate queues/cancels by page token and propagates failures | One modal at a time; late accepted result invalidated on shutdown | Fixed with behavioral tests |
| `MainPage.ConfirmationDialogs.cs` / empty index | Dialog / mutation confirmation | Error observed through runner; safe cancellation cleanup | Per-type single-flight, capture repo identity | Fixed |
| `MainPage.ConfirmationDialogs.cs` / batch discard | Dialog / mutation confirmation | Same, no duplicated command in finally after successful cleanup | Per-type single-flight, identity check after ShowAsync | Fixed |
| `MainPage.CommitActions.cs` / checkout/reset/cherry-pick/revert/fixup | Page / Git mutations | Async event errors observed via runner; VM owns Git failures | Captured repository and mutation availability checked before execution | Fixed |
| `MainPage.About.cs` / About and update click | Page / network read and updater | Main event boundary runner; update check catches cancellation/errors | About dialog scoped to page; update gate remains in VM | Fixed main handlers |
| `MainPage.About.cs` / Dialog_Opened and release link | Dialog / version lookup | Void wrapper uses page runner; expected cancellation scoped to captured dialog token | No UI writes after dialog close or page shutdown; listeners removed | Fixed |
| `MainPage.FileOpening.cs` / file action state refresh | Page / read | Observe no-await task; FileActionsViewModel owns state | Current-request check and cancel by VM | Fixed |
| `MainPage.FileOpening.cs` / file menu | Page / file request | UI event runner + existing catch and error presentation | IsCurrent and selection check before flyout | Fixed |
| `MainPage.RepositoryFiles.cs` / activation and deactivation | Page / repository files reading | Async runner observes errors | Activation revision and selected-tab guard | Fixed |
| `MainPage.RepositoryFiles.cs` / search/open events | Page / content search/file open | Async runner owns error boundary | VM selection/session fences; page lifetime | Fixed |
| `MainPage.RepositoryFilePreview.cs` / LoadRepositoryFilePreviewAsync | Selection / canceled file read | Expected cancellation filtered; unexpected errors logged | PreviewPublicationGate plus repository/commit/path and IsShuttingDown | Fixed |
| `MainPage.ImageDiff.cs` / LoadCommitImageDiffAsync | Selected commit / diff read | Existing CTS, runner logs unexpected errors | Generation/commit/file identity plus IsShuttingDown | Fixed |
| `MainPage.Changes.cs` / image diff initiation | Page / preview | Runner observes launch faults | Selection gate in ImageDiff | Fixed |
| `MainPage.RepositoryRefresh.cs` / status check loop | Repository session / read | Page runner observes outer unexpected failures; inner read cancellation handled | Pending invalidations, generation, repository and shutdown guards | Fixed / existing generation policy retained |
| `MainPage.RepositoryRefresh.cs` / watcher dispatcher callbacks | Repository session / notification | Failed enqueue drops stale notification; no awaiting TaskCompletionSource | Delivery epoch + repository identity recheck | Retain |
| `MainPage.RepositoryRefresh.cs` / presentation refresh enqueue | Page / UI-only | Rejected dispatcher enqueue clears pending refresh | Coalesced queue; shutdown check | Retain |
| `MainPage.HistoryPerformanceDiagnostics.cs` / start/stop/timers/settings | Page / diagnostics | Startup errors shown safely; stops observed; only Outcome.Success says saved | Single-flight facade; ten-minute max; no dialog after shutdown | Fixed |
| `Diagnostics/HistoryPerformanceDiagnostics.cs` / session writer | Capture / file write | Writer failure observed and output marked Partial/Failed | First stop reason wins; all same session stop callers await same task | Fixed |
| `Diagnostics/HistoryPerformanceDiagnostics.cs` / capture facade | App / diagnostics ownership | Start excluded while previous stop pending | Same Task for overlapping stops, detached callbacks before wait | Fixed |
| `MainPage.Lifecycle.cs` / PrepareForShutdown + BeginShutdown | Page / two-phase cleanup | Diagnostics Task awaited by App with 3s budget | Single early and final stages, no repeated Dispose | Fixed |
| `MainPage.RepositorySwitching.cs` / click handlers | Repository session / navigation + Git flows | UI runner observes event failures; repository identity rechecked after draft confirmation | IsChangingRepository and active-session guards; modal serialized | Fixed |
| `MainPage.CommitActions.cs` / short dialogs | Dialog / optional Git mutation | Serialized confirmation and repo recheck | RunPageUiOperation + VM mutation gate | Fixed |
| `MainPage.BranchDragDrop.cs` / Drop | Page / Git mutation | UI runner observes callback failures | Existing branch protections and shutdown gate | Fixed |
| `MainPage.HistoryInfiniteScroll.cs` / page load | Repository session / history read | UI runner observes event failure; finally clears pending flag | Scroll threshold and loading gate; shutdown guard | Fixed |
| `Controls/AuthorAvatarLifecycleState.cs` / avatar changes | Control / cancelable image requests | Existing cancellation and version token | Stale response ignored | Retain |
| `MainPage.RepositoryCloning.cs` / CloneRepository_Click | Dialog / clone workflow | Runner for events; all ContentDialogs use page modal gate; native picker cancellation silent | Clone/create/VM mutation gates; checks shutdown before switch | Fixed with modal contract |
| `MainPage.RepositoryCreation.cs` / CreateRepository_Click | Dialog / create workflow | Runner for events; modal gate serializes creation and follow-up messages | Create/clone/VM mutation gates; checks shutdown before create | Fixed with modal contract |
| `MainPage.ForcePush.cs` / PushTo + ForcePushWithLease | Page / high-risk Git push | UI runner observes event errors, confirmation uses page dialog gate | Existing expected-remote-commit lease and mutation restrictions remain | Fixed entry points |
| `MainPage.EditCommitMessage.cs` / EditCommitMessage_Click | Page / history rewrite | UI runner observes event errors; dialog serialized | Existing rewrite safety checks | Fixed entry point |
 
| `MainPage.Stashes.cs` / create/drop dialogs | Repository / destructive changes | Runner for events; page dialog gate | Repo identity, stash commit selection and CanExecute validated after await | Fixed |
| `MainPage.BranchRename.cs` / F2 confirmation | Repository / branch rename | Runner and serialized modal | Captured repo rechecked before rename | Fixed |
| `MainPage.BranchDeletion.cs` / delete/checkout | Repository / protected refs | Serialized modal + mutation service | Captured repository and busy guard after modal | Fixed |
| `MainPage.BranchFolderDeletion.cs` / batch delete | Repository / protected refs | Serialized modal + VM failure handling | Captured repository before modal, rechecked before execution | Fixed |
| `MainPage.Tags.cs` / tag and remote-tag actions | Repository / Git mutations + network lookup | Runner for menu and remote selector; lookup errors observed | Dialog gate, repo identity and query revision fences late results | Fixed |
| `MainPage.Worktrees.cs` / worktree actions | Repository / Git mutations | Page gate, existing VM failure messages | Captured repository, busy gate and shutdown recheck | Fixed |
| `MainPage.HistoryRewrite.cs` / remove path | Repository / destructive rewrite | Serialized confirm, error notification | Repository identity checked after prepare and confirm | Fixed |
| `MainPage.InteractiveRebaseAuthor.cs` / change author | Dialog / network identity read and todo edit | Runner observes author change/flyout callbacks | Captured repo and page lifetime checked before UI writes | Fixed |
| `MainPage.WorkingTreeDiff.cs` / selection, binary preview, restore | Repository preview / asynchronous read | Runner observes no-await work; IO failures logged | Selection+generation+repo checks, shutdown fence, dispatcher rejection cleanup | Fixed |
| `MainPage.RecentRepositories.cs` / settings and metadata | Page / recent item persistence | Runner observes property-change asynchronous writes | Shutdown guard and VM current context | Fixed |
| `MainPage.RepositoryMaintenance.cs` / optimize | Repository / maintenance | Runner observes outer handler, nested error notification caught | Existing maintenance gate, repo identity check after prepare | Fixed |
| `MainPage.xaml.cs` / toolbar and shortcuts | Page / event boundaries | Runner observes history, sync and navigation commands | Mark key handled synchronously; no new action after shutdown | Fixed |
| `DispatcherQueueExtensions.EnqueueAsync` / awaited UI dispatch | UI dispatcher / test or callback | UiDispatchCompletion resolves rejected dispatch, callback exception, and late owner cancellation | One-shot delivery; no permanently waiting TCS | Fixed with behavioral tests |
| `App.xaml.cs` / macOS close+host stop | Window+process / shutdown | Await managed host StopAsync before final native close | Single shared StopHostAsync flight; Program joins after event loop | Fixed with integration test coverage |
| `Core/Threading/AsyncFirstStop.cs` / history diagnostics | Capture / writer teardown | TaskCompletionSource published before callbacks; writer faults shared | First stop reason wins, reentrant and concurrent stops share task | Fixed with behavioral tests |
| `Core/Threading/UiBoundedTaskWait.cs` / teardown budget | Window / asynchronous writer join | 3-second WaitAsync; eventual fault observed; failed reporter contained | No synchronous blocking or UI use after timeout | Fixed with behavioral tests |

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
- `StopHost()` synchronously joins only in `Program.Main` after Uno event loop exits; on macOS the same single-flight managed host shutdown is awaited asynchronously before native final close.
- Existing repository refresh coalescing/generation, preview publication gate and avatar lifecycle are reused instead of creating a second task scheduler or global CTS.

## Shutdown and error invariants

First normal Close checks protected operations; if necessary, confirms unsaved draft; otherwise prevents the first close event, transitions page to closing and stops accepting new UI work. The UI waits for diagnostics writer/summary for **up to three seconds**, finalizes page exactly once and authorizes a second Close. Timeouts/errors are traced and do not open new dialogs. A timed-out writer may leave partial output. Direct UI command errors are observed by the event boundary and may not be rethrown as async void exceptions. Expected cancellation is silent only when its lifetime guard actually indicates cancellation.

## Validation and outstanding risks

GitHub Actions builds and unit/contract suites provide automated validation. Local dotnet and manual Windows/macOS/Linux desktop test sessions are unavailable to this executor. Record platform-specific CI outcomes from the commit's Actions run, not from unrelated or earlier commits.

Validated checkpoint: commit `c88fbc5611ffb50022680f61718261d3fa00a050`, GitHub Actions build run `38738507774`, **passed** on Windows, macOS and Ubuntu (Application 511/511; Desktop Windows/macOS 535/535, Ubuntu 534/535 with the graphics-dependent test skipped; Git 494/494). In that run, `DesktopApplicationTests.LaunchesRealApplicationForRepositoryAndLinkedWorktree` **passed on Windows and macOS**, and was **skipped on Ubuntu** because no interactive graphical display exists. This is evidence for that SHA, not for commits added afterward.

Additional deterministic behavioral coverage: `UiAsyncOperationRunnerTests` (sync/async faults, expected cancellation, failing reporter); `UiPageDialogGateTests` (serialized modals, rejected queued dialogs, canceled results); `UiBoundedTaskWaitTests` (completed/faulted/delayed/hung writer, late fault, reporter failure); `AsyncFirstStopTests` (concurrency, original reason, synchronous reentry, faults); `UiDispatchCompletionTests` (rejected dispatch, ignored late callback, callback fault); plus existing presentation, history diagnostics, Git and desktop integration suites.

Final verification policy: for a newer commit, report acceptance only if **its own** build/test matrix is green. Windows and macOS exercise actual desktop close, repository and mutation lifecycle; Linux CI verifies build, publish, non-UI behavior and headless tests but **does not run a real close window test** (no graphics session). No manual interactive Linux desktop pass is claimed.

Known safe exceptions: `App.OnLaunched` is a framework-required `async void` boundary with its own try/catch; the desktop-only check harness is similarly guarded. Native folder picker cancellations are intentionally silent. Already-running Git mutations are not canceled by window-close requests. No new global command infrastructure, cancellation service or independent mutation gate was introduced.

Acceptance evidence for UI-01 through UI-08: inventory rows above; deterministic behavioral tests (`UiAsyncOperationRunnerTests`, `UiPageDialogGateTests`, `UiBoundedTaskWaitTests`, `AsyncFirstStopTests`, `UiDispatchCompletionTests`, `WindowCloseSafetyTests`), updated contract tests, and actual desktop end-to-end checks. Clone/create dialogs now use `ShowPageDialogAsync` rather than bypassing the page gate. All remaining limitations are platform validation limitations, not accepted proof of untested execution.
