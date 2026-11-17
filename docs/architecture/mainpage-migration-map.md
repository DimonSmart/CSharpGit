# MainPage migration map

Baseline: `main` at `e64f849e55112ea53b61209346a9a06e3f0f0c32`. This is an inventory of the existing page boundaries, not authorization to move feature behavior in phase 1.

## Cross-file ownership and migration matrix

| Existing file | Current responsibility / observable state | Planned owner | Phase |
|---|---|---|---|
| `MainPage.xaml` | xaml presentation, commands and/or visual state | Workspace UI composition (phase 2) | 3 |
| `MainPage.xaml.cs` | Composition root for the existing page; collection projections, DataContext, event subscriptions and XAML handlers | Workspace UI composition (phase 2) | 3 |
| `MainPage.Lifecycle.cs` | Page-owned cancellation, unsubscription and disposal; currently also computes close safety | WindowSession + IWindowContentLifetime (temporary page adapter) | 1 |
| `MainPage.LoadingOverlays.cs` | Loading Overlays presentation, commands and/or visual state | Window shell / window session (phase 1), feature view for remaining presentation (phase 2) | 3 |
| `MainPage.RefreshIndicator.cs` | Refresh Indicator presentation, commands and/or visual state | Window shell / window session (phase 1), feature view for remaining presentation (phase 2) | 3 |
| `MainPage.RecentRepositories.cs` | Recent repository selection and launcher | Window shell / window session (phase 1), feature view for remaining presentation (phase 2) | 3 |
| `MainPage.Settings.cs` | Settings UI and controller | Window shell / window session (phase 1), feature view for remaining presentation (phase 2) | 3 |
| `MainPage.About.cs` | About presentation, commands and/or visual state | Window shell / window session (phase 1), feature view for remaining presentation (phase 2) | 3 |
| `MainPage.RepositoryNavigation.cs` | Repository tree navigation, pending checkout and reference selection | RepositoryWorkspace coordinator and repository/navigation views (phase 2) | 2 |
| `MainPage.RepositoryTreeState.cs` | Tree expansion/selection and sync of visual tree | RepositoryWorkspace coordinator and repository/navigation views (phase 2) | 2 |
| `MainPage.RepositoryRefresh.cs` | Repository monitor callbacks and refresh invalidation | RepositoryWorkspace coordinator and repository/navigation views (phase 2) | 2 |
| `MainPage.RepositorySwitching.cs` | Repository switching UI and state transitions | RepositoryWorkspace coordinator and repository/navigation views (phase 2) | 2 |
| `MainPage.RepositoryCreation.cs` | Create workflow and dialog | RepositoryWorkspace coordinator and repository/navigation views (phase 2) | 2 |
| `MainPage.RepositoryCloning.cs` | Clone workflow and dialog | RepositoryWorkspace coordinator and repository/navigation views (phase 2) | 2 |
| `MainPage.RepositoryMaintenance.cs` | Maintenance dialog and progress | RepositoryWorkspace coordinator and repository/navigation views (phase 2) | 2 |
| `MainPage.Worktrees.cs` | Worktrees presentation, commands and/or visual state | RepositoryWorkspace coordinator and repository/navigation views (phase 2) | 2 |
| `MainPage.WorktreeContextMenu.cs` | Worktree Context Menu presentation, commands and/or visual state | RepositoryWorkspace coordinator and repository/navigation views (phase 2) | 2 |
| `MainPage.HistoryDisplayMode.cs` | History Display Mode presentation, commands and/or visual state | HistoryView, CommitDetailsView and related focused coordinators (phase 2) | 2 |
| `MainPage.HistoryInfiniteScroll.cs` | History page loading and scroll state | HistoryView, CommitDetailsView and related focused coordinators (phase 2) | 2 |
| `MainPage.HistoryPerformanceDiagnostics.cs` | Bounded history diagnostic capture shutdown | HistoryView, CommitDetailsView and related focused coordinators (phase 2) | 2 |
| `MainPage.CommitGraphLayout.cs` | History commit graph layout and viewport | HistoryView, CommitDetailsView and related focused coordinators (phase 2) | 2 |
| `MainPage.CommitActions.cs` | Commit Actions presentation, commands and/or visual state | HistoryView, CommitDetailsView and related focused coordinators (phase 2) | 2 |
| `MainPage.CommitCopy.cs` | Commit Copy presentation, commands and/or visual state | HistoryView, CommitDetailsView and related focused coordinators (phase 2) | 2 |
| `MainPage.EditCommitMessage.cs` | Edit Commit Message presentation, commands and/or visual state | HistoryView, CommitDetailsView and related focused coordinators (phase 2) | 2 |
| `MainPage.InteractiveRebaseAuthor.cs` | Interactive Rebase Author presentation, commands and/or visual state | HistoryView, CommitDetailsView and related focused coordinators (phase 2) | 2 |
| `MainPage.HistoryRewrite.cs` | History rewrite UI | HistoryView, CommitDetailsView and related focused coordinators (phase 2) | 2 |
| `MainPage.Changes.cs` | Working tree change projections | RepositoryFilesView, WorkingTreeView and preview controllers (phase 2) | 2 |
| `MainPage.RepositoryFiles.cs` | Repository file browser UI composition and tree events | RepositoryFilesView, WorkingTreeView and preview controllers (phase 2) | 2 |
| `MainPage.RepositoryFilePreview.cs` | Cancelable file preview and publication | RepositoryFilesView, WorkingTreeView and preview controllers (phase 2) | 2 |
| `MainPage.FileOpening.cs` | Open file actions and external tool integration | RepositoryFilesView, WorkingTreeView and preview controllers (phase 2) | 2 |
| `MainPage.DiffPresentation.cs` | Diff Presentation presentation, commands and/or visual state | RepositoryFilesView, WorkingTreeView and preview controllers (phase 2) | 2 |
| `MainPage.ImageDiff.cs` | Image Diff presentation, commands and/or visual state | RepositoryFilesView, WorkingTreeView and preview controllers (phase 2) | 2 |
| `MainPage.WorkingTreeDiff.cs` | Working tree diff and selection | RepositoryFilesView, WorkingTreeView and preview controllers (phase 2) | 2 |
| `MainPage.WorkingTreeContextMenu.cs` | Working Tree Context Menu presentation, commands and/or visual state | RepositoryFilesView, WorkingTreeView and preview controllers (phase 2) | 2 |
| `MainPage.BranchDeletion.cs` | Branch Deletion presentation, commands and/or visual state | ReferencesView and dedicated Git operation controllers (phase 3) | 3 |
| `MainPage.BranchFolderDeletion.cs` | Branch Folder Deletion presentation, commands and/or visual state | ReferencesView and dedicated Git operation controllers (phase 3) | 3 |
| `MainPage.BranchDragDrop.cs` | Branch Drag Drop presentation, commands and/or visual state | ReferencesView and dedicated Git operation controllers (phase 3) | 3 |
| `MainPage.BranchRename.cs` | Branch Rename presentation, commands and/or visual state | ReferencesView and dedicated Git operation controllers (phase 3) | 3 |
| `MainPage.Tags.cs` | Tags presentation, commands and/or visual state | ReferencesView and dedicated Git operation controllers (phase 3) | 3 |
| `MainPage.Stashes.cs` | Stashes presentation, commands and/or visual state | ReferencesView and dedicated Git operation controllers (phase 3) | 3 |
| `MainPage.ForcePush.cs` | Force Push presentation, commands and/or visual state | ReferencesView and dedicated Git operation controllers (phase 3) | 3 |
| `MainPage.GitCommandFeedback.cs` | Git operation status and feedback | ReferencesView and dedicated Git operation controllers (phase 3) | 3 |
| `MainPage.GitConsole.cs` | Console presentation and Git command activity subscriptions | ReferencesView and dedicated Git operation controllers (phase 3) | 3 |
| `MainPage.Dialogs.cs` | Page-level dialogs and dialog gate | Feature-specific dialogs, assets presenter (phase 3) | 3 |
| `MainPage.ConfirmationDialogs.cs` | Destructive operation confirmation UI | Feature-specific dialogs, assets presenter (phase 3) | 3 |
| `MainPage.AuthorAvatars.cs` | Author Avatars presentation, commands and/or visual state | Feature-specific dialogs, assets presenter (phase 3) | 3 |

