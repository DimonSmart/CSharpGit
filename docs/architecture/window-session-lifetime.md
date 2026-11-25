# Window-session lifetime audit (phase 1)

Baseline: `e64f849`. Reviewed production registrations in `App.xaml.cs`.

| Dependency | Registration | Ownership and release |
|---|---|---|
| `OpenRepositoryViewModel` | Scoped | Scope; cascaded feature cleanup is idempotent |
| `CreateRepositoryViewModel`, `CloneRepositoryViewModel` | Scoped | Scope, after UI event handlers are detached |
| `RepositoryMaintenanceViewModel` | Scoped | Scope |
| `RepositoryFilesViewModel`, `FileActionsViewModel` | Transient inside window scope, resolved by scoped dependency group | Scope |
| `WorktreesViewModel`, history, branches, stashes, sync and commit feature ViewModels | Transient inside window scope | Scope; do not register as Singleton |
| `SettingsWindowController` | Scoped | Window scope; page requests Shutdown first |
| `RecentRepositoryFolderPicker`, `IFolderPicker` | Scoped | Window scope; native handle remains an App-provided factory (single-window limitation) |
| `IRepositoryImageService`, `IAuthorAvatarService` | Singleton | App host; image/avatar caching shared |
| `GitCommandActivityHistory` | Singleton | App host; application-wide diagnostics |
| `IAppSettingsService` | Singleton | App host; global settings and notifications |
| `WindowSession` | Factory-created (one per scope) | Window-specific; preparation and completion are idempotent |

## Shutdown order

1. `WindowCloseSafetyProvider` reads live flags and rejects protected Git operations.
2. The existing `WindowCloseCoordinator` accepts or rejects the request and rechecks after commit-draft confirmation.
3. `WindowCloseOrchestrator` begins shutdown after macOS native callback returns, or directly on Windows/Linux.
4. Window-scoped preparation callback cancels page-owned operations and diagnostics. Diagnostics use the existing three-second budget; active Git mutation never uses this timeout.
5. The window composition invokes feature-owned cleanup callbacks, then the window scope disposes its models once; the host remains alive until the native close sequence allows shutdown.
6. On macOS host stop precedes final native close; other platforms preserve the existing native close path.

## Final migration

- The generated MainPage is only the visual root; WindowSession has no concrete page reference. WindowWorkspaceComposition wires feature-owned controls and explicit lifetime callbacks. The temporary IWindowContentLifetime bridge and authored root partials are gone.
- WindowWorkspaceLifetime in Presentation.Core shares the preparation task and aggregates errors without skipping other preparation or completion callbacks. Close admission is still the separate WindowCloseCoordinator. The outer orchestrator preserves the diagnostics shutdown budget.
- The production desktop lifecycle test is mandatory in CI on all three supported OSes. Linux uses Xvfb with Openbox; local headless runs may skip. Check the actual run on the final commit, not a prior green build.
