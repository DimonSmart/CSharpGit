using System.ComponentModel;
using CSharpGit.Presentation.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace CSharpGit.Presentation;

public sealed partial class MainPage
{
    private bool _repositoryTreeStateTrackingAttached;

    protected override void OnApplyTemplate()
    {
        base.OnApplyTemplate();

        Loaded -= RepositoryTreeState_Loaded;
        Loaded += RepositoryTreeState_Loaded;
        TryAttachRepositoryTreeStateTracking();

        if (_compactLayoutApplied) return;
        Loaded -= ApplyCompactLayoutWhenLoaded;
        Loaded += ApplyCompactLayoutWhenLoaded;
        _ = DispatcherQueue.TryEnqueue(ApplyCompactWorkspaceLayout);
    }

    private void RepositoryTreeState_Loaded(object sender, RoutedEventArgs args)
    {
        Loaded -= RepositoryTreeState_Loaded;
        TryAttachRepositoryTreeStateTracking();
    }

    private void TryAttachRepositoryTreeStateTracking()
    {
        if (_repositoryTreeStateTrackingAttached || RepositoryTree is null || _viewModel is null) return;

        RepositoryTree.Expanding += RepositoryTree_Expanding;
        RepositoryTree.Collapsed += RepositoryTree_Collapsed;
        _viewModel.PropertyChanged += RepositoryTreeViewModel_PropertyChanged;
        _repositoryTreeStateTrackingAttached = true;

        if (_viewModel.Repository is null) return;
        RepositoryTreeNode.ResetExpansionState();
        RebuildRepositoryTree();
    }

    private void RepositoryTree_Expanding(TreeView sender, TreeViewExpandingEventArgs args)
    {
        if (ResolveNode(args.Item) is { } node) node.IsExpanded = true;
    }

    private void RepositoryTree_Collapsed(TreeView sender, TreeViewCollapsedEventArgs args)
    {
        if (ResolveNode(args.Item) is { } node) node.IsExpanded = false;
    }

    private void RepositoryTreeViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName != nameof(OpenRepositoryViewModel.Repository)) return;

        RepositoryTreeNode.ResetExpansionState();
        RebuildRepositoryTree();
    }

    private void DetachRepositoryTreeStateTracking()
    {
        Loaded -= RepositoryTreeState_Loaded;
        Loaded -= ApplyCompactLayoutWhenLoaded;
        if (!_repositoryTreeStateTrackingAttached) return;

        RepositoryTree.Expanding -= RepositoryTree_Expanding;
        RepositoryTree.Collapsed -= RepositoryTree_Collapsed;
        _viewModel.PropertyChanged -= RepositoryTreeViewModel_PropertyChanged;
        _repositoryTreeStateTrackingAttached = false;
    }
}
