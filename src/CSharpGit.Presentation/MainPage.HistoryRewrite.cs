using CSharpGit.Application.Abstractions;
using CSharpGit.Domain;
using CSharpGit.Presentation.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;

namespace CSharpGit.Presentation;

public sealed partial class MainPage
{
    internal bool IsHistoryRewriteInProgress => _viewModel.RepositoryHistoryRewrite.IsInProgress;

    private void ShutdownRepositoryHistoryRewriteMenus()
    {
        if (_repositoryFilesTree is not null)
            _repositoryFilesTree.RightTapped -= RepositoryFilesTree_HistoryRewriteRightTapped;
        if (_repositoryContentResults is not null)
            _repositoryContentResults.RightTapped -= RepositoryContentResults_HistoryRewriteRightTapped;
    }

    private void InstallRepositoryHistoryRewriteMenus()
    {
        if (_repositoryFilesTree is not null)
        {
            _repositoryFilesTree.RightTapped -= RepositoryFilesTree_RightTapped;
            _repositoryFilesTree.RightTapped += RepositoryFilesTree_HistoryRewriteRightTapped;
        }

        if (_repositoryContentResults is not null)
        {
            _repositoryContentResults.RightTapped -= RepositoryContentResults_RightTapped;
            _repositoryContentResults.RightTapped += RepositoryContentResults_HistoryRewriteRightTapped;
        }
    }

    private void RepositoryFilesTree_HistoryRewriteRightTapped(object sender, RightTappedRoutedEventArgs args)
    {
        if (args.OriginalSource is not FrameworkElement source
            || SelectedRepositorySnapshotEntry() is not { } entry)
        {
            return;
        }

        ShowRepositorySnapshotFileMenuWithHistoryRewrite(source, entry);
        args.Handled = true;
    }

    private void RepositoryContentResults_HistoryRewriteRightTapped(object sender, RightTappedRoutedEventArgs args)
    {
        if (args.OriginalSource is not FrameworkElement source
            || _repositoryContentResults?.SelectedItem is not RepositoryContentSearchRow row)
        {
            return;
        }

        var entry = _repositoryFilesViewModel.FindEntry(row.Match.Path);
        if (entry is null) return;

        ShowRepositorySnapshotFileMenuWithHistoryRewrite(source, entry);
        args.Handled = true;
    }

    private void ShowRepositorySnapshotFileMenuWithHistoryRewrite(
        FrameworkElement target,
        RepositorySnapshotEntry entry)
    {
        var flyout = new MenuFlyout();
        if (entry.Kind == RepositorySnapshotEntryKind.File)
        {
            AddMenuItem(flyout, "Open", true, () => OpenRepositorySnapshotFileAsync(entry, openInEditor: false));
            AddMenuItem(
                flyout,
                "Open in editor",
                true,
                () => OpenRepositorySnapshotFileAsync(entry, openInEditor: true));
            flyout.Items.Add(new MenuFlyoutSeparator());
            AddMenuItem(
                flyout,
                "Remove from repository history…",
                !_viewModel.IsBusy && !_viewModel.RepositoryHistoryRewrite.IsInProgress,
                () => _historyRewrite.RemovePathAsync(entry));
            flyout.Items.Add(new MenuFlyoutSeparator());
        }

        AddMenuItem(flyout, "Copy path", true, () => CopyTextAsync(entry.Path));
        flyout.ShowAt(target);
    }

}
