# MainPage migration map

Phase 3 implementation baseline: `ee3c8909d02db75a9d3ca02117d2d6d6a3b042dc` (after phase 2.6). Historical phase-2 baseline: `e8ebd64467519d642978c31ae635f511db432aba` (subsequent phase-1 fixes on `main` are included). This matrix documents the agreed three-phase boundary. A phase-2 feature cannot retain its implementation in `MainPage` after extraction.

## Phase 3 incremental implementation

Destructive path-history rewrite confirmation, operation admission, error reporting
and invalidation are owned by `Workflows/HistoryRewriteController`.
Repository files and content-search results share the context-menu implementation
in `MainPage.RepositoryFiles.cs`, including the history-rewrite action.
The obsolete `MainPage.HistoryRewrite.cs` duplicate right-tap route was removed;
the historical baseline inventory below intentionally lists earlier files.


Repository switch and close admission, commit-draft confirmation, lifecycle continuation
and successful-transition projections are now owned by
`Workflows/RepositorySwitchController`. The page retains the two button handlers,
public desktop check seams, and shell helpers while existing creation/clone
workflows call through typed forwarding methods.


All worktree mutations and dialogs, menu building, worktree-model subscription and
process relaunch are now in `Workflows/WorktreeController`. The page provides the
window shell dependencies and retains only the XAML right-tap entry point; the old
`MainPage.Worktrees.cs` is removed. The model stays the owner of Git operations.


Commit message rewrite dialog, conflict-pending OIDs and post-rebase selection
restoration are now owned by `Workflows/EditCommitMessageController`.
It calls the existing CommitActionsViewModel mutation and preserves selection
callbacks; the history context-menu handler invokes it directly. The old
`MainPage.EditCommitMessage.cs` partial is removed.


Local and remote branch-folder delete dialogs, candidate planning, partial-failure
reporting and safe confirmation now belong to `Workflows/BranchFolderDeletionController`.
The model remains the sole owner of Git deletion; the controller validates repository
identity and operation state after each dialog. `MainPage.BranchFolderDeletion.cs`
is removed and the context menu calls the window-scoped workflow directly.


Stash creation (repository-wide and selected paths) and destructive drop confirmation
are now owned by `Workflows/StashDialogController`. Its operations share the
window dialog gate, revalidate repository identity and selection after confirmation,
and dispatch to the existing StashesViewModel. `MainPage.Stashes.cs` was removed;
XAML and reference-menu entry points retain only thin forwarding adapters.


Local branch renaming (F2/dialog) and branch drag/drop now belong to separate
`Workflows/BranchRenameController` and `Workflows/BranchDragDropController`.
Both own and dispose their UI event subscriptions; drag/drop requests go through
one typed rename mutation owner. The page keeps temporary thin adapters for
reference-menu actions until Git-reference surfaces are extracted.


Commit changed-file tree reconciliation, expand/collapse state, selection synchronization
and viewport lifetime now belong to `Workflows/CommitChangedFileTreeController`.
The owner explicitly detaches its collection and tree subscriptions on shutdown;
`MainPage.Changes.cs` retains only the Commit diff and tab-activity choreography.


WorkingTree image-diff generation, cancellation and result publication now belong to
`Workflows/WorkingTreeImageDiffWorkflow`, sharing `ImageDiffPreviewService` with
Commit Details. `MainPage.WorkingTreeDiff.cs` retains selection/tree rendering but no
image-diff async lifetime. A lazy service accessor preserves the existing shell
constructor order without admitting work before file services are attached.


Historical Repository Files preview now belongs to `Workflows/RepositoryFilePreviewController`.
It owns the publication gate, cancellation, deferred selection and activation revision;
`MainPage.RepositoryFilePreview.cs` was deleted. The files surface passes a typed preview
host accessor and maintains its separate search/tree-selection lifecycle.


Text/binary/image diff state-to-visual mapping is now owned by
`Workflows/DiffPresentationController`, with separate typed Commit and WorkingTree
control groups. The old `MainPage.DiffPresentation.cs` was deleted; change and file
preview event paths call the controller directly rather than delegating to a page method.


Commit image-diff admission, deferred resume, cancellation and generation-safe publication
are now owned by `Workflows/CommitImageDiffWorkflow`. The shared
`ImageDiffPreviewService` is composed once per window for commit and working-tree
previews. The presentation-state enum is a separate internal type; the old
`MainPage.ImageDiff.cs` is deleted.


The page-local modal gate, dialog lifetime cancellation and interactive rebase
editor workflow are now owned by `Dialogs/PageDialogController` with a normal
`Page` visual-surface reference rather than a dependency on `MainPage`.
`MainPage.Dialogs.cs` is removed. Legacy call sites use thin shell adapters
until their respective feature views are migrated.


Repository tree/working-tree projection updates are now coalesced by
`Workflows/RepositoryPresentationRefreshController`. It owns the dispatch queue and
coalescing state, with typed callbacks to update the existing visual surfaces.
`MainPage.RepositoryRefresh.cs` is removed; the page retains only the
`IsRefreshRequired` binding property and forwarding callbacks until the shell
becomes composition-only.


Commit Details viewport layout now belongs to `Workflows/CommitDetailsLayoutController`.
It owns window-size, scroll/width and XamlRoot subscriptions and explicitly detaches
on shutdown. `MainPage.CommitCopy.cs` is removed; native resize forwarding remains
as a narrow shell adapter until final root composition migration.


Commit Details loading overlays now belong to
`Workflows/CommitLoadingOverlayController`. This controller owns visual changes,
loading rings and the Commit Details subscription, while MainPage holds only a
window-lifetime adapter until the final Commit Details view migration.
`MainPage.LoadingOverlays.cs` is removed.


Repository Clone and Create dialog workflows are now owned by separate window-scoped
`Workflows/RepositoryCloneWorkflow` and `Workflows/RepositoryCreationWorkflow`.
They coordinate admission leases, draft confirmation and repository switch via narrow
callbacks. The old `MainPage.RepositoryCloning.cs` and `MainPage.RepositoryCreation.cs`
are removed. Page compatibility properties for active workflows remain until the
repository-switch workflow itself moves in phase 3.


Commit empty-index and Working Tree discard confirmations now belong to
`Workflows/RepositoryConfirmationController`. It owns subscriptions and cancellation
via the window dialog gate, with repository-identity checks before commands.
`MainPage.ConfirmationDialogs.cs` is removed and shutdown detaches its listeners.


The watcher/refresh baseline lifecycle, mutation-followup queue and refresh toolbar
indicator now belong to per-window `Workflows/RepositoryRefreshController`, with
explicit subscription/shutdown ownership. `MainPage.RepositoryRefresh.cs` retains
only temporary UI collection projection queueing; it will be removed when those
projections migrate to Working Tree and Files owners.


Manual refresh action now belongs to `Workflows/RepositoryManualRefreshController`.
It receives typed repository and safety callbacks rather than accessing MainPage;
`MainPage.RefreshIndicator.cs` is removed. The repository monitor/refresh flight
still owns the shared refresh state, and the toolbar owns its indicator visuals.


The final console adapter `MainPage.GitConsole.cs` has been deleted; window
composition configures `GitConsoleHostView` and `GitCommandFeedbackController`
directly. Runtime commands use the host view and shutdown disposes both owners.
The two read-only `MainPage.HistoryDisplayMode.cs` visual accessors were also
removed; existing commit actions and desktop diagnostics address `HistoryView`
directly until their full phase-3 feature extraction.


`Dialogs/GitCommandFeedbackController` now owns the Git command feedback coordinator,
modal presentation, settings subscriptions and countdown timer. The former
`MainPage.GitCommandFeedback.cs` is deleted. The console host retains only typed
callbacks to this window-scoped owner; the final window composition step will
eliminate the remaining `MainPage.GitConsole.cs` adapter.


Settings entry points now belong to `SettingsWindowController` with a window-scoped
repository/admission context. `MainPage.Settings.cs` has been deleted; toolbar,
start screen and Git tool actions invoke typed controller entry points directly.

The About/self-update UI is now owned by `Dialogs/AboutUpdateDialogController`.
`MainPage.About.cs` was removed; the start screen and toolbar invoke that window-scoped
workflow. The controller receives narrow dialog and close callbacks; the final
window-composition migration will remove those remaining page callback adapters.


The phase-2 `MainPage.HistoryPerformanceDiagnostics.cs` forwarding adapter
has been removed: window composition now configures the owner
`HistoryView.ConfigurePerformance` directly, and shutdown calls
`HistoryView.StopHistoryPerformanceCaptureOnShutdown`. Timers, event
subscriptions and diagnostic state were already owned and torn down by
`HistoryView`; no alternate global controller or view locator was introduced.



