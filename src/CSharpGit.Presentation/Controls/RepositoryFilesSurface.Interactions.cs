using System.ComponentModel;
using CSharpGit.Application.Abstractions;
using CSharpGit.Domain;
using CSharpGit.Presentation.ViewModels;
using CSharpGit.Presentation.Workflows;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Windows.System;

namespace CSharpGit.Presentation.Controls;

// Owns repository snapshot browsing, search and selection interactions.
internal sealed partial class RepositoryFilesSurface
{
    private RepositoryFilesViewModel _repositoryFilesViewModel = null!;
    private RepositoryFilePreviewController _repositoryFilePreview = null!;
    private Func<bool> _isActive = null!;
    private Func<bool> _isShuttingDown = null!;
    private Action<Func<Task>, string> _runUiOperation = null!;
    private Func<RepositorySnapshotEntry, Task> _openSubmodule = null!;
    private Func<FrameworkElement, RepositorySnapshotEntry, Task> _showSubmoduleMenu = null!;
    private Func<RepositorySnapshotEntry, bool, Task> _openFile = null!;
    private Action<FrameworkElement, RepositorySnapshotEntry> _showFileMenu = null!;
    private bool _configured;
    private bool _repositoryFilesBuildingTree;
    private bool _repositoryFilesRestoringPresentationState;
    private bool _repositoryFilesHandlingUserSelection;
    private readonly RepositoryFilesTreeSelectionRouter<RepositorySnapshotTreeNode> _repositoryFilesSelectionRouter = new();
    private long _repositoryFilesActivationRevision;
    private TextBox? _repositoryFilesSearch => SearchBox;
    private ComboBox? _repositoryFilesSearchMode => SearchMode;
    private TreeView? _repositoryFilesTree => FileTree;
    private ListView? _repositoryContentResults => ContentResults;

    internal long ActivationRevision => _repositoryFilesActivationRevision;
    internal bool IsRepositoryFilesActive => _configured && _isActive();
    private bool IsShuttingDown => !_configured || _isShuttingDown();

    internal void Configure(
        RepositoryFilesViewModel viewModel,
        RepositoryFilePreviewController preview,
        Func<bool> isActive,
        Func<bool> isShuttingDown,
        Action<Func<Task>, string> runUiOperation,
        Func<RepositorySnapshotEntry, Task> openSubmodule,
        Func<FrameworkElement, RepositorySnapshotEntry, Task> showSubmoduleMenu,
        Func<RepositorySnapshotEntry, bool, Task> openFile,
        Action<FrameworkElement, RepositorySnapshotEntry> showFileMenu)
    {
        if (_configured) throw new InvalidOperationException("Repository Files surface is already configured.");
        _configured = true;
        _repositoryFilesViewModel = viewModel;
        _repositoryFilePreview = preview;
        _isActive = isActive;
        _isShuttingDown = isShuttingDown;
        _runUiOperation = runUiOperation;
        _openSubmodule = openSubmodule;
        _showSubmoduleMenu = showSubmoduleMenu;
        _openFile = openFile;
        _showFileMenu = showFileMenu;

        SearchBox.TextChanged += RepositoryFilesSearch_TextChanged;
        SearchBox.KeyDown += RepositoryFilesSearch_KeyDown;
        SearchMode.SelectionChanged += RepositoryFilesSearchMode_SelectionChanged;
        FileTree.SelectionChanged += RepositoryFilesTree_SelectionChanged;
        FileTree.DoubleTapped += RepositoryFilesTree_DoubleTapped;
        FileTree.RightTapped += RepositoryFilesTree_RightTapped;
        FileTree.Expanding += RepositoryFilesTree_Expanding;
        FileTree.Collapsed += RepositoryFilesTree_Collapsed;
        ContentResults.SelectionChanged += RepositoryContentResults_SelectionChanged;
        ContentResults.DoubleTapped += RepositoryContentResults_DoubleTapped;
        ContentResults.KeyDown += RepositoryContentResults_KeyDown;
        ContentResults.RightTapped += RepositoryContentResults_RightTapped;
        _repositoryFilesViewModel.PropertyChanged += RepositoryFilesViewModel_PropertyChanged;
        SynchronizeRepositoryFilesSearchControls();
        UpdateRepositoryFilesModeSurface();
        UpdateStatus(_repositoryFilesViewModel.StatusText, _repositoryFilesViewModel.IsLoading);
    }

