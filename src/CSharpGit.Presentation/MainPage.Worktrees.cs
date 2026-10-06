using System.ComponentModel;
using System.Diagnostics;
using System.Text;
using CSharpGit.Domain;
using CSharpGit.Presentation.ViewModels;
using Microsoft.UI.Xaml.Controls;

namespace CSharpGit.Presentation;

public sealed partial class MainPage
{
    private WorktreesViewModel _worktreesViewModel = null!;
    private bool _worktreeShutdown;

    private void InitializeWorktreeSupport()
    {
        _worktreeShutdown = false;
        _worktreesViewModel.PropertyChanged += WorktreesViewModel_PropertyChanged;
        _worktreesViewModel.Attach(_viewModel);
    }

    private void ShutdownWorktreeSupport()
    {
        if (_worktreeShutdown) return;
        _worktreeShutdown = true;
        _worktreesViewModel.PropertyChanged -= WorktreesViewModel_PropertyChanged;
        _worktreesViewModel.Dispose();
    }

    internal Task OpenInitialRepositoryAsync() => _viewModel.OpenRepositoryAsyncForDesktopCheck();

    private void WorktreesViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (_worktreeShutdown) return;
        if (args.PropertyName == nameof(WorktreesViewModel.Worktrees))
            SynchronizeWorktreePresentation();
    }

    private WorktreeInfo? FindWorktreeForBranch(string branch) =>
        _worktreesViewModel.FindWorktreeForBranch(branch);

    private async Task CreateWorktreeFromBranchAsync(GitBranch branch)
    {
        var repository = _viewModel.Repository;
        if (repository is null || !_worktreesViewModel.CanMutate) return;

        if (FindWorktreeForBranch(branch.Name) is { } existing)
        {
            await ShowErrorAsync(
                "Branch already has a worktree",
                $"Branch '{branch.Name}' is already checked out in '{existing.Path}'.");
            return;
        }

        var directory = new TextBox
        {
            Header = "Directory",
            Text = SuggestWorktreePath(repository, branch.Name),
            MinWidth = 520
        };
        var content = new StackPanel { Spacing = 8 };
        content.Children.Add(new TextBox { Header = "Branch", Text = branch.Name, IsReadOnly = true });
        content.Children.Add(directory);

        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = "Open in New Worktree",
            Content = content,
            PrimaryButtonText = "Create and Open",
            SecondaryButtonText = "Create",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary
        };
        var result = await dialog.ShowAsync();
        if (result is not ContentDialogResult.Primary and not ContentDialogResult.Secondary) return;

        var operation = await _worktreesViewModel.CreateFromBranchAsync(
            repository,
            directory.Text,
            branch);
        if (!operation.Succeeded)
        {
            await ShowWorktreeOperationErrorAsync(operation, "Could not create worktree");
            return;
        }

        if (result == ContentDialogResult.Primary && operation.WorktreePath is { } path)
            OpenWorktreeInNewInstance(path);
    }

    private async Task CreateNewWorktreeAsync()
    {
        var repository = _viewModel.Repository;
        if (repository is null || !_worktreesViewModel.CanMutate) return;

        var branch = new TextBox { Header = "New branch", PlaceholderText = "feature/new-api", MinWidth = 520 };
        var startPoint = new TextBox
        {
            Header = "Start from",
            Text = _viewModel.LocalBranches.FirstOrDefault(item => item.IsCurrent)?.Name ?? "HEAD"
        };
        var directory = new TextBox
        {
            Header = "Directory",
            Text = SuggestWorktreePath(repository, "new-worktree")
        };
        var autoDirectory = true;
        var updatingDirectory = false;
        branch.TextChanged += (_, _) =>
        {
            if (!autoDirectory || string.IsNullOrWhiteSpace(branch.Text)) return;
            updatingDirectory = true;
            directory.Text = SuggestWorktreePath(repository, branch.Text.Trim());
            updatingDirectory = false;
        };
        directory.TextChanged += (_, _) =>
        {
            if (!updatingDirectory) autoDirectory = false;
        };

        var content = new StackPanel { Spacing = 8 };
        content.Children.Add(branch);
        content.Children.Add(startPoint);
        content.Children.Add(directory);

        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = "New Worktree",
            Content = content,
            PrimaryButtonText = "Create and Open",
            SecondaryButtonText = "Create",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary
        };
        var result = await dialog.ShowAsync();
        if (result is not ContentDialogResult.Primary and not ContentDialogResult.Secondary) return;

        var operation = await _worktreesViewModel.CreateNewBranchAsync(
            repository,
            directory.Text,
            branch.Text,
            startPoint.Text);
        if (!operation.Succeeded)
        {
            await ShowWorktreeOperationErrorAsync(operation, "Could not create worktree");
            return;
        }

        if (result == ContentDialogResult.Primary && operation.WorktreePath is { } path)
            OpenWorktreeInNewInstance(path);
    }

    private void PopulateWorktreeMenu(MenuFlyout flyout, WorktreeInfo worktree)
    {
        if (!worktree.IsCurrent)
            AddMenuItem(flyout, "Open", !_worktreesViewModel.IsBusy, () => OpenWorktreeAsync(worktree));
        AddMenuItem(
            flyout,
            _desktopShellService.OpenFolderDescription,
            !_worktreesViewModel.IsBusy,
            () => OpenWorktreeFolderAsync(worktree));
        AddMenuItem(
            flyout,
            "Copy worktree path",
            true,
            () => CopyTextAsync(WorktreePresentation.GetPathForCopy(worktree)));
        if (WorktreePresentation.GetBranchNameForCopy(worktree) is { } branch)
            AddMenuItem(flyout, "Copy branch name", true, () => CopyTextAsync(branch));
        flyout.Items.Add(new MenuFlyoutSeparator());

        if (worktree.IsLocked)
            AddMenuItem(flyout, "Unlock", _worktreesViewModel.CanUnlock(worktree), () => UnlockWorktreeAsync(worktree));
        else
            AddMenuItem(flyout, "Lock…", _worktreesViewModel.CanLock(worktree), () => LockWorktreeAsync(worktree));

        if (!worktree.IsPrimary && !worktree.IsCurrent && !worktree.IsLocked)
        {
            flyout.Items.Add(new MenuFlyoutSeparator());
            AddMenuItem(
                flyout,
                "Remove Worktree",
                _worktreesViewModel.CanRemove(worktree),
                () => RemoveWorktreeAsync(worktree));
        }

        flyout.Items.Add(new MenuFlyoutSeparator());
        AddMenuItem(flyout, "Prune Worktrees", _worktreesViewModel.CanMutate, PruneWorktreesAsync);
    }

    private Task OpenWorktreeAsync(WorktreeInfo worktree)
    {
        OpenWorktreeInNewInstance(worktree.Path);
        return Task.CompletedTask;
    }

    private Task OpenWorktreeFolderAsync(WorktreeInfo worktree) =>
        OpenFolderInDesktopShellAsync(worktree.Path, "Could not open worktree folder");

    private async Task LockWorktreeAsync(WorktreeInfo worktree)
    {
        var repository = _viewModel.Repository;
        if (repository is null || !_worktreesViewModel.CanLock(worktree)) return;

        var reason = new TextBox { Header = "Reason (optional)", MinWidth = 420 };
        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = "Lock worktree",
            Content = reason,
            PrimaryButtonText = "Lock",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary
        };
        if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;

        var operation = await _worktreesViewModel.LockAsync(repository, worktree, reason.Text);
        await ShowWorktreeOperationErrorAsync(operation, "Could not lock worktree");
    }

    private async Task UnlockWorktreeAsync(WorktreeInfo worktree)
    {
        var repository = _viewModel.Repository;
        if (repository is null || !_worktreesViewModel.CanUnlock(worktree)) return;

        var operation = await _worktreesViewModel.UnlockAsync(repository, worktree);
        await ShowWorktreeOperationErrorAsync(operation, "Could not unlock worktree");
    }

    private async Task RemoveWorktreeAsync(WorktreeInfo worktree)
    {
        var repository = _viewModel.Repository;
        if (repository is null || !_worktreesViewModel.CanRemove(worktree)) return;

        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = "Remove worktree?",
            Content = $"Remove worktree '{worktree.Path}'?",
            PrimaryButtonText = "Remove",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Close
        };
        if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;

        var operation = await _worktreesViewModel.RemoveAsync(repository, worktree);
        if (operation.Succeeded || operation.Canceled) return;

        var forceDialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = "Worktree could not be removed",
            Content = $"{operation.ErrorMessage}\n\nForce removal can discard changes in that worktree.",
            PrimaryButtonText = "Force Remove",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Close
        };
        if (await forceDialog.ShowAsync() != ContentDialogResult.Primary) return;

        var forced = await _worktreesViewModel.RemoveAsync(repository, worktree, force: true);
        await ShowWorktreeOperationErrorAsync(forced, "Could not force remove worktree");
    }

    private async Task PruneWorktreesAsync()
    {
        var repository = _viewModel.Repository;
        if (repository is null || !_worktreesViewModel.CanMutate) return;

        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = "Prune worktrees?",
            Content = "Remove stale worktree records?",
            PrimaryButtonText = "Prune",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Close
        };
        if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;

        var operation = await _worktreesViewModel.PruneAsync(repository);
        await ShowWorktreeOperationErrorAsync(operation, "Could not prune worktrees");
    }

    private Task ShowWorktreeOperationErrorAsync(
        WorktreeOperationResult operation,
        string fallbackTitle)
    {
        if (!operation.Failed || string.IsNullOrWhiteSpace(operation.ErrorMessage))
            return Task.CompletedTask;

        return ShowErrorAsync(
            operation.ErrorTitle ?? fallbackTitle,
            operation.ErrorMessage);
    }

    private static string SuggestWorktreePath(Repository repository, string branch)
    {
        var basePath = Path.TrimEndingDirectorySeparator(Path.GetFullPath(repository.WorkingDirectory));
        var parent = Directory.GetParent(basePath)?.FullName ?? basePath;
        var repositoryName = Path.GetFileName(basePath);
        if (string.IsNullOrWhiteSpace(repositoryName)) repositoryName = "repository";
        return Path.Combine(parent, $"{repositoryName}-worktrees", ToSafeDirectoryName(branch));
    }

    private static string ToSafeDirectoryName(string value)
    {
        var invalid = Path.GetInvalidFileNameChars().ToHashSet();
        invalid.Add('/');
        invalid.Add('\\');
        invalid.Add(':');
        var builder = new StringBuilder(value.Length);
        var lastWasDash = false;
        foreach (var character in value.Trim())
        {
            var replacement = invalid.Contains(character) ? '-' : character;
            if (replacement == '-' && lastWasDash) continue;
            builder.Append(replacement);
            lastWasDash = replacement == '-';
        }
        var result = builder.ToString().Trim(' ', '.', '-');
        return result.Length == 0 ? "worktree" : result;
    }

    private static void OpenWorktreeInNewInstance(string path)
    {
        var fullPath = Path.GetFullPath(path);
        if (!Directory.Exists(fullPath))
            throw new DirectoryNotFoundException($"The worktree path no longer exists: {fullPath}");
        var executable = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(executable))
            throw new InvalidOperationException("CSharpGit executable path could not be determined.");
        StartProcess(executable, fullPath);
    }

    private static void StartProcess(string executable, params string[] arguments)
    {
        var startInfo = new ProcessStartInfo(executable)
        {
            UseShellExecute = false,
            CreateNoWindow = true
        };
        foreach (var argument in arguments) startInfo.ArgumentList.Add(argument);
        Process.Start(startInfo);
    }
}