The recent-repository native folder picker is now a focused
`RecentRepositoryPickerWorkflow`, which owns picker errors and checks live
repository-switch admission again after awaiting the native chooser. Its
existing `MainPage` wrapper is strictly transitional; selection and switching
remain routed through the existing repository-switch workflow while phase 3.5
extraction proceeds. The former `MainPage.RecentRepositories.cs` is removed.



Commit-draft close confirmation is now owned by the per-window
`CommitDraftCloseConfirmationDialog`, passed directly to
`WindowCloseOrchestrator`. It reads the live draft state and rejects a late
dialog result after shutdown or repository switching. The existing page dialog
gate supplies serialized presentation and `XamlRoot`; the remaining
`MainPage.ConfirmCloseAsync` adapter is only for legacy switching/restart
callers, and must be removed during the final window-composition migration.



The working-tree commit-message editor, Commit/Amend commands and Stash button
now live in `WorkingTreeCommitEditorView` as one visual component. The parent
still handles `StashRequested` through its existing dialog workflow, with an
explicit shutdown unsubscription. This is an intermediate narrow event, not a
claim that the Working Tree workflow or the whole `MainPage` was extracted.


The initial phase-3 extraction gives `WorkspaceNavigator` **scoped window ownership**. `App` registers it once per window DI scope, `WindowSessionFactory` resolves the same instance that `MainPage` receives through constructor injection in its existing full dependency chain, and `WindowSession` explicitly retains the owner reference. History scrolling remains local to `HistoryView`. The semantic navigation and interaction-version checks remain in `WorkspaceNavigator` / `RepositoryTreeNavigationCoordinator`. This is a partial migration; the still-present `MainPage.RepositoryNavigation.cs` visual adapters and legacy lifecycle are not considered complete.

Remaining module extraction must preserve each row's assigned owner in the matrix below. Existing diagnostic runners and tests remain valid while being moved behind feature-owned inspection contracts.

## Cross-file ownership and migration matrix

Each source entry has one planned owner. Temporary adapters are **not** new general-purpose service containers; remove them at the specified phase. The acceptance column describes when the legacy implementation may be deleted, not merely wrapped.

| Existing file | Planned owner | Phase | Required dependencies | Temporary integration | Exit criterion |
|---|---|---|---|---|---|
| `MainPage.xaml` | MainPage (composition only), then phase-3 feature views | 2 + 3 | OpenRepositoryViewModel, XAML controls | Phase-3 Working Tree/Files/dialog integration | Migrated regions contain only component placement and typed integration |
| `MainPage.xaml.cs` | MainPage (composition only), then phase-3 feature views | 2 + 3 | OpenRepositoryViewModel, XAML controls | Phase-3 Working Tree/Files/dialog integration | Migrated regions contain only component placement and typed integration |
| `MainPage.Lifecycle.cs` | WindowSession + IWindowContentLifetime | 1 → 3 | WindowSession, diagnostic/preview cancellation | Page implements IWindowContentLifetime until phase 3 | No page-owned close admission; adapter deleted in phase 3 |
| `MainPage.RecentRepositories.cs` | StartScreenView | 2.2 | RecentRepositoriesView, RecentRepositoriesViewModel, settings | Create/clone dialogs remain behind narrow phase-3 commands | Start-screen subscriptions and recent-list lifecycle removed from MainPage |
| `MainPage.RepositorySwitching.cs` | RepositorySelectorView + RepositoryHeaderView | 2.2 | OpenRepositoryViewModel, repository selector grouping | Commit-draft confirmation lives in phase-3 dialog adapter | Selector UI, grouped entries, and actions are view-owned |
| `MainPage.LoadingOverlays.cs` | RepositoryToolbarView + shell state | 2.2 | Repository operation and refresh state | Remaining phase-3 dialogs invoked by typed commands | No MainPage control mutation for toolbar and refresh visuals |
| `MainPage.RefreshIndicator.cs` | RepositoryToolbarView + shell state | 2.2 | Repository operation and refresh state | Remaining phase-3 dialogs invoked by typed commands | No MainPage control mutation for toolbar and refresh visuals |
| `MainPage.Settings.cs` | Settings and About dialog feature surfaces | 3 | SettingsWindowController, update services | Toolbar delegates to narrow commands | Settings and About workflows extracted from MainPage |
| `MainPage.About.cs` | Settings and About dialog feature surfaces | 3 | SettingsWindowController, update services | Toolbar delegates to narrow commands | Settings and About workflows extracted from MainPage |
| `MainPage.AuthorAvatars.cs` (deleted in 2.6) | HistoryView and CommitDetailsView (instance dependencies) | 2.1 → 2.6 | IAuthorAvatarService, IAppSettingsService | HistoryView implements IAuthorAvatarDependencyProvider; CommitDetailsView explicitly configures avatar | Both render from instance-owned dependencies without a page/global fallback |
| `MainPage.RepositoryNavigation.cs` | RepositoryExplorerView + WorkspaceNavigator | 2.4 | RepositoryTreeSynchronizer, RepositoryTreeNavigationCoordinator | Phase-3 branch/stash dialog operations behind typed contracts | View owns TreeView/selection/expand; navigator owns navigation only |
| `MainPage.RepositoryTreeState.cs` | RepositoryExplorerView + WorkspaceNavigator | 2.4 | RepositoryTreeSynchronizer, RepositoryTreeNavigationCoordinator | Phase-3 branch/stash dialog operations behind typed contracts | View owns TreeView/selection/expand; navigator owns navigation only |
| `MainPage.RepositoryRefresh.cs` | RepositoryExplorerView + window refresh coordination | 2.4 | RepositoryChangeCoordinator, RepositorySessionLifetime | Phase-3 preview invalidation remains local to Files | Stale repository requests rejected without MainPage tree access |
| `MainPage.HistoryDisplayMode.cs` | HistoryView and its local controllers | 2.5 | HistoryViewModel, CommitGraphLayoutState, diagnostics | Commit Details and context dialogs remain phase-3 consumers | Graph, filters, virtual scrolling and diagnostics fully view-owned |
| `MainPage.HistoryInfiniteScroll.cs` | HistoryView and its local controllers | 2.5 | HistoryViewModel, CommitGraphLayoutState, diagnostics | Commit Details and context dialogs remain phase-3 consumers | Graph, filters, virtual scrolling and diagnostics fully view-owned |
| `MainPage.HistoryPerformanceDiagnostics.cs` | HistoryView and its local controllers | 2.5 | HistoryViewModel, CommitGraphLayoutState, diagnostics | Commit Details and context dialogs remain phase-3 consumers | Graph, filters, virtual scrolling and diagnostics fully view-owned |
| `MainPage.CommitGraphLayout.cs` | HistoryView and its local controllers | 2.5 | HistoryViewModel, CommitGraphLayoutState, diagnostics | Commit Details and context dialogs remain phase-3 consumers | Graph, filters, virtual scrolling and diagnostics fully view-owned |
| `MainPage.GitConsole.cs` | GitConsoleHostView + GitConsoleView | 2.3 | IGitCommandActivitySource, splitter/layout | GitCommandFeedback stays a separate phase-3 modal | Console layout, streaming, shutdown owned by host |
| `MainPage.RepositoryCreation.cs` | Dedicated repository workflow dialogs/controllers | 3 | Create/Clone/Maintenance ViewModels | StartScreen/selector call typed workflow commands | Full dialog lifecycle and operations removed from MainPage |
| `MainPage.RepositoryCloning.cs` | Dedicated repository workflow dialogs/controllers | 3 | Create/Clone/Maintenance ViewModels | StartScreen/selector call typed workflow commands | Full dialog lifecycle and operations removed from MainPage |
| `MainPage.RepositoryMaintenance.cs` | Dedicated repository workflow dialogs/controllers | 3 | Create/Clone/Maintenance ViewModels | StartScreen/selector call typed workflow commands | Full dialog lifecycle and operations removed from MainPage |
| `MainPage.Worktrees.cs` | WorktreesView + dialog controllers | 3 | WorktreesViewModel, worktree services | RepositoryExplorer navigation exposes references only | Worktree operations and context menus feature-owned |
| `MainPage.WorktreeContextMenu.cs` | WorktreesView + dialog controllers | 3 | WorktreesViewModel, worktree services | RepositoryExplorer navigation exposes references only | Worktree operations and context menus feature-owned |
| `MainPage.CommitActions.cs` | CommitDetailsView + commit-operation dialog controllers | 3 | CommitActions/CommitDetails/HistoryRewrite ViewModels | HistoryView emits selected commit via existing HistoryViewModel | All commit dialog/operation handlers absent from MainPage |
| `MainPage.CommitCopy.cs` | CommitDetailsView + commit-operation dialog controllers | 3 | CommitActions/CommitDetails/HistoryRewrite ViewModels | HistoryView emits selected commit via existing HistoryViewModel | All commit dialog/operation handlers absent from MainPage |
| `MainPage.EditCommitMessage.cs` | CommitDetailsView + commit-operation dialog controllers | 3 | CommitActions/CommitDetails/HistoryRewrite ViewModels | HistoryView emits selected commit via existing HistoryViewModel | All commit dialog/operation handlers absent from MainPage |
| `MainPage.InteractiveRebaseAuthor.cs` | CommitDetailsView + commit-operation dialog controllers | 3 | CommitActions/CommitDetails/HistoryRewrite ViewModels | HistoryView emits selected commit via existing HistoryViewModel | All commit dialog/operation handlers absent from MainPage |
| `MainPage.HistoryRewrite.cs` | CommitDetailsView + commit-operation dialog controllers | 3 | CommitActions/CommitDetails/HistoryRewrite ViewModels | HistoryView emits selected commit via existing HistoryViewModel | All commit dialog/operation handlers absent from MainPage |
| `MainPage.Changes.cs` | WorkingTreeView + focused diff controllers | 3 | WorkingTreeViewModel, FileActionsViewModel | WorkspaceNavigator switches to Working Tree via model state | Working tree selections/diffs and actions view-owned |
| `MainPage.WorkingTreeDiff.cs` | WorkingTreeView + focused diff controllers | 3 | WorkingTreeViewModel, FileActionsViewModel | WorkspaceNavigator switches to Working Tree via model state | Working tree selections/diffs and actions view-owned |
| `MainPage.WorkingTreeContextMenu.cs` | WorkingTreeView + focused diff controllers | 3 | WorkingTreeViewModel, FileActionsViewModel | WorkspaceNavigator switches to Working Tree via model state | Working tree selections/diffs and actions view-owned |
| `MainPage.RepositoryFiles.cs` | RepositoryFilesView + preview lifecycle | 3 | RepositoryFilesViewModel, RepositoryFilesSurface, preview services | RepositoryExplorer passes typed navigation targets | File browser, preview, open/diff actions feature-owned |
| `MainPage.RepositoryFilePreview.cs` | RepositoryFilesView + preview lifecycle | 3 | RepositoryFilesViewModel, RepositoryFilesSurface, preview services | RepositoryExplorer passes typed navigation targets | File browser, preview, open/diff actions feature-owned |
| `MainPage.FileOpening.cs` | RepositoryFilesView + preview lifecycle | 3 | RepositoryFilesViewModel, RepositoryFilesSurface, preview services | RepositoryExplorer passes typed navigation targets | File browser, preview, open/diff actions feature-owned |
| `MainPage.DiffPresentation.cs` | RepositoryFilesView + preview lifecycle | 3 | RepositoryFilesViewModel, RepositoryFilesSurface, preview services | RepositoryExplorer passes typed navigation targets | File browser, preview, open/diff actions feature-owned |
| `MainPage.ImageDiff.cs` | RepositoryFilesView + preview lifecycle | 3 | RepositoryFilesViewModel, RepositoryFilesSurface, preview services | RepositoryExplorer passes typed navigation targets | File browser, preview, open/diff actions feature-owned |
| `MainPage.BranchDeletion.cs` | Reference operation dialogs and commands | 3 | Branches/Tags/Stashes ViewModels, operation safety gate | RepositoryExplorer supplies narrow phase-3 action requests | No branch/tag/stash Git operation implementation in Explorer/MainPage |
| `MainPage.BranchFolderDeletion.cs` | Reference operation dialogs and commands | 3 | Branches/Tags/Stashes ViewModels, operation safety gate | RepositoryExplorer supplies narrow phase-3 action requests | No branch/tag/stash Git operation implementation in Explorer/MainPage |
| `MainPage.BranchDragDrop.cs` | Reference operation dialogs and commands | 3 | Branches/Tags/Stashes ViewModels, operation safety gate | RepositoryExplorer supplies narrow phase-3 action requests | No branch/tag/stash Git operation implementation in Explorer/MainPage |
| `MainPage.BranchRename.cs` | Reference operation dialogs and commands | 3 | Branches/Tags/Stashes ViewModels, operation safety gate | RepositoryExplorer supplies narrow phase-3 action requests | No branch/tag/stash Git operation implementation in Explorer/MainPage |
| `MainPage.Tags.cs` | Reference operation dialogs and commands | 3 | Branches/Tags/Stashes ViewModels, operation safety gate | RepositoryExplorer supplies narrow phase-3 action requests | No branch/tag/stash Git operation implementation in Explorer/MainPage |
| `MainPage.Stashes.cs` | Reference operation dialogs and commands | 3 | Branches/Tags/Stashes ViewModels, operation safety gate | RepositoryExplorer supplies narrow phase-3 action requests | No branch/tag/stash Git operation implementation in Explorer/MainPage |
| `MainPage.ForcePush.cs` | Reference operation dialogs and commands | 3 | Branches/Tags/Stashes ViewModels, operation safety gate | RepositoryExplorer supplies narrow phase-3 action requests | No branch/tag/stash Git operation implementation in Explorer/MainPage |
| `MainPage.GitCommandFeedback.cs` | GitCommandFeedbackView + modal controller | 3 | GitCommandFeedbackCoordinator | Distinct from GitConsoleHostView | Modal ownership and dialog sequence local |
| `MainPage.Dialogs.cs` | Feature dialog controllers + serialized dialog host | 3 | UiPageDialogGate, correct XamlRoot | Narrow interfaces for phase-2 components | All shared page dialog entry points removed |
| `MainPage.ConfirmationDialogs.cs` | Feature dialog controllers + serialized dialog host | 3 | UiPageDialogGate, correct XamlRoot | Narrow interfaces for phase-2 components | All shared page dialog entry points removed |