    internal void ShutdownInteractions()
    {
        if (!_configured) return;
        _configured = false;
        SearchBox.TextChanged -= RepositoryFilesSearch_TextChanged;
        SearchBox.KeyDown -= RepositoryFilesSearch_KeyDown;
        SearchMode.SelectionChanged -= RepositoryFilesSearchMode_SelectionChanged;
        FileTree.SelectionChanged -= RepositoryFilesTree_SelectionChanged;
        FileTree.DoubleTapped -= RepositoryFilesTree_DoubleTapped;
        FileTree.RightTapped -= RepositoryFilesTree_RightTapped;
        FileTree.Expanding -= RepositoryFilesTree_Expanding;
        FileTree.Collapsed -= RepositoryFilesTree_Collapsed;
        ContentResults.SelectionChanged -= RepositoryContentResults_SelectionChanged;
        ContentResults.DoubleTapped -= RepositoryContentResults_DoubleTapped;
        ContentResults.KeyDown -= RepositoryContentResults_KeyDown;
        ContentResults.RightTapped -= RepositoryContentResults_RightTapped;
        _repositoryFilesViewModel.PropertyChanged -= RepositoryFilesViewModel_PropertyChanged;
        _repositoryFilePreview.Cancel();
    }

    internal void UpdateActivity()
    {
        if (_configured) UpdateRepositoryFilesViewActivity();
    }
    internal void UpdateModeSurface()
    {
        if (_configured) UpdateRepositoryFilesModeSurface();
    }
    private void RunPageUiOperation(Func<Task> operation, string description) =>
        _runUiOperation(operation, description);

