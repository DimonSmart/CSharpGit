# UI composition and component ownership

## Target hierarchy

```text
App (host, global services, native application events)
└─ WindowSessionFactory (one DI scope per window)
   └─ WindowSession (initialization, shutdown, session lifetime)
      ├─ WindowCloseOrchestrator → WindowCloseCoordinator (existing state machine)
      ├─ WindowCloseSafetyProvider → operation owners (read-only status)
      ├─ OpenRepositoryViewModel (repository state and Git work)
      └─ WindowWorkspaceComposition → MainPage.xaml (generated visual root)
         ├─ RepositoryLauncherView / RepositorySelectorView
         ├─ RepositoryTreeView
         ├─ HistoryView → CommitDetailsView
         ├─ WorkingTreeView → DiffView
         ├─ RepositoryFilesView → PreviewView
         ├─ GitConsoleView
         └─ Feature dialogs
```

## Binding and control conventions

- `*View.xaml`: visual composition, local UI events and bindings. Use Uno-generated code-behind when no custom construction or event handler is needed.
- `*ViewModel.cs`: observable feature state and commands. Existing feature models remain authoritative; do not manufacture one model per view.
- `*Coordinator.cs`: one cross-feature interaction with explicit inputs and no access to arbitrary UI controls.
- `*Dialog.cs`: a specific interaction; share the existing ContentDialog serialization gate, preserve the owning window's XamlRoot.
- `*Behavior.cs`: small UI-only event adapter when ICommand binding is inadequate.
- `*Service.cs`: focused domain or infrastructure operation, not a global UI service container.

## Dependency direction

View → own ViewModel or focused UI controller; coordinator → feature contracts/models; session → window-owned components; App → session factory. Feature ViewModels must not reference XAML types, MainPage or IServiceProvider. Views must not access each other's private fields.

## Lifetime policy

- App owns IHost and singleton cross-window services.
- Window factory owns IServiceScope and resolves the window-local composition and session dependencies from it. The composition creates the generated XAML root.
- Session owns one-way Created → Active → PreparingForShutdown → Disposed state transitions, never runs Git commands itself.
- The UI-independent WindowWorkspaceLifetime in Presentation.Core runs the explicitly registered prepare and completion callbacks; each feature owns its subscriptions, and the DI scope owns resolved models. Do not rely on multiple owners disposing the same feature object.
- Session shutdown blocks new UI work, cancels previews and bounded diagnostics, preserves running protected Git operations by rejecting close at admission, then disposes UI resources before releasing scope.
- Window-session events must be unregistered before scope disposal; callbacks after shutdown cannot publish to controls.
- The temporary IWindowContentLifetime bridge is removed.

## Phase acceptance and migration guardrails

Phase 1 preserves existing XAML and behavior. Phase 2 separates repository/tree/history/working-tree/file views. Phase 3 removes every authored MainPage partial and the compatibility adapter; the architecture contract enforces a declarative root. Verify real desktop behavior and all supported platforms before accepting the phase. No event bus, global services bag, new functional MainPage partials, or extra constructor arguments.

## Final stabilization verification

- MainPage is generated from XAML; no authored root partial, no experimental probe.
- HistoryWorkspaceView declares Summary, Changes and Files tabs; the window composition supplies the feature's scoped dependencies without adding tabs dynamically.
- A single CommitDraftCloseConfirmationDialog is constructed by window composition and shared by native-close admission and restart. Native resize forwards a method-group callback to the layout controller.
- Workspace preparation closes admission before invoking callbacks. Repeat calls share the in-flight task. All preparation callbacks and diagnostics are observed even if one fails. Completion runs every cleanup once, including initialization failure and diagnostic timeout cases.
- Build workflow runs the production DesktopApplicationTests on Windows, macOS and Linux (Xvfb with Openbox). CI sets CSHARPGIT_REQUIRE_DESKTOP_CHECK=1 so an unavailable display fails rather than skips. The desktop smoke covers generated MainPage bindings, repository/worktree opening and switching, actual Files tab activation, working-tree/history navigation, initial graph rendering and resizing, and native close/lifecycle protocol.
- Local headless unit runs may still skip GUI tests. Verify the exact PR head's completed CI and Desktop.Tests results; never count a skip as a pass. Manual accessibility and long-running update/rewrite scenarios are separate from this automated smoke; see doc/Repository workspace lifecycle verification.md.