### New phase-2 component ownership

| Component | Window owner | Direct dependencies | Lifetime / disposal | Acceptance |
|---|---|---|---|---|
| `WindowSession` | `WindowSessionFactory` scope | `WindowCloseSafetyProvider`, `WindowTitleState`, legacy `IWindowContentLifetime` | Scope disposed exactly once, including startup failures; native macOS close defers cleanup | Close decision has no `MainPage` input |
| `WindowTitleState` | `WindowSession` | `OpenRepositoryViewModel` | Subscribes once; unsubscribes before scope disposal | App title independent of MainPage events |
| `StartScreenView` | root view | `RecentRepositoriesViewModel` | Owns subscriptions and recent tiles | Repository opening and create/clone entry points work |
| `RepositoryHeaderView` / `RepositorySelectorView` | root view | `OpenRepositoryViewModel`, selector state | UI instance owns menu/selection state | Grouped switching and confirmation parity |
| `RepositoryToolbarView` | root view | Existing Git ICommand properties | UI instance owns subscriptions | Fetch/Pull/Push/refresh/navigation available |
| `StatusBarView` | root view | Repository activity and status projection | UI instance owns animation | No MainPage TextBlock/ProgressRing mutations |
| `GitConsoleHostView` | root view | `GitConsoleView`, activity source | Stream callbacks canceled or ignored on final shutdown | Splitter, keyboard shortcut, streaming parity |
| `RepositoryExplorerView` | root view | Tree synchronizer and navigator | Incremental visuals and event subscriptions are view-local | No direct MainPage Git operations |
| `WorkspaceNavigator` | `WindowSession` | `HistoryViewModel`, repository navigation coordinator | Scoped; no XAML dependencies | History/ref/commit/Working Tree flows race-safe |
| `HistoryView` | root view | `HistoryViewModel`, graph layout and diagnostics | Owns scroll subscription and bounded capture | Existing virtualization/performance retained |

### Phase 2.1 implementation notes

- Transient child ViewModels resolved within the window DI scope are disposed by that scope, not a second time by `OpenRepositoryViewModel`; independent manually constructed `OpenRepositoryViewModel` instances retain cascade disposal.
- `AuthorAvatarServiceContext` and `HistoryReferencePresentationContext` have been removed. HistoryView now implements `IAuthorAvatarDependencyProvider` directly, and CommitDetailsView receives its own explicit avatar configuration. The obsolete MainPage provider was removed in PR 2.6. Default-branch metadata is carried on `HistoryReferenceDecoration`.
- The phase-2 smoke check must instantiate `XamlOnlyProbe`, attach a DataContext, assert its binding, mutate an `INotifyPropertyChanged` source, and assert updated text. No `.xaml.cs` is created.
- PR 2.5 replaced the global `CommitGraphPresentationContext` with per-HistoryView `ICommitGraphLayoutProvider`, and deleted the static context. Virtualized graph controls subscribe to their visual ancestor's layout.

## Phase 2 completion inventory (implementation, not baseline)

The source-evidence tables below capture the **pre-migration baseline** and must not be interpreted as the current ownership of extracted controls. The authoritative implementation boundaries after PRs 2.1–2.5 are:

