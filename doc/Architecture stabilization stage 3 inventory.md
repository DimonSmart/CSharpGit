# Architecture Stabilization — stage 3: source ownership inventory

Reference commit: `b274ac0271822dad602ec4bc71551ecaaa77ad43` (2026-10-14).
This inventory records all original direct production-source test inclusions,
without silently assuming a file should be moved just because it was linked.

## Measurements

| Measurement | Before | After |
| --- | ---: | ---: |
| Application.Tests production `Compile Include` entries | 32 | 0 |
| Desktop.Tests production `Compile Include` entries | 64 | 0 |
| Total direct test inclusions | 96 | 0 |
| Unique directly included production files | 91 | 0 |
| Files compiled separately into both test assemblies | 5 | 0 |
| New Uno-free production assemblies | 0 | 2 |

New production owners compile 10 Infrastructure files from the inventory,
81 Presentation files from the inventory, plus the formerly Uno-owned
`DesktopFileServices.cs` (92 files total). The service and algorithm sources
are **linked into the new Core projects at their original physical paths**.
The matching original Uno `.csproj` files explicitly remove them from their
default compile sets. This gives each type a single production assembly owner
while leaving source-contract tests functional. Physical relocation and
removal of the source links is possible as a subsequent pure file-move with
test path resolver updates; the current source path is *not* evidence of a
second compilation owner.

## Inventory

