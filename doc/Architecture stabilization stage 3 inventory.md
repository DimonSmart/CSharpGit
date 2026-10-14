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
`DesktopFileServices.cs` (92 files total). At the time of stage 3 the service and algorithm sources were linked into
Core from their original directories and excluded from the Uno default compile
sets. Their physical relocation was completed subsequently; the inventory
below retains the original paths and records the final Core paths. Both
Core projects now use SDK default `.cs` inclusion, while tests continue to
reference production assemblies instead of compiling source copies.

## Inventory

| Baseline source path (before relocation) | Physical source path (after relocation) | Previous compiled owner | Current compiled owner | Baseline test consumers |
| --- | --- | --- | --- | --- |
| `src/CSharpGit.Infrastructure/ApplicationUpdateServices.cs` | `src/CSharpGit.Infrastructure.Core/ApplicationUpdateServices.cs` | `CSharpGit.Infrastructure` | `CSharpGit.Infrastructure.Core` | Application.Tests |
| `src/CSharpGit.Infrastructure/AuthorAvatarService.cs` | `src/CSharpGit.Infrastructure.Core/AuthorAvatarService.cs` | `CSharpGit.Infrastructure` | `CSharpGit.Infrastructure.Core` | Application.Tests |
| `src/CSharpGit.Infrastructure/ExternalToolProcessService.cs` | `src/CSharpGit.Infrastructure.Core/ExternalToolProcessService.cs` | `CSharpGit.Infrastructure` | `CSharpGit.Infrastructure.Core` | Application.Tests |
| `src/CSharpGit.Infrastructure/GitHubRepositoryImageProvider.cs` | `src/CSharpGit.Infrastructure.Core/GitHubRepositoryImageProvider.cs` | `CSharpGit.Infrastructure` | `CSharpGit.Infrastructure.Core` | Application.Tests |
| `src/CSharpGit.Infrastructure/JsonAppSettingsService.cs` | `src/CSharpGit.Infrastructure.Core/JsonAppSettingsService.cs` | `CSharpGit.Infrastructure` | `CSharpGit.Infrastructure.Core` | Application.Tests |
| `src/CSharpGit.Infrastructure/LocalRepositoryImageProvider.cs` | `src/CSharpGit.Infrastructure.Core/LocalRepositoryImageProvider.cs` | `CSharpGit.Infrastructure` | `CSharpGit.Infrastructure.Core` | Application.Tests |
| `src/CSharpGit.Infrastructure/RepositoryImageInternals.cs` | `src/CSharpGit.Infrastructure.Core/RepositoryImageInternals.cs` | `CSharpGit.Infrastructure` | `CSharpGit.Infrastructure.Core` | Application.Tests |
| `src/CSharpGit.Infrastructure/RepositoryImageService.Cache.cs` | `src/CSharpGit.Infrastructure.Core/RepositoryImageService.Cache.cs` | `CSharpGit.Infrastructure` | `CSharpGit.Infrastructure.Core` | Application.Tests |
| `src/CSharpGit.Infrastructure/RepositoryImageService.Utilities.cs` | `src/CSharpGit.Infrastructure.Core/RepositoryImageService.Utilities.cs` | `CSharpGit.Infrastructure` | `CSharpGit.Infrastructure.Core` | Application.Tests |
| `src/CSharpGit.Infrastructure/RepositoryImageService.cs` | `src/CSharpGit.Infrastructure.Core/RepositoryImageService.cs` | `CSharpGit.Infrastructure` | `CSharpGit.Infrastructure.Core` | Application.Tests |
| `src/CSharpGit.Presentation/Controls/AuthorAvatarFallback.cs` | `src/CSharpGit.Presentation.Core/Controls/AuthorAvatarFallback.cs` | `CSharpGit` | `CSharpGit.Presentation.Core` | Desktop.Tests |
| `src/CSharpGit.Presentation/Controls/AuthorAvatarLifecycleState.cs` | `src/CSharpGit.Presentation.Core/Controls/AuthorAvatarLifecycleState.cs` | `CSharpGit` | `CSharpGit.Presentation.Core` | Desktop.Tests |
| `src/CSharpGit.Presentation/Controls/AuthorAvatarRequestGate.cs` | `src/CSharpGit.Presentation.Core/Controls/AuthorAvatarRequestGate.cs` | `CSharpGit` | `CSharpGit.Presentation.Core` | Desktop.Tests |
| `src/CSharpGit.Presentation/Controls/CommitGraph/CommitGraphGeometryBuilder.cs` | `src/CSharpGit.Presentation.Core/Controls/CommitGraph/CommitGraphGeometryBuilder.cs` | `CSharpGit` | `CSharpGit.Presentation.Core` | Desktop.Tests |
| `src/CSharpGit.Presentation/Controls/CommitGraph/CommitGraphGeometryCache.cs` | `src/CSharpGit.Presentation.Core/Controls/CommitGraph/CommitGraphGeometryCache.cs` | `CSharpGit` | `CSharpGit.Presentation.Core` | Desktop.Tests |
| `src/CSharpGit.Presentation/Controls/CommitGraph/CommitGraphGeometryKey.cs` | `src/CSharpGit.Presentation.Core/Controls/CommitGraph/CommitGraphGeometryKey.cs` | `CSharpGit` | `CSharpGit.Presentation.Core` | Desktop.Tests |
| `src/CSharpGit.Presentation/Controls/CommitGraph/CommitGraphLayoutState.cs` | `src/CSharpGit.Presentation.Core/Controls/CommitGraph/CommitGraphLayoutState.cs` | `CSharpGit` | `CSharpGit.Presentation.Core` | Desktop.Tests |
| `src/CSharpGit.Presentation/Controls/CommitGraph/CommitGraphMetrics.cs` | `src/CSharpGit.Presentation.Core/Controls/CommitGraph/CommitGraphMetrics.cs` | `CSharpGit` | `CSharpGit.Presentation.Core` | Desktop.Tests |
| `src/CSharpGit.Presentation/Controls/CommitGraph/CommitGraphRowVisual.cs` | `src/CSharpGit.Presentation.Core/Controls/CommitGraph/CommitGraphRowVisual.cs` | `CSharpGit` | `CSharpGit.Presentation.Core` | Desktop.Tests |
| `src/CSharpGit.Presentation/Controls/CommitGraph/CommitGraphTrackPresentation.cs` | `src/CSharpGit.Presentation.Core/Controls/CommitGraph/CommitGraphTrackPresentation.cs` | `CSharpGit` | `CSharpGit.Presentation.Core` | Desktop.Tests |
| `src/CSharpGit.Presentation/Controls/CommitTimeFormatter.cs` | `src/CSharpGit.Presentation.Core/Controls/CommitTimeFormatter.cs` | `CSharpGit` | `CSharpGit.Presentation.Core` | Desktop.Tests |
| `src/CSharpGit.Presentation/Controls/DiffViewportResetGate.cs` | `src/CSharpGit.Presentation.Core/Controls/DiffViewportResetGate.cs` | `CSharpGit` | `CSharpGit.Presentation.Core` | Desktop.Tests |
| `src/CSharpGit.Presentation/Controls/GitCommandConsoleItem.cs` | `src/CSharpGit.Presentation.Core/Controls/GitCommandConsoleItem.cs` | `CSharpGit` | `CSharpGit.Presentation.Core` | Application.Tests |
| `src/CSharpGit.Presentation/Controls/GitCommandConsoleState.cs` | `src/CSharpGit.Presentation.Core/Controls/GitCommandConsoleState.cs` | `CSharpGit` | `CSharpGit.Presentation.Core` | Application.Tests |
| `src/CSharpGit.Presentation/Controls/GitConsoleClipboardText.cs` | `src/CSharpGit.Presentation.Core/Controls/GitConsoleClipboardText.cs` | `CSharpGit` | `CSharpGit.Presentation.Core` | Application.Tests |
| `src/CSharpGit.Presentation/Controls/GitOutputDisplayBuffer.cs` | `src/CSharpGit.Presentation.Core/Controls/GitOutputDisplayBuffer.cs` | `CSharpGit` | `CSharpGit.Presentation.Core` | Application.Tests |
| `src/CSharpGit.Presentation/Controls/RepositoryTileGridMetrics.cs` | `src/CSharpGit.Presentation.Core/Controls/RepositoryTileGridMetrics.cs` | `CSharpGit` | `CSharpGit.Presentation.Core` | Application.Tests |
| `src/CSharpGit.Presentation/DiffPresentationResolver.cs` | `src/CSharpGit.Presentation.Core/DiffPresentationResolver.cs` | `CSharpGit` | `CSharpGit.Presentation.Core` | Desktop.Tests |
| `src/CSharpGit.Presentation/DisplayedWorkingTreeBaseline.cs` | `src/CSharpGit.Presentation.Core/DisplayedWorkingTreeBaseline.cs` | `CSharpGit` | `CSharpGit.Presentation.Core` | Desktop.Tests |
| `src/CSharpGit.Presentation/MainWindowTitleFormatter.cs` | `src/CSharpGit.Presentation.Core/MainWindowTitleFormatter.cs` | `CSharpGit` | `CSharpGit.Presentation.Core` | Desktop.Tests |
| `src/CSharpGit.Presentation/Previewing/DiffFileVersionPathResolver.cs` | `src/CSharpGit.Presentation.Core/Previewing/DiffFileVersionPathResolver.cs` | `CSharpGit` | `CSharpGit.Presentation.Core` | Desktop.Tests |
| `src/CSharpGit.Presentation/Previewing/FileLanguageClassifier.cs` | `src/CSharpGit.Presentation.Core/Previewing/FileLanguageClassifier.cs` | `CSharpGit` | `CSharpGit.Presentation.Core` | Desktop.Tests |
| `src/CSharpGit.Presentation/Previewing/FilePreviewModels.cs` | `src/CSharpGit.Presentation.Core/Previewing/FilePreviewModels.cs` | `CSharpGit` | `CSharpGit.Presentation.Core` | Desktop.Tests |
| `src/CSharpGit.Presentation/Previewing/FilePreviewService.cs` | `src/CSharpGit.Presentation.Core/Previewing/FilePreviewService.cs` | `CSharpGit` | `CSharpGit.Presentation.Core` | Desktop.Tests |
| `src/CSharpGit.Presentation/Previewing/ImageDiffPreviewModels.cs` | `src/CSharpGit.Presentation.Core/Previewing/ImageDiffPreviewModels.cs` | `CSharpGit` | `CSharpGit.Presentation.Core` | Desktop.Tests |
| `src/CSharpGit.Presentation/Previewing/ImageFilePreviewProvider.cs` | `src/CSharpGit.Presentation.Core/Previewing/ImageFilePreviewProvider.cs` | `CSharpGit` | `CSharpGit.Presentation.Core` | Desktop.Tests |
| `src/CSharpGit.Presentation/Previewing/ImageMetadataReader.cs` | `src/CSharpGit.Presentation.Core/Previewing/ImageMetadataReader.cs` | `CSharpGit` | `CSharpGit.Presentation.Core` | Desktop.Tests |
| `src/CSharpGit.Presentation/Previewing/PreviewPublicationGate.cs` | `src/CSharpGit.Presentation.Core/Previewing/PreviewPublicationGate.cs` | `CSharpGit` | `CSharpGit.Presentation.Core` | Desktop.Tests |
| `src/CSharpGit.Presentation/Previewing/TextFilePreviewProvider.cs` | `src/CSharpGit.Presentation.Core/Previewing/TextFilePreviewProvider.cs` | `CSharpGit` | `CSharpGit.Presentation.Core` | Desktop.Tests |
| `src/CSharpGit.Presentation/RepositoryChangeMonitor.cs` | `src/CSharpGit.Presentation.Core/RepositoryChangeMonitor.cs` | `CSharpGit` | `CSharpGit.Presentation.Core` | Desktop.Tests |
| `src/CSharpGit.Presentation/RepositoryFilesTreeSelectionRouter.cs` | `src/CSharpGit.Presentation.Core/RepositoryFilesTreeSelectionRouter.cs` | `CSharpGit` | `CSharpGit.Presentation.Core` | Desktop.Tests |
| `src/CSharpGit.Presentation/RepositoryRefreshLifecycleState.cs` | `src/CSharpGit.Presentation.Core/RepositoryRefreshLifecycleState.cs` | `CSharpGit` | `CSharpGit.Presentation.Core` | Desktop.Tests |
| `src/CSharpGit.Presentation/Threading/IUiDispatcher.cs` | `src/CSharpGit.Presentation.Core/Threading/IUiDispatcher.cs` | `CSharpGit` | `CSharpGit.Presentation.Core` | Application.Tests + Desktop.Tests |
| `src/CSharpGit.Presentation/ViewModels/AsyncCommand.cs` | `src/CSharpGit.Presentation.Core/ViewModels/AsyncCommand.cs` | `CSharpGit` | `CSharpGit.Presentation.Core` | Application.Tests + Desktop.Tests |
| `src/CSharpGit.Presentation/ViewModels/BranchFolderDeletionPlanning.cs` | `src/CSharpGit.Presentation.Core/ViewModels/BranchFolderDeletionPlanning.cs` | `CSharpGit` | `CSharpGit.Presentation.Core` | Application.Tests + Desktop.Tests |
| `src/CSharpGit.Presentation/ViewModels/BranchFolderInfo.cs` | `src/CSharpGit.Presentation.Core/ViewModels/BranchFolderInfo.cs` | `CSharpGit` | `CSharpGit.Presentation.Core` | Application.Tests + Desktop.Tests |
| `src/CSharpGit.Presentation/ViewModels/BranchMovePlanning.cs` | `src/CSharpGit.Presentation.Core/ViewModels/BranchMovePlanning.cs` | `CSharpGit` | `CSharpGit.Presentation.Core` | Desktop.Tests |
| `src/CSharpGit.Presentation/ViewModels/BranchesViewModel.cs` | `src/CSharpGit.Presentation.Core/ViewModels/BranchesViewModel.cs` | `CSharpGit` | `CSharpGit.Presentation.Core` | Application.Tests |
| `src/CSharpGit.Presentation/ViewModels/BulkObservableCollection.cs` | `src/CSharpGit.Presentation.Core/ViewModels/BulkObservableCollection.cs` | `CSharpGit` | `CSharpGit.Presentation.Core` | Application.Tests + Desktop.Tests |
| `src/CSharpGit.Presentation/ViewModels/ChangedFileTreeNode.cs` | `src/CSharpGit.Presentation.Core/ViewModels/ChangedFileTreeNode.cs` | `CSharpGit` | `CSharpGit.Presentation.Core` | Desktop.Tests |
| `src/CSharpGit.Presentation/ViewModels/CloneRepositoryViewModel.cs` | `src/CSharpGit.Presentation.Core/ViewModels/CloneRepositoryViewModel.cs` | `CSharpGit` | `CSharpGit.Presentation.Core` | Application.Tests |
| `src/CSharpGit.Presentation/ViewModels/CommitActionsViewModel.cs` | `src/CSharpGit.Presentation.Core/ViewModels/CommitActionsViewModel.cs` | `CSharpGit` | `CSharpGit.Presentation.Core` | Application.Tests |
| `src/CSharpGit.Presentation/ViewModels/CommitCreationViewModel.cs` | `src/CSharpGit.Presentation.Core/ViewModels/CommitCreationViewModel.cs` | `CSharpGit` | `CSharpGit.Presentation.Core` | Desktop.Tests |
| `src/CSharpGit.Presentation/ViewModels/CommitDetailsViewModel.cs` | `src/CSharpGit.Presentation.Core/ViewModels/CommitDetailsViewModel.cs` | `CSharpGit` | `CSharpGit.Presentation.Core` | Desktop.Tests |
| `src/CSharpGit.Presentation/ViewModels/CompactDiffLine.cs` | `src/CSharpGit.Presentation.Core/ViewModels/CompactDiffLine.cs` | `CSharpGit` | `CSharpGit.Presentation.Core` | Desktop.Tests |
| `src/CSharpGit.Presentation/ViewModels/DiffLogicalText.cs` | `src/CSharpGit.Presentation.Core/ViewModels/DiffLogicalText.cs` | `CSharpGit` | `CSharpGit.Presentation.Core` | Desktop.Tests |
| `src/CSharpGit.Presentation/ViewModels/FileActionsContext.cs` | `src/CSharpGit.Presentation.Core/ViewModels/FileActionsContext.cs` | `CSharpGit` | `CSharpGit.Presentation.Core` | Desktop.Tests |
| `src/CSharpGit.Presentation/ViewModels/FileActionsViewModel.cs` | `src/CSharpGit.Presentation.Core/ViewModels/FileActionsViewModel.cs` | `CSharpGit` | `CSharpGit.Presentation.Core` | Desktop.Tests |
| `src/CSharpGit.Presentation/ViewModels/FileTreeExpansionPolicy.cs` | `src/CSharpGit.Presentation.Core/ViewModels/FileTreeExpansionPolicy.cs` | `CSharpGit` | `CSharpGit.Presentation.Core` | Desktop.Tests |
| `src/CSharpGit.Presentation/ViewModels/HistoryViewModel.cs` | `src/CSharpGit.Presentation.Core/ViewModels/HistoryViewModel.cs` | `CSharpGit` | `CSharpGit.Presentation.Core` | Desktop.Tests |
| `src/CSharpGit.Presentation/ViewModels/IncrementalTreeReconciler.cs` | `src/CSharpGit.Presentation.Core/ViewModels/IncrementalTreeReconciler.cs` | `CSharpGit` | `CSharpGit.Presentation.Core` | Desktop.Tests |
| `src/CSharpGit.Presentation/ViewModels/InteractiveRebaseViewModel.cs` | `src/CSharpGit.Presentation.Core/ViewModels/InteractiveRebaseViewModel.cs` | `CSharpGit` | `CSharpGit.Presentation.Core` | Application.Tests |
| `src/CSharpGit.Presentation/ViewModels/MiddleEllipsis.cs` | `src/CSharpGit.Presentation.Core/ViewModels/MiddleEllipsis.cs` | `CSharpGit` | `CSharpGit.Presentation.Core` | Desktop.Tests |
| `src/CSharpGit.Presentation/ViewModels/PathTreeBuilder.cs` | `src/CSharpGit.Presentation.Core/ViewModels/PathTreeBuilder.cs` | `CSharpGit` | `CSharpGit.Presentation.Core` | Desktop.Tests |
| `src/CSharpGit.Presentation/ViewModels/RecentRepositoriesProjection.cs` | `src/CSharpGit.Presentation.Core/ViewModels/RecentRepositoriesProjection.cs` | `CSharpGit` | `CSharpGit.Presentation.Core` | Application.Tests |
| `src/CSharpGit.Presentation/ViewModels/RepositoryFilesViewModel.cs` | `src/CSharpGit.Presentation.Core/ViewModels/RepositoryFilesViewModel.cs` | `CSharpGit` | `CSharpGit.Presentation.Core` | Desktop.Tests |
| `src/CSharpGit.Presentation/ViewModels/RepositoryHistoryRewriteViewModel.cs` | `src/CSharpGit.Presentation.Core/ViewModels/RepositoryHistoryRewriteViewModel.cs` | `CSharpGit` | `CSharpGit.Presentation.Core` | Application.Tests |
| `src/CSharpGit.Presentation/ViewModels/RepositoryIdentitySettingsViewModel.cs` | `src/CSharpGit.Presentation.Core/ViewModels/RepositoryIdentitySettingsViewModel.cs` | `CSharpGit` | `CSharpGit.Presentation.Core` | Application.Tests |
| `src/CSharpGit.Presentation/ViewModels/RepositoryMaintenanceContext.cs` | `src/CSharpGit.Presentation.Core/ViewModels/RepositoryMaintenanceContext.cs` | `CSharpGit` | `CSharpGit.Presentation.Core` | Desktop.Tests |
| `src/CSharpGit.Presentation/ViewModels/RepositoryMaintenanceViewModel.cs` | `src/CSharpGit.Presentation.Core/ViewModels/RepositoryMaintenanceViewModel.cs` | `CSharpGit` | `CSharpGit.Presentation.Core` | Desktop.Tests |
| `src/CSharpGit.Presentation/ViewModels/RepositoryOperationsViewModel.cs` | `src/CSharpGit.Presentation.Core/ViewModels/RepositoryOperationsViewModel.cs` | `CSharpGit` | `CSharpGit.Presentation.Core` | Application.Tests |
| `src/CSharpGit.Presentation/ViewModels/RepositorySessionLifetime.cs` | `src/CSharpGit.Presentation.Core/ViewModels/RepositorySessionLifetime.cs` | `CSharpGit` | `CSharpGit.Presentation.Core` | Desktop.Tests |
| `src/CSharpGit.Presentation/ViewModels/RepositorySnapshotTreeNode.cs` | `src/CSharpGit.Presentation.Core/ViewModels/RepositorySnapshotTreeNode.cs` | `CSharpGit` | `CSharpGit.Presentation.Core` | Desktop.Tests |
| `src/CSharpGit.Presentation/ViewModels/RepositorySyncViewModel.cs` | `src/CSharpGit.Presentation.Core/ViewModels/RepositorySyncViewModel.cs` | `CSharpGit` | `CSharpGit.Presentation.Core` | Application.Tests |
| `src/CSharpGit.Presentation/ViewModels/RepositoryTreeDescriptor.cs` | `src/CSharpGit.Presentation.Core/ViewModels/RepositoryTreeDescriptor.cs` | `CSharpGit` | `CSharpGit.Presentation.Core` | Desktop.Tests |
| `src/CSharpGit.Presentation/ViewModels/RepositoryTreeExpansion.cs` | `src/CSharpGit.Presentation.Core/ViewModels/RepositoryTreeExpansion.cs` | `CSharpGit` | `CSharpGit.Presentation.Core` | Desktop.Tests |
| `src/CSharpGit.Presentation/ViewModels/RepositoryTreeGuideLayout.cs` | `src/CSharpGit.Presentation.Core/ViewModels/RepositoryTreeGuideLayout.cs` | `CSharpGit` | `CSharpGit.Presentation.Core` | Desktop.Tests |
| `src/CSharpGit.Presentation/ViewModels/RepositoryTreeNodeKind.cs` | `src/CSharpGit.Presentation.Core/ViewModels/RepositoryTreeNodeKind.cs` | `CSharpGit` | `CSharpGit.Presentation.Core` | Desktop.Tests |
| `src/CSharpGit.Presentation/ViewModels/RepositoryTreeSelection.cs` | `src/CSharpGit.Presentation.Core/ViewModels/RepositoryTreeSelection.cs` | `CSharpGit` | `CSharpGit.Presentation.Core` | Desktop.Tests |
| `src/CSharpGit.Presentation/ViewModels/SettingsViewModel.cs` | `src/CSharpGit.Presentation.Core/ViewModels/SettingsViewModel.cs` | `CSharpGit` | `CSharpGit.Presentation.Core` | Application.Tests |
| `src/CSharpGit.Presentation/ViewModels/StashesViewModel.cs` | `src/CSharpGit.Presentation.Core/ViewModels/StashesViewModel.cs` | `CSharpGit` | `CSharpGit.Presentation.Core` | Desktop.Tests |
| `src/CSharpGit.Presentation/ViewModels/TagsViewModel.cs` | `src/CSharpGit.Presentation.Core/ViewModels/TagsViewModel.cs` | `CSharpGit` | `CSharpGit.Presentation.Core` | Application.Tests |
| `src/CSharpGit.Presentation/ViewModels/TreeHierarchyGuideBuilder.cs` | `src/CSharpGit.Presentation.Core/ViewModels/TreeHierarchyGuideBuilder.cs` | `CSharpGit` | `CSharpGit.Presentation.Core` | Desktop.Tests |
| `src/CSharpGit.Presentation/ViewModels/UiChoice.cs` | `src/CSharpGit.Presentation.Core/ViewModels/UiChoice.cs` | `CSharpGit` | `CSharpGit.Presentation.Core` | Desktop.Tests |
| `src/CSharpGit.Presentation/ViewModels/WorkingTreeTreeExpansionState.cs` | `src/CSharpGit.Presentation.Core/ViewModels/WorkingTreeTreeExpansionState.cs` | `CSharpGit` | `CSharpGit.Presentation.Core` | Desktop.Tests |
| `src/CSharpGit.Presentation/ViewModels/WorkingTreeTreeNode.cs` | `src/CSharpGit.Presentation.Core/ViewModels/WorkingTreeTreeNode.cs` | `CSharpGit` | `CSharpGit.Presentation.Core` | Desktop.Tests |
| `src/CSharpGit.Presentation/ViewModels/WorkingTreeTreeSelection.cs` | `src/CSharpGit.Presentation.Core/ViewModels/WorkingTreeTreeSelection.cs` | `CSharpGit` | `CSharpGit.Presentation.Core` | Desktop.Tests |
| `src/CSharpGit.Presentation/ViewModels/WorkingTreeViewModel.cs` | `src/CSharpGit.Presentation.Core/ViewModels/WorkingTreeViewModel.cs` | `CSharpGit` | `CSharpGit.Presentation.Core` | Desktop.Tests |
| `src/CSharpGit.Presentation/ViewModels/WorktreePresentation.cs` | `src/CSharpGit.Presentation.Core/ViewModels/WorktreePresentation.cs` | `CSharpGit` | `CSharpGit.Presentation.Core` | Desktop.Tests |
| `src/CSharpGit.Presentation/ViewModels/WorktreesViewModel.cs` | `src/CSharpGit.Presentation.Core/ViewModels/WorktreesViewModel.cs` | `CSharpGit` | `CSharpGit.Presentation.Core` | Application.Tests |
| `src/CSharpGit.Presentation/WorkingTreeInvalidationEvaluator.cs` | `src/CSharpGit.Presentation.Core/WorkingTreeInvalidationEvaluator.cs` | `CSharpGit` | `CSharpGit.Presentation.Core` | Desktop.Tests |

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

## Physical relocation completion (2026-10-14)

| Measurement | Before relocation | After relocation |
| --- | ---: | ---: |
| Linked production sources in Infrastructure.Core | 11 | 0 |
| Linked production sources in Presentation.Core | 81 | 0 |
| Corresponding Uno `Compile Remove` entries | 92 | 0 |
| Physically relocated production source files | 0 | 92 |
| Direct production source inclusions in tests | 0 | 0 |
| Core assemblies | 2 | 2 |

`DesktopFileServices.cs` was included in the Core build after the initial
stage 3 inventory and is moved from `src/CSharpGit.Infrastructure/` to
`src/CSharpGit.Infrastructure.Core/` as the 92nd file. The baseline table
above preserves the original 91 inventory members.

The Linux CI publish-smoke step checks that the framework-dependent
publish directory includes the executable, `CSharpGit.dll` and both new
Core assemblies. This is a packaging smoke check, not a substitute for
native macOS/Windows release artifacts or interactive UI testing.