| Owner | Creation and scope | Owns and tears down | Phase-3 integration to remove later |
|---|---|---|---|
| `WindowSession` / `WindowSessionFactory` | One DI scope per main window | title publisher, close admission, scoped feature models; unhooks before scope disposal | `IWindowContentLifetime` bridge to `MainPage` |
| `MainPage` | Main window root within `WindowSession` | composition and remaining Working Tree / Commit Details / Files / dialog surfaces | legacy Git dialog and preview hosts, eventually replaced in phase 3 |
| `StartScreenView` | XAML child of `MainPage` | recent repository projection, tile model, settings and repository subscriptions, explicit `ShutdownRecentRepositories` | `StartScreenActions` for repository picker/clone/create and error dialogs |
| `RepositoryHeaderView` / `RepositorySelectorView` | XAML child of `MainPage`, header owns selector | grouped quick list, selector menu, folder/terminal/path UI, repository subscription, explicit `Shutdown` | `RepositorySelectorActions` for commit-draft confirmation and repository switch workflows |
| `RepositoryToolbarView` | XAML child of `MainPage` | toolbar XAML, refresh visuals, commit navigation label, optimization menu availability, typed command intents | `RepositoryToolbarAction` adapter to phase-3 Git dialog/workflow handlers |
| `GitConsoleHostView` / `StatusBarView` | Single XAML child of workspace; host owns status and console | status pulse, Git command status, activity-source subscriptions, streaming, console resize persistence, keyboard shortcut, explicit `Shutdown` | `MainPage.GitConsole.cs` adapter to separate GitCommandFeedback modal |
| `RepositoryExplorerView` | XAML child of workspace | tree roots, synchronizer, selection, expand/collapse, event subscriptions, explicit `Shutdown` | `MainPage.RepositoryTreeState.cs` exposes read-only tree to phase-3 branch/stash/tag/Files consumers; `ReferenceMenuRequested` bridges complex Git menus |
| `WorkspaceNavigator` | One instance per window, created by root composition | reference/commit/Working Tree target decisions and existing `RepositoryTreeNavigationCoordinator`; XAML-free; retained with window | navigation visuals and legacy Commit Details access currently adapted by `MainPage.RepositoryNavigation.cs` |
| `HistoryView` | XAML child of workspace | history filter/scope/reflog, list, lazy scroll subscription, per-instance graph layout + geometry cache, bounded diagnostics and timers, explicit `Shutdown` | `MainPage.HistoryDisplayMode.cs` retains only two read-only accessors for the phase-3 Commit Details and desktop diagnostics consumers; `MainPage.HistoryPerformanceDiagnostics.cs` hosts the narrow dialog callback |
| `CommitGraphControl` | Instance per virtualized row | rendering state and cache from nearest ancestor `ICommitGraphLayoutProvider`, unsubscribes at `Unloaded` | none; process-global `CommitGraphPresentationContext` deleted |

**Preserved phase-3 scope.** `MainPage.WorkingTreeDiff.cs`, `MainPage.Changes.cs`, `MainPage.CommitActions.cs`, `MainPage.RepositoryFiles.cs`, `MainPage.FileOpening.cs`, `MainPage.DiffPresentation.cs`, `MainPage.ImageDiff.cs`, `MainPage.BranchDeletion.cs`, `MainPage.BranchRename.cs`, `MainPage.BranchDragDrop.cs`, `MainPage.Tags.cs`, `MainPage.Stashes.cs`, `MainPage.RepositoryCreation.cs`, `MainPage.RepositoryCloning.cs`, `MainPage.RepositoryMaintenance.cs`, `MainPage.Settings.cs`, `MainPage.About.cs`, `MainPage.GitCommandFeedback.cs`, and shared page dialogs are **not** migrated by phase 2.

**Avatar ownership.** History rows obtain `IAuthorAvatarService` and `IAppSettingsService` from their own `HistoryView` visual ancestor; `CommitDetailsView` uses `ConfigureAuthorAvatar`. `MainPage` no longer implements `IAuthorAvatarDependencyProvider`, and neither component uses application-global UI state.

**Known transitional dependencies.** Repository context menus still enter phase-3 operation/dialog code through `ReferenceMenuRequested`; History selection still has two thin accessors for existing Commit Details and diagnostics smoke checks; graph layout and history scrolling are fully view-owned. They must be replaced with operation-specific contracts and independent feature views, not promoted into a universal `IMainPageActions` or service locator. These adapters have an explicit removal target: **phase 3**.

**Removed obsolete migration files.** `MainPage.AuthorAvatars.cs`, `MainPage.CommitGraphLayout.cs` and `MainPage.HistoryInfiniteScroll.cs` have been deleted rather than preserved as forwarding-only partial classes. `MainPage.RepositoryTreeState.cs` invokes `HistoryView.InitializeScroll()` directly. History navigation uses `HistoryView.ScrollTo(...)` and `FocusFilter()` rather than mutating the list and search controls from `MainPage`. Their pre-migration file names remain in the historical source-evidence table below for traceability; no current source ownership is implied.

**Lifetime guardrails.** `Unloaded` is not final disposal. The root invokes the children’s explicit shutdown during normal `WindowSession` cleanup; each child unhooks events it owns. History diagnostics stops asynchronously during `PrepareForShutdown` before final view disposal. A cross-repository navigation result is accepted only when both reference identity and interaction/selection versions still match.

**Verification scope.** PR CI exercises .NET build, automated tests and the existing cross-platform publish/smoke checks on Windows/macOS/Linux. Real interactive resize, streaming, complex dialogs, platform-specific focus and visual-performance comparisons remain useful manual acceptance evidence; do not infer measured FPS, memory or latency from source-only assertions.

## Existing high-risk shared scenarios

- **Close:** `App.xaml.cs` owns native close callbacks and host stop; `WindowCloseOrchestrator` and `WindowCloseSafetyProvider` decide admission and confirmations, independent of `MainPage`; `WindowSession` owns lifetime and scope. `MainPage.Lifecycle.cs` only completes legacy UI cleanup via `IWindowContentLifetime` until phase 3.
- **Repository switch:** `MainPage.RepositorySwitching.cs`, `RepositoryRefresh.cs`, `RepositoryTreeState.cs`, `RepositoryNavigation.cs` and `RepositoryFilePreview.cs`. Phase-2 selector owns menu and selection UI, `WorkspaceNavigator` owns navigation decisions, and the phase-3 repository-switch/dialog workflow remains behind a typed action adapter; preserve `RepositorySessionLifetime` and `RepositoryChangeCoordinator`.
- **History:** `MainPage.HistoryInfiniteScroll.cs`, `HistoryDisplayMode.cs`, `CommitGraphLayout.cs`, `HistoryPerformanceDiagnostics.cs` and `MainPage.xaml.cs`. Phase 2.5 owner: HistoryView plus existing HistoryViewModel.
- **Files:** `MainPage.RepositoryFiles.cs`, `RepositoryFilePreview.cs`, `FileOpening.cs`, `WorkingTreeDiff.cs` and `DiffPresentation.cs`. Phase 3 owner: separate file surfaces and preview lifecycle; use existing `RepositoryFilesSurface`.
- **Dialogs and commands:** `MainPage.Dialogs.cs`, `ConfirmationDialogs.cs` and feature files. Each phase-3 feature owns its dialog; shared serialization remains the existing page dialog gate until extracted.

## Lifetime and subscription hotspots

- `MainPage.xaml.cs`: subscribes to repository, history, branches, tags, remotes, stashes and working-tree collections; uses window-title callback. All corresponding unsubscriptions must be paired in `MainPage.Lifecycle.cs` while the temporary adapter exists.
- `MainPage.Lifecycle.cs`: `PrepareForShutdown` cancels dialogs, previews and repository monitor; `BeginShutdown` unregisters change handlers, shuts down child surfaces, disposes feature models. Scope ownership prevents duplicate feature disposal; page releases its own subscriptions.
- `App.xaml.cs`: owns host and native Window, theme registration, title update and layout resize callback. Scope/host must live until window-specific cleanup completes.
- `MainPage.HistoryPerformanceDiagnostics.cs`: bounded diagnostic task must not delay window closure indefinitely; no blanket timeout for mutations.

## Phase 1 boundaries

1. `App` creates a scope for each window and does not expose a global window service locator.
2. The session holds no concrete `MainPage` reference; any compatibility bridge is `IWindowContentLifetime` and is deleted in **phase 3**.
3. MainPage remains a visual owner while migrating; after phase 1 no new functional `MainPage.*.cs` files or constructor dependencies.
4. Disposal and event unsubscription belong to the object that created the subscription; review transient `OpenRepositoryViewModel`, `FileActionsViewModel`, `RepositoryFilesViewModel` and `RepositoryMaintenanceViewModel` for existing cascaded disposal before scope ownership changes.
5. Confirm macOS deferred-native-close, Windows WM_CLOSE and Linux behavior with desktop checks before moving shutdown.