| Baseline physical source path (unchanged) | Previous compiled owner | Current compiled owner | Baseline test consumers |
| --- | --- | --- | --- |
| `src/CSharpGit.Infrastructure/ApplicationUpdateServices.cs` | `CSharpGit.Infrastructure` | `CSharpGit.Infrastructure.Core` | Application.Tests |
| `src/CSharpGit.Infrastructure/AuthorAvatarService.cs` | `CSharpGit.Infrastructure` | `CSharpGit.Infrastructure.Core` | Application.Tests |
| `src/CSharpGit.Infrastructure/ExternalToolProcessService.cs` | `CSharpGit.Infrastructure` | `CSharpGit.Infrastructure.Core` | Application.Tests |
| `src/CSharpGit.Infrastructure/GitHubRepositoryImageProvider.cs` | `CSharpGit.Infrastructure` | `CSharpGit.Infrastructure.Core` | Application.Tests |
| `src/CSharpGit.Infrastructure/JsonAppSettingsService.cs` | `CSharpGit.Infrastructure` | `CSharpGit.Infrastructure.Core` | Application.Tests |
| `src/CSharpGit.Infrastructure/LocalRepositoryImageProvider.cs` | `CSharpGit.Infrastructure` | `CSharpGit.Infrastructure.Core` | Application.Tests |
| `src/CSharpGit.Infrastructure/RepositoryImageInternals.cs` | `CSharpGit.Infrastructure` | `CSharpGit.Infrastructure.Core` | Application.Tests |
| `src/CSharpGit.Infrastructure/RepositoryImageService.Cache.cs` | `CSharpGit.Infrastructure` | `CSharpGit.Infrastructure.Core` | Application.Tests |
| `src/CSharpGit.Infrastructure/RepositoryImageService.Utilities.cs` | `CSharpGit.Infrastructure` | `CSharpGit.Infrastructure.Core` | Application.Tests |
| `src/CSharpGit.Infrastructure/RepositoryImageService.cs` | `CSharpGit.Infrastructure` | `CSharpGit.Infrastructure.Core` | Application.Tests |
| `src/CSharpGit.Presentation/Controls/AuthorAvatarFallback.cs` | `CSharpGit` | `CSharpGit.Presentation.Core` | Desktop.Tests |
| `src/CSharpGit.Presentation/Controls/AuthorAvatarLifecycleState.cs` | `CSharpGit` | `CSharpGit.Presentation.Core` | Desktop.Tests |
| `src/CSharpGit.Presentation/Controls/AuthorAvatarRequestGate.cs` | `CSharpGit` | `CSharpGit.Presentation.Core` | Desktop.Tests |
| `src/CSharpGit.Presentation/Controls/CommitGraph/CommitGraphGeometryBuilder.cs` | `CSharpGit` | `CSharpGit.Presentation.Core` | Desktop.Tests |
| `src/CSharpGit.Presentation/Controls/CommitGraph/CommitGraphGeometryCache.cs` | `CSharpGit` | `CSharpGit.Presentation.Core` | Desktop.Tests |
| `src/CSharpGit.Presentation/Controls/CommitGraph/CommitGraphGeometryKey.cs` | `CSharpGit` | `CSharpGit.Presentation.Core` | Desktop.Tests |
| `src/CSharpGit.Presentation/Controls/CommitGraph/CommitGraphLayoutState.cs` | `CSharpGit` | `CSharpGit.Presentation.Core` | Desktop.Tests |
| `src/CSharpGit.Presentation/Controls/CommitGraph/CommitGraphMetrics.cs` | `CSharpGit` | `CSharpGit.Presentation.Core` | Desktop.Tests |
| `src/CSharpGit.Presentation/Controls/CommitGraph/CommitGraphRowVisual.cs` | `CSharpGit` | `CSharpGit.Presentation.Core` | Desktop.Tests |
| `src/CSharpGit.Presentation/Controls/CommitGraph/CommitGraphTrackPresentation.cs` | `CSharpGit` | `CSharpGit.Presentation.Core` | Desktop.Tests |
| `src/CSharpGit.Presentation/Controls/CommitTimeFormatter.cs` | `CSharpGit` | `CSharpGit.Presentation.Core` | Desktop.Tests |
| `src/CSharpGit.Presentation/Controls/DiffViewportResetGate.cs` | `CSharpGit` | `CSharpGit.Presentation.Core` | Desktop.Tests |
| `src/CSharpGit.Presentation/Controls/GitCommandConsoleItem.cs` | `CSharpGit` | `CSharpGit.Presentation.Core` | Application.Tests |
| `src/CSharpGit.Presentation/Controls/GitCommandConsoleState.cs` | `CSharpGit` | `CSharpGit.Presentation.Core` | Application.Tests |
| `src/CSharpGit.Presentation/Controls/GitConsoleClipboardText.cs` | `CSharpGit` | `CSharpGit.Presentation.Core` | Application.Tests |
| `src/CSharpGit.Presentation/Controls/GitOutputDisplayBuffer.cs` | `CSharpGit` | `CSharpGit.Presentation.Core` | Application.Tests |
| `src/CSharpGit.Presentation/Controls/RepositoryTileGridMetrics.cs` | `CSharpGit` | `CSharpGit.Presentation.Core` | Application.Tests |
| `src/CSharpGit.Presentation/DiffPresentationResolver.cs` | `CSharpGit` | `CSharpGit.Presentation.Core` | Desktop.Tests |
| `src/CSharpGit.Presentation/DisplayedWorkingTreeBaseline.cs` | `CSharpGit` | `CSharpGit.Presentation.Core` | Desktop.Tests |
| `src/CSharpGit.Presentation/MainWindowTitleFormatter.cs` | `CSharpGit` | `CSharpGit.Presentation.Core` | Desktop.Tests |
| `src/CSharpGit.Presentation/Previewing/DiffFileVersionPathResolver.cs` | `CSharpGit` | `CSharpGit.Presentation.Core` | Desktop.Tests |
| `src/CSharpGit.Presentation/Previewing/FileLanguageClassifier.cs` | `CSharpGit` | `CSharpGit.Presentation.Core` | Desktop.Tests |
| `src/CSharpGit.Presentation/Previewing/FilePreviewModels.cs` | `CSharpGit` | `CSharpGit.Presentation.Core` | Desktop.Tests |
| `src/CSharpGit.Presentation/Previewing/FilePreviewService.cs` | `CSharpGit` | `CSharpGit.Presentation.Core` | Desktop.Tests |
| `src/CSharpGit.Presentation/Previewing/ImageDiffPreviewModels.cs` | `CSharpGit` | `CSharpGit.Presentation.Core` | Desktop.Tests |
| `src/CSharpGit.Presentation/Previewing/ImageFilePreviewProvider.cs` | `CSharpGit` | `CSharpGit.Presentation.Core` | Desktop.Tests |
| `src/CSharpGit.Presentation/Previewing/ImageMetadataReader.cs` | `CSharpGit` | `CSharpGit.Presentation.Core` | Desktop.Tests |
| `src/CSharpGit.Presentation/Previewing/PreviewPublicationGate.cs` | `CSharpGit` | `CSharpGit.Presentation.Core` | Desktop.Tests |
| `src/CSharpGit.Presentation/Previewing/TextFilePreviewProvider.cs` | `CSharpGit` | `CSharpGit.Presentation.Core` | Desktop.Tests |
| `src/CSharpGit.Presentation/RepositoryChangeMonitor.cs` | `CSharpGit` | `CSharpGit.Presentation.Core` | Desktop.Tests |
| `src/CSharpGit.Presentation/RepositoryFilesTreeSelectionRouter.cs` | `CSharpGit` | `CSharpGit.Presentation.Core` | Desktop.Tests |
| `src/CSharpGit.Presentation/RepositoryRefreshLifecycleState.cs` | `CSharpGit` | `CSharpGit.Presentation.Core` | Desktop.Tests |
| `src/CSharpGit.Presentation/Threading/IUiDispatcher.cs` | `CSharpGit` | `CSharpGit.Presentation.Core` | Application.Tests + Desktop.Tests |
| `src/CSharpGit.Presentation/ViewModels/AsyncCommand.cs` | `CSharpGit` | `CSharpGit.Presentation.Core` | Application.Tests + Desktop.Tests |
| `src/CSharpGit.Presentation/ViewModels/BranchFolderDeletionPlanning.cs` | `CSharpGit` | `CSharpGit.Presentation.Core` | Application.Tests + Desktop.Tests |
| `src/CSharpGit.Presentation/ViewModels/BranchFolderInfo.cs` | `CSharpGit` | `CSharpGit.Presentation.Core` | Application.Tests + Desktop.Tests |
| `src/CSharpGit.Presentation/ViewModels/BranchMovePlanning.cs` | `CSharpGit` | `CSharpGit.Presentation.Core` | Desktop.Tests |
| `src/CSharpGit.Presentation/ViewModels/BranchesViewModel.cs` | `CSharpGit` | `CSharpGit.Presentation.Core` | Application.Tests |
| `src/CSharpGit.Presentation/ViewModels/BulkObservableCollection.cs` | `CSharpGit` | `CSharpGit.Presentation.Core` | Application.Tests + Desktop.Tests |
| `src/CSharpGit.Presentation/ViewModels/ChangedFileTreeNode.cs` | `CSharpGit` | `CSharpGit.Presentation.Core` | Desktop.Tests |
| `src/CSharpGit.Presentation/ViewModels/CloneRepositoryViewModel.cs` | `CSharpGit` | `CSharpGit.Presentation.Core` | Application.Tests |
| `src/CSharpGit.Presentation/ViewModels/CommitActionsViewModel.cs` | `CSharpGit` | `CSharpGit.Presentation.Core` | Application.Tests |
| `src/CSharpGit.Presentation/ViewModels/CommitCreationViewModel.cs` | `CSharpGit` | `CSharpGit.Presentation.Core` | Desktop.Tests |
| `src/CSharpGit.Presentation/ViewModels/CommitDetailsViewModel.cs` | `CSharpGit` | `CSharpGit.Presentation.Core` | Desktop.Tests |
| `src/CSharpGit.Presentation/ViewModels/CompactDiffLine.cs` | `CSharpGit` | `CSharpGit.Presentation.Core` | Desktop.Tests |
| `src/CSharpGit.Presentation/ViewModels/DiffLogicalText.cs` | `CSharpGit` | `CSharpGit.Presentation.Core` | Desktop.Tests |
| `src/CSharpGit.Presentation/ViewModels/FileActionsContext.cs` | `CSharpGit` | `CSharpGit.Presentation.Core` | Desktop.Tests |
| `src/CSharpGit.Presentation/ViewModels/FileActionsViewModel.cs` | `CSharpGit` | `CSharpGit.Presentation.Core` | Desktop.Tests |
| `src/CSharpGit.Presentation/ViewModels/FileTreeExpansionPolicy.cs` | `CSharpGit` | `CSharpGit.Presentation.Core` | Desktop.Tests |
| `src/CSharpGit.Presentation/ViewModels/HistoryViewModel.cs` | `CSharpGit` | `CSharpGit.Presentation.Core` | Desktop.Tests |
| `src/CSharpGit.Presentation/ViewModels/IncrementalTreeReconciler.cs` | `CSharpGit` | `CSharpGit.Presentation.Core` | Desktop.Tests |
| `src/CSharpGit.Presentation/ViewModels/InteractiveRebaseViewModel.cs` | `CSharpGit` | `CSharpGit.Presentation.Core` | Application.Tests |
| `src/CSharpGit.Presentation/ViewModels/MiddleEllipsis.cs` | `CSharpGit` | `CSharpGit.Presentation.Core` | Desktop.Tests |
| `src/CSharpGit.Presentation/ViewModels/PathTreeBuilder.cs` | `CSharpGit` | `CSharpGit.Presentation.Core` | Desktop.Tests |
| `src/CSharpGit.Presentation/ViewModels/RecentRepositoriesProjection.cs` | `CSharpGit` | `CSharpGit.Presentation.Core` | Application.Tests |
| `src/CSharpGit.Presentation/ViewModels/RepositoryFilesViewModel.cs` | `CSharpGit` | `CSharpGit.Presentation.Core` | Desktop.Tests |
| `src/CSharpGit.Presentation/ViewModels/RepositoryHistoryRewriteViewModel.cs` | `CSharpGit` | `CSharpGit.Presentation.Core` | Application.Tests |
| `src/CSharpGit.Presentation/ViewModels/RepositoryIdentitySettingsViewModel.cs` | `CSharpGit` | `CSharpGit.Presentation.Core` | Application.Tests |
| `src/CSharpGit.Presentation/ViewModels/RepositoryMaintenanceContext.cs` | `CSharpGit` | `CSharpGit.Presentation.Core` | Desktop.Tests |
| `src/CSharpGit.Presentation/ViewModels/RepositoryMaintenanceViewModel.cs` | `CSharpGit` | `CSharpGit.Presentation.Core` | Desktop.Tests |
| `src/CSharpGit.Presentation/ViewModels/RepositoryOperationsViewModel.cs` | `CSharpGit` | `CSharpGit.Presentation.Core` | Application.Tests |
| `src/CSharpGit.Presentation/ViewModels/RepositorySessionLifetime.cs` | `CSharpGit` | `CSharpGit.Presentation.Core` | Desktop.Tests |
| `src/CSharpGit.Presentation/ViewModels/RepositorySnapshotTreeNode.cs` | `CSharpGit` | `CSharpGit.Presentation.Core` | Desktop.Tests |
| `src/CSharpGit.Presentation/ViewModels/RepositorySyncViewModel.cs` | `CSharpGit` | `CSharpGit.Presentation.Core` | Application.Tests |
| `src/CSharpGit.Presentation/ViewModels/RepositoryTreeDescriptor.cs` | `CSharpGit` | `CSharpGit.Presentation.Core` | Desktop.Tests |
| `src/CSharpGit.Presentation/ViewModels/RepositoryTreeExpansion.cs` | `CSharpGit` | `CSharpGit.Presentation.Core` | Desktop.Tests |
| `src/CSharpGit.Presentation/ViewModels/RepositoryTreeGuideLayout.cs` | `CSharpGit` | `CSharpGit.Presentation.Core` | Desktop.Tests |
| `src/CSharpGit.Presentation/ViewModels/RepositoryTreeNodeKind.cs` | `CSharpGit` | `CSharpGit.Presentation.Core` | Desktop.Tests |
| `src/CSharpGit.Presentation/ViewModels/RepositoryTreeSelection.cs` | `CSharpGit` | `CSharpGit.Presentation.Core` | Desktop.Tests |
| `src/CSharpGit.Presentation/ViewModels/SettingsViewModel.cs` | `CSharpGit` | `CSharpGit.Presentation.Core` | Application.Tests |
| `src/CSharpGit.Presentation/ViewModels/StashesViewModel.cs` | `CSharpGit` | `CSharpGit.Presentation.Core` | Desktop.Tests |
| `src/CSharpGit.Presentation/ViewModels/TagsViewModel.cs` | `CSharpGit` | `CSharpGit.Presentation.Core` | Application.Tests |
| `src/CSharpGit.Presentation/ViewModels/TreeHierarchyGuideBuilder.cs` | `CSharpGit` | `CSharpGit.Presentation.Core` | Desktop.Tests |
| `src/CSharpGit.Presentation/ViewModels/UiChoice.cs` | `CSharpGit` | `CSharpGit.Presentation.Core` | Desktop.Tests |
| `src/CSharpGit.Presentation/ViewModels/WorkingTreeTreeExpansionState.cs` | `CSharpGit` | `CSharpGit.Presentation.Core` | Desktop.Tests |
| `src/CSharpGit.Presentation/ViewModels/WorkingTreeTreeNode.cs` | `CSharpGit` | `CSharpGit.Presentation.Core` | Desktop.Tests |
| `src/CSharpGit.Presentation/ViewModels/WorkingTreeTreeSelection.cs` | `CSharpGit` | `CSharpGit.Presentation.Core` | Desktop.Tests |
| `src/CSharpGit.Presentation/ViewModels/WorkingTreeViewModel.cs` | `CSharpGit` | `CSharpGit.Presentation.Core` | Desktop.Tests |
| `src/CSharpGit.Presentation/ViewModels/WorktreePresentation.cs` | `CSharpGit` | `CSharpGit.Presentation.Core` | Desktop.Tests |
| `src/CSharpGit.Presentation/ViewModels/WorktreesViewModel.cs` | `CSharpGit` | `CSharpGit.Presentation.Core` | Application.Tests |
| `src/CSharpGit.Presentation/WorkingTreeInvalidationEvaluator.cs` | `CSharpGit` | `CSharpGit.Presentation.Core` | Desktop.Tests |

## Platform-dependent types deliberately retained in Uno

`NativeFolderPicker` uses `Windows.Storage.Pickers` and `WinRT.Interop`.
`UiDispatcher` implements the UI-neutral `IUiDispatcher` using the
platform dispatcher. `RecentRepositoriesViewModel` and
`RepositoryTreeNode` depend on XAML and image/UI types, so their complete
classes remain in the Uno executable. XAML pages, controls and
`OpenRepositoryViewModel` remain in the Uno project;
the other, closed neutral ViewModel and model groups are in Presentation.Core.

## Validations

Core projects target `net10.0` with `Microsoft.NET.Sdk`, and
are directly referenced by the two test projects.
Source-ownership, project-reference graph, facet membership, and
absence of linked production-source compilation in tests have
automated guardrails in `CoreAssemblyArchitectureTests` and
`SettingsFacetContractsTests`.

The Linux CI publish-smoke step checks that the framework-dependent
publish directory includes the executable, `CSharpGit.dll` and both new
Core assemblies. This is a packaging smoke check, not a substitute for
native macOS/Windows release artifacts or interactive UI testing.