    private void RepositoryFilesViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs args)
    {
        switch (args.PropertyName)
        {
            case nameof(RepositoryFilesViewModel.StatusText):
            case nameof(RepositoryFilesViewModel.IsLoading):
                UpdateStatus(
                    _repositoryFilesViewModel.StatusText,
                    _repositoryFilesViewModel.IsLoading);
                break;

            case nameof(RepositoryFilesViewModel.NameQuery):
            case nameof(RepositoryFilesViewModel.SearchMode):
                SynchronizeRepositoryFilesSearchControls();
                UpdateRepositoryFilesModeSurface();
                break;

            case nameof(RepositoryFilesViewModel.ContentResults):
            case nameof(RepositoryFilesViewModel.IsContentSearchMode):
                UpdateRepositoryFilesModeSurface();
                break;

            case nameof(RepositoryFilesViewModel.TreeRevision):
                _repositoryFilesSelectionRouter.ReplaceTree(
                    _repositoryFilesViewModel.TreeRoots,
                    node => node.Children);
                SynchronizeRepositoryFilesTreeSelection();
                break;

            case nameof(RepositoryFilesViewModel.SelectionRevision):
                if (!_repositoryFilesHandlingUserSelection)
                    SynchronizeRepositoryFilesTreeSelection();
                ApplyRepositoryFileSelectionFromViewModel();
                break;
        }
    }

    private void UpdateRepositoryFilesViewActivity()
    {
        if (!IsRepositoryFilesActive)
        {
            ++_repositoryFilesActivationRevision;
            _repositoryFilePreview.Cancel();
            RunPageUiOperation(() => _repositoryFilesViewModel.SetActiveAsync(false),
                "Repository files deactivation");
            return;
        }

        if (_repositoryFilesViewModel.IsActive)
            return;

        var activation = ++_repositoryFilesActivationRevision;
        RunPageUiOperation(() => ActivateRepositoryFilesAsync(activation),
            "Repository files activation");
    }

    private async Task ActivateRepositoryFilesAsync(long activation)
    {
        await _repositoryFilesViewModel.SetActiveAsync(true);
        if (IsShuttingDown || !IsRepositoryFilesActive
            || !_repositoryFilesViewModel.IsActive
            || activation != _repositoryFilesActivationRevision)
            return;

        // Rebuilding may have generated no SelectionRevision (same selected node).
        // Resume the preview cancelled on tab deactivation, but not if RefreshAsync
        // has already started it through a genuine selection change.
        if (_repositoryFilePreview.StartedActivation != activation)
            ApplyRepositoryFileSelectionFromViewModel();
    }

    private void SynchronizeRepositoryFilesSearchControls()
    {
        _repositoryFilesRestoringPresentationState = true;
        try
        {
            if (_repositoryFilesSearch is not null
                && !string.Equals(
                    _repositoryFilesSearch.Text,
                    _repositoryFilesViewModel.NameQuery,
                    StringComparison.Ordinal))
            {
                _repositoryFilesSearch.Text = _repositoryFilesViewModel.NameQuery;
            }

            if (_repositoryFilesSearchMode is not null
                && !string.Equals(
                    _repositoryFilesSearchMode.SelectedItem as string,
                    _repositoryFilesViewModel.SearchMode,
                    StringComparison.Ordinal))
            {
                _repositoryFilesSearchMode.SelectedItem = _repositoryFilesViewModel.SearchMode;
            }
        }
        finally
        {
            _repositoryFilesRestoringPresentationState = false;
        }
    }

    private void SynchronizeRepositoryFilesTreeSelection()
    {
        if (_repositoryFilesTree is null
            || !IsRepositoryFilesActive
            || _repositoryFilesHandlingUserSelection
            || _repositoryFilesViewModel.IsRebuildingTree
            || _repositoryFilesViewModel.IsContentSearchMode)
            return;

        var target = _repositoryFilesViewModel.SelectedTreeNode;
        if (ReferenceEquals(_repositoryFilesTree.SelectedItem, target))
            return;

        _repositoryFilesSelectionRouter.ExpectProgrammaticSelection(target);
        _repositoryFilesBuildingTree = true;
        try
        {
            _repositoryFilesTree.SelectedItem = target;
        }
        finally
        {
            _repositoryFilesBuildingTree = false;
        }
    }

    private void ApplyRepositoryFileSelectionFromViewModel()
    {
        if (!IsRepositoryFilesActive)
            return;

        if (_repositoryFilesViewModel.SelectedPath is not { } path)
        {
            _repositoryFilePreview.ClearSelection();
            return;
        }

        _repositoryFilePreview.ApplySelection(
            path,
            _repositoryFilesViewModel.SelectedLineNumber,
            _repositoryFilesViewModel.SelectedEntry);
    }

    private void RepositoryFilesSearch_TextChanged(object sender, TextChangedEventArgs args)
    {
        if (_repositoryFilesRestoringPresentationState || _repositoryFilesSearch is null)
            return;
        _repositoryFilesViewModel.SetNameQuery(_repositoryFilesSearch.Text);
    }

    private void RepositoryFilesSearch_KeyDown(object sender, KeyRoutedEventArgs args)
    {
        if (args.Key != VirtualKey.Enter
            || !_repositoryFilesViewModel.IsContentSearchMode
            || _repositoryFilesSearch is null)
        {
            return;
        }

        args.Handled = true;
        var query = _repositoryFilesSearch.Text;
        RunPageUiOperation(() => _repositoryFilesViewModel.SearchContentAsync(query), "Repository content search");
    }

    private void RepositoryFilesSearchMode_SelectionChanged(object sender, SelectionChangedEventArgs args)
    {
        if (_repositoryFilesRestoringPresentationState
            || _repositoryFilesSearchMode?.SelectedItem is not string mode)
        {
            return;
        }

        _repositoryFilesViewModel.SetSearchMode(
            mode,
            _repositoryFilesSearch?.Text ?? string.Empty);
    }

    private void UpdateRepositoryFilesModeSurface()
    {
        if (_repositoryFilesTree is null || _repositoryContentResults is null) return;

        var content = _repositoryFilesViewModel.IsContentSearchMode;
        _repositoryFilesTree.Visibility = content ? Visibility.Collapsed : Visibility.Visible;
        _repositoryContentResults.Visibility = content ? Visibility.Visible : Visibility.Collapsed;
        _repositoryContentResults.ItemsSource = content
            ? _repositoryFilesViewModel.ContentResults
            : null;
    }

    private void RepositoryFilesTree_SelectionChanged(TreeView sender, TreeViewSelectionChangedEventArgs args)
    {
        if (!IsRepositoryFilesActive
            || _repositoryFilesBuildingTree || _repositoryFilesViewModel.IsRebuildingTree
            || _repositoryFilesViewModel.IsContentSearchMode
            || !_repositoryFilesViewModel.SnapshotMatchesSelection)
            return;

        var decision = _repositoryFilesSelectionRouter.Route(
            args.AddedItems.Cast<object?>().ToArray(),
            args.RemovedItems.Cast<object?>().ToArray(),
            ResolveRepositoryFilesTreeNode,
            _repositoryFilesViewModel.SelectedTreeNode,
            rebuilding: false);

        if (decision.Action == RepositoryFilesTreeSelectionAction.Select)
        {
            _repositoryFilesHandlingUserSelection = true;
            try
            {
                _repositoryFilesViewModel.SelectTreeNode(decision.Node);
            }
            finally
            {
                _repositoryFilesHandlingUserSelection = false;
            }
        }
        else if (decision.Action == RepositoryFilesTreeSelectionAction.ClearIfStillUnselected)
        {
            var selectionRevision = _repositoryFilesViewModel.SelectionRevision;
            var treeRevision = _repositoryFilesViewModel.TreeRevision;
            var selectedNode = _repositoryFilesViewModel.SelectedTreeNode;
            // A remove(A) -> add(B) sequence must not clear B. This also filters
            // delayed removal emitted by a programmatic tree reconciliation.
            DispatcherQueue.TryEnqueue(() =>
            {
                if (!IsRepositoryFilesActive || _repositoryFilesViewModel.IsRebuildingTree
                    || _repositoryFilesViewModel.IsContentSearchMode
                    || !_repositoryFilesViewModel.SnapshotMatchesSelection
                    || selectionRevision != _repositoryFilesViewModel.SelectionRevision
                    || treeRevision != _repositoryFilesViewModel.TreeRevision
                    || !ReferenceEquals(selectedNode, _repositoryFilesViewModel.SelectedTreeNode)
                    || sender.SelectedItem is not null)
                    return;

                _repositoryFilesHandlingUserSelection = true;
                try
                {
                    _repositoryFilesViewModel.SelectTreeNode(null);
                }
                finally
                {
                    _repositoryFilesHandlingUserSelection = false;
                }
            });
        }
    }

    private static RepositorySnapshotTreeNode? ResolveRepositoryFilesTreeNode(object? value) =>
        value switch
        {
            RepositorySnapshotTreeNode node => node,
            TreeViewNode { Content: RepositorySnapshotTreeNode node } => node,
            _ => null
        };

    private void RepositoryFilesTree_DoubleTapped(object sender, DoubleTappedRoutedEventArgs args)
    {
        if (SelectedRepositorySnapshotEntry() is not { } entry) return;
        if (entry.Kind == RepositorySnapshotEntryKind.Submodule)
            RunPageUiOperation(() => _submodules._openSubmodule(entry), "Open submodule");
        else if (entry.Kind == RepositorySnapshotEntryKind.File)
            RunPageUiOperation(() => _openFile(entry, openInEditor: false), "Repository file opening");
        else
            return;
        args.Handled = true;
    }

    private void RepositoryFilesTree_RightTapped(object sender, RightTappedRoutedEventArgs args)
    {
        if (args.OriginalSource is not FrameworkElement source
            || SelectedRepositorySnapshotEntry() is not { } entry)
        {
            return;
        }

        if (entry.Kind == RepositorySnapshotEntryKind.Submodule)
            RunPageUiOperation(() => _submodules._showSubmoduleMenu(source, entry), "Submodule menu");
        else
            _showFileMenu(source, entry);
        args.Handled = true;
    }

    private void RepositoryFilesTree_Expanding(TreeView sender, TreeViewExpandingEventArgs args)
    {
        if (args.Item is RepositorySnapshotTreeNode model)
            _repositoryFilesViewModel.SetExpanded(model, expanded: true);
    }

    private void RepositoryFilesTree_Collapsed(TreeView sender, TreeViewCollapsedEventArgs args)
    {
        if (args.Item is RepositorySnapshotTreeNode model)
            _repositoryFilesViewModel.SetExpanded(model, expanded: false);
    }

    private RepositorySnapshotEntry? SelectedRepositorySnapshotEntry() =>
        _repositoryFilesViewModel.SnapshotMatchesSelection
            ? _repositoryFilesViewModel.SelectedEntry
            : null;

    private void RepositoryContentResults_SelectionChanged(object sender, SelectionChangedEventArgs args)
    {
        _repositoryFilesViewModel.SelectContentResult(
            _repositoryContentResults?.SelectedItem as RepositoryContentSearchRow);
    }

    private void RepositoryContentResults_DoubleTapped(object sender, DoubleTappedRoutedEventArgs args)
    {
        if (_repositoryContentResults?.SelectedItem is not RepositoryContentSearchRow row)
            return;

        RunPageUiOperation(() => OpenRepositoryContentMatchAsync(row, openInEditor: false), "Repository content opening");
        args.Handled = true;
    }

    private void RepositoryContentResults_KeyDown(object sender, KeyRoutedEventArgs args)
    {
        if (args.Key != VirtualKey.Enter
            || _repositoryContentResults?.SelectedItem is not RepositoryContentSearchRow row)
        {
            return;
        }

        args.Handled = true;
        RunPageUiOperation(() => OpenRepositoryContentMatchAsync(row, openInEditor: false), "Repository content opening");
    }

    private void RepositoryContentResults_RightTapped(object sender, RightTappedRoutedEventArgs args)
    {
        if (args.OriginalSource is not FrameworkElement source
            || _repositoryContentResults?.SelectedItem is not RepositoryContentSearchRow row)
        {
            return;
        }

        var entry = _repositoryFilesViewModel.FindEntry(row.Match.Path);
        if (entry is null) return;

        _showFileMenu(source, entry);
        args.Handled = true;
    }

    private async Task OpenRepositoryContentMatchAsync(
        RepositoryContentSearchRow row,
        bool openInEditor)
    {
        var entry = _repositoryFilesViewModel.SelectContentResult(row);
        if (entry is not { Kind: RepositorySnapshotEntryKind.File }) return;
        await _openFile(entry, openInEditor);
    }


}