## Verification

The matrix is a responsibility-oriented planning map. Exact private fields and subscription pairs must be checked against each file before phase 2 migration; these are not newly introduced contracts.

## Per-file source evidence

Generated from the baseline source declarations to support phase-2 extraction. Fields and operations below are examples from each actual file, not a substitute for reading its implementation. Cross-file call dependencies are listed in the scenario map above.

| File | Declared state (sample) | Methods / entry points (sample) | Subscription or cleanup references (sample) |
|---|---|---|---|
| `MainPage.About.cs` | `_applicationVersionProvider`, `_updateCheckService`, `_applicationUpdateInstaller`, `_systemUriLauncher`, `_applicationUpdateInProgress` | `About_Click`, `ShowAboutAsync`, `RunApplicationUpdateAsync`, `ShowUpdateMessageAsync` | `releaseButton.Click += (_, _) =>`, `updateButton.Click += (_, _) => RunPageUiOperation(async () =>`, `dialog.Opened += Dialog_Opened;`, `dialog.Closed += Dialog_Closed;`, `dialog.Opened -= Dialog_Opened;`, `dialog.Closed -= Dialog_Closed;`, `progressDialog.CloseButtonClick += (_, args) =>`, `progressDialog.Closed += (_, _) => dialogOpen = false;` |
| `MainPage.AuthorAvatars.cs` | `_authorAvatarService`, `_authorAvatarSettings` | — | — |
| `MainPage.BranchDeletion.cs` | — | `RepositoryTreeBranchDeletion_RightTapped`, `UnsetBranchUpstreamAsync`, `ConfirmRemoveRemoteAsync`, `ConfirmCheckoutTagDetachedAsync`, `ConfirmDeleteLocalBranchAsync`, `ConfirmDeleteRemoteBranchAsync` | `deleteLocalCheckBox.Checked += (_, _) => forceCheckBox.IsEnabled `, `deleteLocalCheckBox.Unchecked += (_, _) =>` |
| `MainPage.BranchDragDrop.cs` | `_branchDragSource` | `InitializeBranchDragDrop`, `RepositoryTree_DragItemsStarting`, `RepositoryTree_DragOver`, `CanAcceptBranchDrop`, `RepositoryTree_Drop`, `RepositoryTree_DropCoreAsync`, `RepositoryTree_DragItemsCompleted`, `ResolveBranchDropTarget` | `RepositoryTree.DragItemsStarting -= RepositoryTree_DragItemsStart`, `RepositoryTree.DragItemsStarting += RepositoryTree_DragItemsStart`, `RepositoryTree.DragOver -= RepositoryTree_DragOver;`, `RepositoryTree.DragOver += RepositoryTree_DragOver;`, `RepositoryTree.Drop -= RepositoryTree_Drop;`, `RepositoryTree.Drop += RepositoryTree_Drop;`, `RepositoryTree.DragItemsCompleted -= RepositoryTree_DragItemsComp`, `RepositoryTree.DragItemsCompleted += RepositoryTree_DragItemsComp` |
| `MainPage.BranchFolderDeletion.cs` | — | `ConfirmDeleteBranchFolderAsync`, `ConfirmDeleteLocalBranchFolderAsync`, `ConfirmDeleteRemoteBranchFolderAsync`, `CollectBranchFolderCandidates`, `ShowNoEligibleLocalBranchesAsync`, `ShowBranchFolderDeletionFailuresAsync`, `CreateBoundedBranchList`, `AddBranchLine` | — |
| `MainPage.BranchRename.cs` | — | `InitializeBranchRename`, `CanRenameBranch`, `RepositoryTree_KeyDown`, `RenameBranchAsync`, `ExecuteBranchRenameAsync` | `RepositoryTree.KeyDown -= RepositoryTree_KeyDown;`, `RepositoryTree.KeyDown += RepositoryTree_KeyDown;`, `nameBox.TextChanged += (_, _) => UpdatePrimaryButton();` |
| `MainPage.Changes.cs` | `_changedFileTreeRoots`, `_changesSurfaceInitialized`, `_changedFileTreeRefreshQueued` | `ChangesSurface_Loaded`, `DetailsTabs_SelectionChanged`, `UpdateChangesViewActivity`, `CommitFiles_CollectionChanged`, `ChangesViewModel_PropertyChanged`, `RebuildChangedFileTree`, `RebuildCompactDiff`, `EnsureCurrentCommitFileSelection` | `_commitFiles.CollectionChanged += CommitFiles_CollectionChanged;`, `_viewModel.CommitDetails.PropertyChanged += ChangesViewModel_Prop` |
| `MainPage.CommitActions.cs` | `_commitActionsFlyout`, `_copyHashItem`, `_createBranchHereItem`, `_checkoutCommitItem`, `_cherryPickItem`, `_revertItem`, `_editCommitMessageItem`, `_fixupIntoPreviousCommitItem` | `InitializeCommitActions`, `HistoryList_RightTapped`, `UpdateCommitActionAvailability`, `CopyCommitHash_Click`, `CreateBranchHere_Click`, `CreateBranchHereAsync`, `CreateBranchFromReferenceAsync`, `CheckoutCommit_Click` | `_copyHashItem.Click += CopyCommitHash_Click;`, `_createBranchHereItem.Click += CreateBranchHere_Click;`, `_checkoutCommitItem.Click += CheckoutCommit_Click;`, `_cherryPickItem.Click += CherryPickCommit_Click;`, `_revertItem.Click += RevertCommit_Click;`, `_editCommitMessageItem.Click += EditCommitMessage_Click;`, `_fixupIntoPreviousCommitItem.Click += FixupIntoPreviousCommit_Cli`, `_interactiveRebaseFromHereItem.Click += InteractiveRebaseFromHere` |
| `MainPage.CommitCopy.cs` | `_commitDetailsSurfaceInitialized`, `_commitDetailsView` | `InitializeCommitDetailsSurface`, `QueueCommitDetailsLayout`, `DetailsScroller_SizeChanged`, `ConstrainCommitDetailsToViewport` | `DetailsScroller.SizeChanged += DetailsScroller_SizeChanged;`, `HistoryPane.SizeChanged += DetailsScroller_SizeChanged;`, `XamlRoot.Changed += (_, _) => QueueCommitDetailsLayout();` |
| `MainPage.CommitGraphLayout.cs` | `_historyGraphLayout`, `_commitGraphLayoutInitialized`, `_commitGraphLayoutApplyQueued` | `HistoryColumnHeader_Loaded`, `InitializeGraphLayout`, `HistoryRows_CollectionChanged`, `UpdateGraphLayout`, `QueueActiveCommitGraphLayout`, `ApplyActiveCommitGraphLayout` | `_viewModel.History.Rows.CollectionChanged += HistoryRows_Collecti` |
| `MainPage.ConfirmationDialogs.cs` | `_confirmationDialogsInitialized`, `_discardConfirmationOpen`, `_emptyIndexConfirmationOpen` | `InitializeConfirmationDialogs`, `CommitCreationViewModel_PropertyChanged`, `WorkingTreeConfirmation_PropertyChanged`, `ShowDiscardConfirmationAsync`, `ShowEmptyIndexChoiceAsync` | `_viewModel.CommitCreation.PropertyChanged += CommitCreationViewMo`, `_viewModel.WorkingTree.PropertyChanged += WorkingTreeConfirmation` |
| `MainPage.Dialogs.cs` | `_pageDialogGate`, `_pageDialogLifetime` | `ShowPageDialogAsync`, `ShowInteractiveRebaseEditorAsync`, `GetPageDialog` | `dialog.Opened += Dialog_Opened;`, `dialog.Opened -= Dialog_Opened;` |
| `MainPage.DiffPresentation.cs` | — | `SetCommitDiffPresentationState`, `SetWorkingTreeDiffPresentationState`, `ConfigureInfoBar` | — |
| `MainPage.EditCommitMessage.cs` | `_pendingEditedCommitHash`, `_pendingOriginalEditedCommitHash` | `EditCommitMessage_Click`, `EditCommitMessage_ClickCoreAsync`, `RestorePendingEditedCommitSelectionAsync`, `ClearPendingEditedCommitSelection` | `messageBox.TextChanged += (_, _) => UpdatePrimaryButton();` |
| `MainPage.FileOpening.cs` | `_fileVersionService`, `_externalGitToolService`, `_fileActions` | `InitializeFileOpening`, `FileActions_PropertyChanged`, `FileOpeningViewModel_PropertyChanged`, `WorkingTreeFileOpening_PropertyChanged`, `CaptureCommitRequest`, `CaptureWorkingTreeRequest`, `RefreshCommitFileActionStateAsync`, `RefreshWorkingTreeFileActionStateAsync` | `ChangedFilesTree.RightTapped += ChangedFilesTree_RightTapped;`, `ChangedFilesTree.DoubleTapped += ChangedFilesTree_DoubleTapped;`, `UnstagedChangesList.RightTapped += WorkingTreeChanges_RightTapped`, `StagedChangesList.RightTapped += WorkingTreeChanges_RightTapped;`, `UnstagedChangesList.DoubleTapped += WorkingTreeChanges_DoubleTapp`, `StagedChangesList.DoubleTapped += WorkingTreeChanges_DoubleTapped`, `_viewModel.PropertyChanged += FileOpeningViewModel_PropertyChange`, `_viewModel.CommitDetails.PropertyChanged += FileOpeningViewModel_` |
| `MainPage.ForcePush.cs` | — | `PushFromUiAsync`, `OfferChoosePublishTargetAsync`, `ShowPublishBranchDialogAsync`, `ShowPushToDialogAsync`, `ShowPushTargetDialogAsync`, `RunTargetPushAsync`, `PushTargetDialogOptions`, `PushTargetDialogResult` | `remoteCombo.SelectionChanged += (_, _) => UpdatePrimaryState();`, `remoteBranchBox.TextChanged += (_, _) => UpdatePrimaryState();` |
| `MainPage.GitCommandFeedback.cs` | `_commandFeedback`, `_commandFeedbackTimer`, `_floatingFeedbackView`, `_cloneFeedbackView`, `_cloneDialogFeedbackActive` | `InitializeGitCommandFeedback`, `GitCommandFeedbackSettingsChanged`, `ApplyGitCommandFeedbackSettings`, `RouteGitCommandFeedback`, `RenderGitCommandFeedback`, `WireGitCommandFeedback`, `CreateCloneGitCommandFeedback`, `RemoveFloatingGitCommandFeedback` | `_commandFeedbackTimer.Tick += (_, _) => RenderGitCommandFeedback(`, `_gitConsoleSettings.Changed += GitCommandFeedbackSettingsChanged;`, `view.AutoCloseCancellationRequested += id =>`, `view.DismissRequested += id =>`, `view.OpenConsoleRequested += id =>`, `view.InteractionChanged += (hovered, focused) =>`, `_gitConsoleSettings.Changed -= GitCommandFeedbackSettingsChanged;` |
| `MainPage.GitConsole.cs` | `_gitCommandActivitySource`, `_gitConsoleSettings`, `_gitConsoleView`, `_gitConsoleSplitter`, `_gitCommandStatusButton`, `_gitCommandStatusText`, `_gitConsoleSplitterRow`, `_gitConsoleRow` | `InitializeGitConsole`, `BuildGitConsoleLayout`, `GitCommandActivitySource_Changed`, `ApplyGitCommandLifecycleChange`, `ShouldUpdateGitCommandStatus`, `RebuildGitConsole`, `UpdateGitCommandStatus`, `ToggleGitConsoleFromStatus` | `_gitCommandActivitySource.Changed += GitCommandActivitySource_Cha`, `RootLayout.KeyDown += GitConsole_KeyDown;`, `_gitConsoleView.CloseRequested += (_, _) => CloseGitConsole();`, `_gitConsoleView.FilterChanged += (_, _) => RebuildGitConsole();`, `_gitCommandStatusButton.Click += (_, _) => ToggleGitConsoleFromSt`, `_gitCommandActivitySource.Changed -= GitCommandActivitySource_Cha`, `RootLayout.KeyDown -= GitConsole_KeyDown;` |
| `MainPage.HistoryDisplayMode.cs` | `_syncingHistoryDisplayMode` | `HistoryDisplayModeSelector_Loaded`, `HistoryDisplayModeSelector_Unloaded`, `HistoryDisplayModeViewModel_PropertyChanged`, `HistoryDisplayModeSelector_SelectionChanged`, `SyncHistoryDisplayModeSelector` | `_viewModel.History.PropertyChanged += HistoryDisplayModeViewModel`, `_viewModel.History.PropertyChanged -= HistoryDisplayModeViewModel` |
| `MainPage.HistoryInfiniteScroll.cs` | `_historyScrollViewer`, `_isInfiniteHistoryLoading` | `InitializeHistoryInfiniteScroll`, `HistoryList_Loaded`, `AttachHistoryScrollViewer`, `HistoryScrollViewer_ViewChanged`, `HistoryScrollViewer_ViewChangedCoreAsync`, `FindHistoryScrollViewer` | `HistoryList.Loaded -= HistoryList_Loaded;`, `HistoryList.Loaded += HistoryList_Loaded;`, `_historyScrollViewer.ViewChanged -= HistoryScrollViewer_ViewChang`, `_historyScrollViewer.ViewChanged += HistoryScrollViewer_ViewChang` |
| `MainPage.HistoryPerformanceDiagnostics.cs` | `_historyPerformanceStatusTimer`, `_historyPerformanceTimeoutTimer`, `_historyPerformanceSubscriptionsAttached` | `InitializeHistoryPerformanceDiagnostics`, `StartHistoryPerformanceCapture_Click`, `StartHistoryPerformanceCaptureAsync`, `StopHistoryPerformanceCapture_Click`, `OpenHistoryPerformanceDiagnosticsFolder_Click`, `OpenHistoryPerformanceDiagnosticsFolderAsync`, `AttachHistoryPerformanceSubscriptions`, `DetachHistoryPerformanceSubscriptions` | `HistoryList.ContainerContentChanging += HistoryList_PerformanceCo`, `HistoryList.SelectionChanged += HistoryList_PerformanceSelectionC`, `RootLayout.SizeChanged += RootLayout_PerformanceSizeChanged;`, `HistoryList.ContainerContentChanging -= HistoryList_PerformanceCo`, `HistoryList.SelectionChanged -= HistoryList_PerformanceSelectionC`, `RootLayout.SizeChanged -= RootLayout_PerformanceSizeChanged;`, `_historyPerformanceStatusTimer.Tick += HistoryPerformanceStatusTi`, `_historyPerformanceTimeoutTimer.Tick += HistoryPerformanceTimeout` |
| `MainPage.HistoryRewrite.cs` | — | `InstallRepositoryHistoryRewriteMenus`, `RepositoryFilesTree_HistoryRewriteRightTapped`, `RepositoryContentResults_HistoryRewriteRightTapped`, `ShowRepositorySnapshotFileMenuWithHistoryRewrite`, `RemovePathFromRepositoryHistoryAsync`, `InvalidateHistoryRewritePresentation`, `ConfirmPathHistoryRemovalAsync`, `ShowHistoryRewriteSuccessAsync` | `_repositoryFilesTree.RightTapped -= RepositoryFilesTree_RightTapp`, `_repositoryFilesTree.RightTapped += RepositoryFilesTree_HistoryRe`, `_repositoryContentResults.RightTapped -= RepositoryContentResults`, `_repositoryContentResults.RightTapped += RepositoryContentResults`, `message += "\n\nThe repository may have been partially rewritten.`, `message += $"\n\n{(failure.DestructivePhaseStarted ? "Safety back` |
| `MainPage.ImageDiff.cs` | `_imageDiffPreviewService`, `_commitImageDiffCts`, `_commitImageDiffGeneration`, `_pendingCommitImageDiff` | `LoadCommitImageDiffAsync`, `ResumeDeferredCommitImageDiffAsync`, `IsCurrentCommitImageDiffRequest`, `CancelCommitImageDiff` | — |
| `MainPage.InteractiveRebaseAuthor.cs` | — | `ChangeInteractiveRebaseAuthor_Click`, `ChangeInteractiveRebaseAuthorAsync`, `ShowChangeAuthorErrorFlyout` | `flyout.Closed += (_, _) =>`, `resetMode.Checked += (_, _) => UpdateAuthorMode();`, `explicitMode.Checked += (_, _) => UpdateAuthorMode();`, `selectedScope.Checked += (_, _) => UpdateScopeInfo();`, `allScope.Checked += (_, _) => UpdateScopeInfo();`, `cancel.Click += (_, _) => flyout.Hide();`, `apply.Click += (_, _) =>` |
| `MainPage.Lifecycle.cs` | `_shutdownStarted`, `_shutdownFinalized` | `MainPage_Loaded`, `PrepareForShutdown`, `BeginShutdown` | `Loaded -= RunDesktopCheckWhenRequested;`, `_viewModel.WorkingTree.Changes.CollectionChanged -= RepositoryPre`, `_viewModel.Branches.LocalBranches.CollectionChanged -= Repository`, `_viewModel.Branches.RemoteBranches.CollectionChanged -= Repositor`, `_viewModel.RepositorySync.Remotes.CollectionChanged -= Repository`, `_viewModel.Tags.Items.CollectionChanged -= RepositoryPresentation`, `_viewModel.Stashes.Items.CollectionChanged -= RepositoryPresentat`, `_viewModel.PropertyChanged -= ViewModel_PropertyChanged;` |
| `MainPage.LoadingOverlays.cs` | `_loadingOverlaysInitialized`, `_changesLoadingOverlay`, `_diffLoadingOverlay`, `_changesLoadingRing`, `_diffLoadingRing` | `InitializeLoadingOverlays`, `CreateLoadingOverlay`, `LoadingOverlayViewModel_PropertyChanged`, `UpdateLoadingOverlays`, `DetachLoadingOverlays` | `_viewModel.CommitDetails.PropertyChanged += LoadingOverlayViewMod`, `_viewModel.CommitDetails.PropertyChanged -= LoadingOverlayViewMod` |
| `MainPage.RecentRepositories.cs` | `_recentRepositorySettings`, `_recentRepositoryFolderPicker`, `_repositoryImageService`, `_recentRepositoriesView`, `_recentRepositoriesViewModel`, `_recentRepositoryWasBusy`, `_lastRecordedRecentRepositoryPath`, `_recentRepositoriesShutdown` | `InitializeRecentRepositories`, `RecentRepositoryHost_PropertyChanged`, `RecentRepositorySettings_Changed`, `UpdateStartScreenVisibility`, `OpenRecentRepositoryAsync`, `OpenRecentRepositoryFolderAsync`, `OpenRepositoryPickerAsync`, `UpdateRecentRepositoryBranchMetadataAsync` | `_viewModel.PropertyChanged += RecentRepositoryHost_PropertyChange`, `_recentRepositorySettings.Changed += RecentRepositorySettings_Cha`, `Unloaded += RecentRepositories_Unloaded;`, `_viewModel.PropertyChanged -= RecentRepositoryHost_PropertyChange`, `_recentRepositorySettings.Changed -= RecentRepositorySettings_Cha`, `Unloaded -= RecentRepositories_Unloaded;` |
| `MainPage.RefreshIndicator.cs` | — | `RefreshIndicator_Click`, `RefreshIndicatorAsync` | — |
| `MainPage.RepositoryCloning.cs` | `_cloneRepositoryViewModel`, `_repositoryCloningWorkflowActive`, `_cloneWorkflowAdmission` | `CloneRepository_Click`, `CloneRepository_ClickCoreAsync`, `ShowCloneRepositoryAsync`, `ShowCloneRepositoryDialogAsync`, `ShowRepositoryCloneMessageAsync` | `repositoryUrlBox.TextChanged += (_, _) =>`, `localDirectoryBox.TextChanged += (_, _) =>`, `browseButton.Click += (_, _) =>`, `cancelButton.Click += (_, _) =>`, `cloneButton.Click += (_, _) =>`, `dialog.Closing += (_, args) =>`, `_cloneRepositoryViewModel.PropertyChanged += changed;`, `_cloneRepositoryViewModel.PropertyChanged -= changed;` |
| `MainPage.RepositoryCreation.cs` | `_createRepositoryViewModel`, `_repositoryCreationWorkflowActive` | `CreateRepository_Click`, `CreateRepository_ClickCoreAsync`, `ShowCreateRepositoryAsync`, `ShowCreateRepositoryDialogAsync`, `ShowRepositoryCreationMessageAsync` | `directoryBox.TextChanged += (_, _) =>`, `browseButton.Click += (_, _) =>`, `personal.Checked += (_, _) =>`, `central.Checked += (_, _) =>`, `_createRepositoryViewModel.PropertyChanged += creationChanged;`, `_viewModel.PropertyChanged += repositoryChanged;`, `_createRepositoryViewModel.PropertyChanged -= creationChanged;`, `_viewModel.PropertyChanged -= repositoryChanged;` |
| `MainPage.RepositoryFilePreview.cs` | `_repositoryFilePreviewService`, `_repositoryPreviewGate`, `_repositoryFilePreviewHost`, `_pendingRepositoryPreviewSelection` | `ApplyRepositoryFileSelection`, `ClearRepositoryFileSelection`, `CancelRepositoryFilePreview`, `LoadRepositoryFilePreviewAsync`, `ResumeDeferredRepositoryPreviewAsync`, `CanPublishRepositoryPreview`, `RepositoryIdentity`, `RepositoryFileSelection` | — |
| `MainPage.RepositoryFiles.cs` | `_repositoryFilesViewModel`, `_repositoryFilesTab`, `_changesTab`, `_repositoryFilesSurface`, `_repositoryFilesSearch`, `_repositoryFilesSearchMode`, `_repositoryFilesTree`, `_repositoryContentResults` | `InitializeRepositoryFiles`, `BuildRepositoryFilesSurface`, `RepositoryFilesViewModel_PropertyChanged`, `UpdateRepositoryFilesViewActivity`, `ActivateRepositoryFilesAsync`, `SynchronizeRepositoryFilesSearchControls`, `SynchronizeRepositoryFilesTreeSelection`, `ApplyRepositoryFileSelectionFromViewModel` | `CommitFilesList.DoubleTapped -= CommitFilesList_DoubleTapped;`, `CommitFilesList.KeyDown -= CommitFilesList_KeyDown;`, `CommitFilesList.DoubleTapped += RepositoryChangedFiles_DoubleTapp`, `CommitFilesList.KeyDown += RepositoryChangedFiles_KeyDown;`, `_repositoryFilesViewModel.PropertyChanged += RepositoryFilesViewM`, `_repositoryFilesSurface.SearchBox.TextChanged += RepositoryFilesS`, `_repositoryFilesSurface.SearchBox.KeyDown += RepositoryFilesSearc`, `_repositoryFilesSurface.SearchMode.SelectionChanged += Repository` |
| `MainPage.RepositoryMaintenance.cs` | `_repositoryMaintenance`, `_desktopShellService` | `RepositoryMaintenance_PropertyChanged`, `RepositoryMaintenanceViewModel_PropertyChanged`, `UpdateOptimizeRepositoryAvailability`, `OptimizeRepository_Click`, `OptimizeRepositoryAsync` | `_repositoryMaintenance.PropertyChanged += RepositoryMaintenance_P`, `_viewModel.PropertyChanged += RepositoryMaintenanceViewModel_Prop` |
| `MainPage.RepositoryNavigation.cs` | — | `NavigateToReferenceAsync`, `ResumePendingRepositoryTreeNavigationAsync`, `ResumePendingRepositoryTreeCheckoutAsync`, `SwitchRepositoryTreeBranchAsync`, `ShowReferenceHistoryAsync`, `ShowAllHistoryAsync`, `ShowWorkingTree`, `UpdateCommitNavigationText` | `_viewModel.PropertyChanged -= UpdateAvailability;`, `flyout.Closed -= Detach;`, `_viewModel.PropertyChanged += UpdateAvailability;`, `flyout.Closed += Detach;`, `item.Click += (_, _) => RunPageUiOperation(action, "Menu action",` |
| `MainPage.RepositoryRefresh.cs` | `_repositoryChangeCoordinator`, `_repositoryChangeMonitoringInitialized`, `_isRefreshInProgress`, `_repositoryPresentationRefreshQueued`, `_repositoryTreePresentationDirty`, `_workingTreePresentationDirty` | `InitializeRepositoryChangeMonitoring`, `ShutdownRepositoryChangeMonitoring`, `RepositoryRefreshTracking_PropertyChanged`, `RepositoryStateRefreshStarting`, `RepositoryStateRefreshCompleted`, `UpdateRepositoryChangeMonitor`, `CapturePendingMonitorDeliveryForDesktopCheck`, `SetRefreshInProgress` | `_repositoryChangeCoordinator.RefreshRequirementChanged += UpdateR`, `_viewModel.PropertyChanged += RepositoryRefreshTracking_PropertyC`, `_viewModel.RepositoryStateRefreshStarting += RepositoryStateRefre`, `_viewModel.RepositoryStateRefreshCompleted += RepositoryStateRefr`, `_viewModel.PropertyChanged -= RepositoryRefreshTracking_PropertyC`, `_viewModel.RepositoryStateRefreshStarting -= RepositoryStateRefre`, `_viewModel.RepositoryStateRefreshCompleted -= RepositoryStateRefr`, `_repositoryChangeCoordinator.RefreshRequirementChanged -= UpdateR` |
| `MainPage.RepositorySwitching.cs` | `_repositorySelectorItems` | `RepositorySelectorEntry`, `InitializeRepositorySwitching`, `UpdateRepositorySelectorPresentation`, `RepositorySelectorFlyout_Opening`, `RepositorySelectorList_ItemClick`, `RepositorySelectorList_ItemClickCoreAsync`, `RepositorySelectorOpenFolder_Click`, `RepositorySelectorOpenFolder_ClickCoreAsync` | — |
| `MainPage.RepositoryTreeState.cs` | `_repositoryTreeStateTrackingAttached`, `_repositoryTreeSynchronizer` | `RepositoryTreeState_Loaded`, `TryAttachRepositoryTreeStateTracking`, `RepositoryTree_Expanding`, `RepositoryTree_Collapsed`, `RepositoryTreeViewModel_PropertyChanged`, `ResetRepositoryTreeForLifecycle`, `SynchronizeRepositoryTree`, `SynchronizeWorktreePresentation` | `Loaded -= RepositoryTreeState_Loaded;`, `Loaded += RepositoryTreeState_Loaded;`, `RepositoryTree.Expanding += RepositoryTree_Expanding;`, `RepositoryTree.Collapsed += RepositoryTree_Collapsed;`, `_viewModel.PropertyChanged += RepositoryTreeViewModel_PropertyCha`, `RepositoryTree.Expanding -= RepositoryTree_Expanding;`, `RepositoryTree.Collapsed -= RepositoryTree_Collapsed;`, `_viewModel.PropertyChanged -= RepositoryTreeViewModel_PropertyCha` |
| `MainPage.Settings.cs` | `_settingsWindowController` | `Settings_Click`, `RepositorySettings_Click`, `PullSettings_Click`, `OpenSettingsWindow` | — |
| `MainPage.Stashes.cs` | — | `CreateStash_Click`, `ShowCreateStashDialogAsync`, `ShowCreateSelectedStashDialogAsync`, `DropSelectedStash_Click`, `ConfirmDropStashAsync` | `allTracked.Checked += (_, _) => UpdateState();`, `stagedOnly.Checked += (_, _) => UpdateState();`, `includeUntracked.Checked += (_, _) => UpdateState();`, `includeUntracked.Unchecked += (_, _) => UpdateState();` |
| `MainPage.Tags.cs` | `_tagSupportInitialized`, `_createTagHereItem`, `_deleteTagSubItem` | `InitializeTagSupportIfNeeded`, `UpdateDeleteTagSubmenu`, `ApplyTagOrderingToRepositoryTree`, `TryShowTagContextMenu`, `CanMutateTags`, `ShowCreateTagDialogAsync`, `ShowTagDetailsAsync`, `DeleteTagFromUiAsync` | `_createTagHereItem.Click += (_, _) =>`, `_commitActionsFlyout.Opening += (_, _) =>`, `item.Click += (_, _) =>`, `type.SelectionChanged += (_, _) => message.IsEnabled = type.Selec`, `deleteRemote.Checked += (_, _) => UpdateDeleteAvailability();`, `deleteRemote.Unchecked += (_, _) => UpdateDeleteAvailability();`, `remoteSelector.SelectionChanged += (_, _) => UpdateDeleteAvailabi` |
| `MainPage.WorkingTreeContextMenu.cs` | — | `UnstagedChangesTree_RightTapped`, `StagedChangesTree_RightTapped`, `ShowWorkingTreeContextMenu`, `AddWorkingTreeFileContextMenuItems`, `AddWorkingTreeFolderContextMenuItems`, `SelectWorkingTreeContextTarget`, `ConfirmAddFolderToGitIgnoreAsync`, `CanDiscardStagedFile` | — |
| `MainPage.WorkingTreeDiff.cs` | `_unstagedTreeRoots`, `_stagedTreeRoots`, `_unstagedTreeSelection`, `_stagedTreeSelection`, `_unstagedExpansionState`, `_stagedExpansionState`, `_workingTreeImageDiffCts`, `_workingTreeImageDiffGeneration` | `InitializeWorkingTreeDiffSurface`, `WorkingTreePresentationSourceChanged`, `ScheduleWorkingTreeTreeRefreshIfNeeded`, `RebuildWorkingTreeTrees`, `ReplaceRoots`, `UnstagedChangesTree_ItemInvoked`, `StagedChangesTree_ItemInvoked`, `WorkingTreeNodeInvoked` | `_unstagedChanges.CollectionChanged += WorkingTreePresentationSour`, `_stagedChanges.CollectionChanged += WorkingTreePresentationSource`, `_viewModel.WorkingTree.PropertyChanged += WorkingTreeViewModel_Pr` |
| `MainPage.WorktreeContextMenu.cs` | — | `ShowWorktreeContextMenu` | — |
| `MainPage.Worktrees.cs` | `_worktreesViewModel`, `_worktreeShutdown` | `InitializeWorktreeSupport`, `ShutdownWorktreeSupport`, `OpenInitialRepositoryAsync`, `WorktreesViewModel_PropertyChanged`, `FindWorktreeForBranch`, `CreateWorktreeFromBranchAsync`, `CreateNewWorktreeAsync`, `PopulateWorktreeMenu` | `_worktreesViewModel.PropertyChanged += WorktreesViewModel_Propert`, `_worktreesViewModel.PropertyChanged -= WorktreesViewModel_Propert`, `branch.TextChanged += (_, _) =>`, `directory.TextChanged += (_, _) =>` |
| `MainPage.xaml.cs` | `_viewModel`, `_repositoryTreeRoots`, `_unstagedChanges`, `_stagedChanges`, `_commitFiles`, `_wasBusy`, `_repositoryTreeNavigation`, `_busyPulseStoryboard` | `RepositoryPresentationChanges_CollectionChanged`, `RepositoryPresentationLocalBranches_CollectionChanged`, `RepositoryPresentationRemoteBranches_CollectionChanged`, `RepositoryPresentationRemotes_CollectionChanged`, `RepositoryPresentationTags_CollectionChanged`, `RepositoryPresentationStashes_CollectionChanged`, `ConfirmCloseAsync`, `ViewModel_PropertyChanged` | `_viewModel.PropertyChanged += ViewModel_PropertyChanged;`, `_viewModel.Stashes.PropertyChanged += StashesViewModel_PropertyCh`, `_viewModel.CommitDetails.PropertyChanged += CommitDetailsViewMode`, `_viewModel.History.PropertyChanged += HistoryViewModel_PropertyCh`, `_viewModel.History.CommitLookupCompleted += History_CommitLookupC`, `_viewModel.WorkingTree.Changes.CollectionChanged += RepositoryPre`, `_viewModel.Branches.LocalBranches.CollectionChanged += Repository`, `_viewModel.Branches.RemoteBranches.CollectionChanged += Repositor` |