## Existing high-risk shared scenarios

- **Close:** `App.xaml.cs` owns native close events, coordinator and host stop; `MainPage.Lifecycle.cs` owns cancellation, close-state computation and final resource release. Phase 1 owner: `WindowCloseOrchestrator` using existing `WindowCloseCoordinator`; `WindowSession` owns lifetime; a narrow temporary content adapter retains page cleanup.
- **Repository switch:** `MainPage.RepositorySwitching.cs`, `RepositoryRefresh.cs`, `RepositoryTreeState.cs`, `RepositoryNavigation.cs` and `RepositoryFilePreview.cs`. Phase 2 coordinator owns switch and invalidation; preserve `RepositorySessionLifetime` and `RepositoryChangeCoordinator`.
- **History:** `MainPage.HistoryInfiniteScroll.cs`, `HistoryDisplayMode.cs`, `CommitGraphLayout.cs`, `HistoryPerformanceDiagnostics.cs` and `MainPage.xaml.cs`. Phase 2 owner: HistoryView plus existing HistoryViewModel.
- **Files:** `MainPage.RepositoryFiles.cs`, `RepositoryFilePreview.cs`, `FileOpening.cs`, `WorkingTreeDiff.cs` and `DiffPresentation.cs`. Phase 2 owner: separate file surfaces and preview lifecycle; use existing `RepositoryFilesSurface`.
- **Dialogs and commands:** `MainPage.Dialogs.cs`, `ConfirmationDialogs.cs` and feature files. Each feature owns its dialog; shared serialization remains the existing page dialog gate until extracted.

## Lifetime and subscription hotspots

- `MainPage.xaml.cs`: subscribes to repository, history, branches, tags, remotes, stashes and working-tree collections; uses window-title callback. All corresponding unsubscriptions must be paired in `MainPage.Lifecycle.cs` while the temporary adapter exists.
- `MainPage.Lifecycle.cs`: `PrepareForShutdown` cancels dialogs, previews and repository monitor; `BeginShutdown` unregisters change handlers, shuts down child surfaces, disposes feature models. Do not allow DI scope and page to dispose the same logical feature twice.
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
