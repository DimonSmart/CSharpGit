# Window-session lifetime audit (phase 1)

Baseline: `e64f849`. Reviewed production registrations in `App.xaml.cs`.

| Dependency | Registration | Ownership and release |
|---|---|---|
| `OpenRepositoryViewModel` | Transient, created inside the window scope | Scope; cascaded feature cleanup is idempotent |
| `CreateRepositoryViewModel`, `CloneRepositoryViewModel` | Transient inside window scope | Scope, after UI event handlers are detached |
| `RepositoryMaintenanceViewModel`, `RepositoryFilesViewModel`, `FileActionsViewModel` | Transient inside window scope | Scope; legacy MainPage no longer calls their Dispose |
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
4. Legacy `IWindowContentLifetime` cancels page-owned operations and diagnostics. Diagnostics use the existing three-second budget; active Git mutation never uses this timeout.
5. The page unsubscribes its own callbacks, then the window scope disposes transient models once; the host remains alive until the native close sequence allows shutdown.
6. On macOS host stop precedes final native close; other platforms preserve the existing native close path.

## Remaining migration

- Phase 2: replace remaining direct App-to-MainPage title/layout and desktop-check dependencies with focused window-shell interfaces; move repository/history/file UI components.
- Phase 3: remove the temporary `IWindowContentLifetime` bridge, all legacy `MainPage.*.cs` partial files and the transitional factory reference to `MainPage`.
- Native desktop close checks on Windows, macOS and Linux need explicit execution; ordinary CI unit test success is not evidence that native close smoke tests ran.