## Phase 3 follow-up: toolbar routing

Typed repository-toolbar actions now call their existing workflows directly; no synthetic `RoutedEventArgs` are constructed by `MainPage`. F5 and the toolbar refresh indicator share `RepositoryManualRefreshController`. This is a partial Phase 3 step; feature views and window lifetime still require extraction.

## Phase 3 follow-up: repository push workflow

Push, Push To, Publish Branch and Force Push with Lease operations and dialogs are owned by `Workflows/RepositoryPushController`; `RepositorySyncViewModel` retains Git semantics. `MainPage.ForcePush.cs` was removed. Remaining feature views and window composition are not yet migrated.

## Phase 3 follow-up: window lifetime bridge

The temporary `IWindowContentLifetime` interface has been removed. `WindowSessionFactory` supplies explicit preparation and completion callbacks to `WindowSession`; session idempotency, close protection and DI-scope disposal remain unchanged. Remaining feature-owned subscriptions and shutdown cleanup still need to migrate out of `MainPage` before Phase 3 can be declared complete.

## Phase 3 follow-up: worktree context menu ownership

The worktree attached right-tap behavior now resolves `RepositoryExplorerView` rather than `MainPage`; the explorer owns placement and routes menu actions to the scoped `WorktreeController`. `MainPage.WorktreeContextMenu.cs` was removed. The behavior no longer depends on the application's root page.

## Phase 3 follow-up: branch deletion ownership

Local/remote branch deletion (including the optional paired remote/local deletion) and branch-folder deletion now share `Workflows/BranchDeletionController`. `BranchesViewModel` retains mutation semantics; `MainPage.BranchDeletion.cs` contains only the remaining reference context-menu, tag checkout and remote removal UI, which must still migrate to the owning explorer views.

## Phase 3 follow-up: working-tree visual composition

The staging trees, working-tree diff surface, buttons and commit editor are now visually owned by `Views/WorkingTreeView.xaml` instead of being declared inline in `MainPage.xaml`. The parent page retains temporary typed event callbacks and control facades for the existing workflow controllers. This is a staging step, not completion of the required feature-owned state/handler migration; remove the facades during the next stage.

## Phase 3 follow-up: tag workflow ownership

Tag creation, deletion, remote selection, fetch/push and remote-tag dialogs now belong to `Workflows/TagWorkflowController`, with mutation semantics remaining in `TagsViewModel`. `MainPage.Tags.cs` retains only tag menu composition and the shared information dialog; full reference-menu ownership still needs to move to `RepositoryExplorerView`.
