using System.ComponentModel;
using System.Diagnostics;
using System.Text;
using CSharpGit.Application.Abstractions;
using CSharpGit.Domain;
using CSharpGit.Presentation.ViewModels;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace CSharpGit.Presentation;

public sealed partial class MainPage
{
    private IWorktreeService? _worktreeService;
    private IReadOnlyList<WorktreeInfo> _worktrees = [];
    private bool _worktreeRefreshQueued;

    internal void InitializeWorktreeSupport(IWorktreeService worktreeService)
    {
        _worktreeService = worktreeService ?? throw new ArgumentNullException(nameof(worktreeService));
        _viewModel.PropertyChanged += WorktreeViewModel_PropertyChanged;
        _viewModel.LocalBranches.CollectionChanged += (_, _) => QueueWorktreeRefresh();
        _viewModel.RemoteBranches.CollectionChanged += (_, _) => QueueWorktreeRefresh();
        _viewModel.Remotes.CollectionChanged += (_, _) => QueueWorktreeRefresh();
        _viewModel.Tags.CollectionChanged += (_, _) => QueueWorktreeRefresh();
        _viewModel.Stashes.CollectionChanged += (_, _) => QueueWorktreeRefresh();
        QueueWorktreeRefresh();
    }

    internal Task OpenInitialRepositoryAsync() => _viewModel.OpenRepositoryAsyncForDesktopCheck();

    private void WorktreeViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName is nameof(OpenRepositoryViewModel.Repository)
            or nameof(OpenRepositoryViewModel.HeadDisplay)
            or nameof(OpenRepositoryViewModel.CurrentOperation))
            QueueWorktreeRefresh();
    }

    private void QueueWorktreeRefresh()
    {
        if (_worktreeService is null || _worktreeRefreshQueued) return;
        _worktreeRefreshQueued = true;
        DispatcherQueue.TryEnqueue(DispatcherQueuePriority.Low, async () =>
        {
            _worktreeRefreshQueued = false;
            await RefreshWorktreePresentationAsync();
        });
    }

    private async Task RefreshWorktreePresentationAsync()
    {
        if (_worktreeService is null) return;
        var repository = _viewModel.Repository;
        if (repository is null)
        {
            _worktrees = [];
            RemoveWorktreeRoot();
            return;
        }

        try
        {
            var worktrees = await _worktreeService.ListAsync(repository);
            if (!ReferenceEquals(repository, _viewModel.Repository)) return;
            _worktrees = worktrees;
            InstallWorktreeRoot();
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception exception)
        {
            Debug.WriteLine($"Could not refresh worktrees: {exception}");
        }
    }

    private void InstallWorktreeRoot()
    {
        RemoveWorktreeRoot();
        if (_viewModel.Repository is null) return;

        var children = _worktrees
            .OrderByDescending(worktree => worktree.IsCurrent)
            .ThenBy(worktree => worktree.Branch ?? worktree.Head, StringComparer.OrdinalIgnoreCase)
            .Select(worktree => new RepositoryTreeNode(
                RepositoryTreeNodeKind.Worktree,
                worktree.Branch ?? ShortHead(worktree.Head),
                value: worktree,
                isCurrent: worktree.IsCurrent))
            .ToList();

        _repositoryTreeRoots.Insert(0, new RepositoryTreeNode(
            RepositoryTreeNodeKind.Group,
            "Worktrees",
            isExpanded: true,
            children: children));
        ApplyBranchWorktreeIndicators();
    }

    private void RemoveWorktreeRoot()
    {
        for (var index = _repositoryTreeRoots.Count - 1; index >= 0; index--)
        {
            if (_repositoryTreeRoots[index] is { Kind: RepositoryTreeNodeKind.Group, Name: "Worktrees" })
                _repositoryTreeRoots.RemoveAt(index);
        }
    }

    private void ApplyBranchWorktreeIndicators()
    {
        var byBranch = _worktrees
            .Where(worktree => !string.IsNullOrWhiteSpace(worktree.Branch))
            .GroupBy(worktree => worktree.Branch!, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.First().Path, StringComparer.Ordinal);

        foreach (var root in _repositoryTreeRoots)
            ApplyBranchWorktreeIndicators(root, byBranch);
    }

    private static void ApplyBranchWorktreeIndicators(
        RepositoryTreeNode node,
        IReadOnlyDictionary<string, string> worktreesByBranch)
    {
        if (node.Kind == RepositoryTreeNodeKind.LocalBranch && node.ReferenceName is { } branch)
            node.SetAssociatedWorktreePath(worktreesByBranch.GetValueOrDefault(branch));

        foreach (var child in node.Children)
            ApplyBranchWorktreeIndicators(child, worktreesByBranch);
    }

    private WorktreeInfo? FindWorktreeForBranch(string branch) =>
        _worktrees.FirstOrDefault(worktree =>
            string.Equals(worktree.Branch, branch, StringComparison.Ordinal));

    private async Task CreateWorktreeFromBranchAsync(GitBranch branch)
    {
        if (_worktreeService is null || _viewModel.Repository is null || _viewModel.IsBusy) return;
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
            Text = SuggestWorktreePath(_viewModel.Repository, branch.Name),
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

        try
        {
            var path = NormalizeRequestedWorktreePath(_viewModel.Repository, directory.Text);
            await _worktreeService.AddAsync(_viewModel.Repository, path, branch.Name);
            await RefreshAfterWorktreeMutationAsync();
            if (result == ContentDialogResult.Primary) OpenWorktreeInNewInstance(path);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            await ShowErrorAsync("Could not create worktree", FormatWorktreeError(exception));
        }
    }

    private async Task CreateNewWorktreeAsync()
    {
        if (_worktreeService is null || _viewModel.Repository is null || _viewModel.IsBusy) return;

        var repository = _viewModel.Repository;
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
        if (string.IsNullOrWhiteSpace(branch.Text) || string.IsNullOrWhiteSpace(startPoint.Text))
        {
            await ShowErrorAsync("Could not create worktree", "New branch and start point are required.");
            return;
        }

        try
        {
            var path = NormalizeRequestedWorktreePath(repository, directory.Text);
            await _worktreeService.AddNewBranchAsync(
                repository,
                path,
                branch.Text.Trim(),
                startPoint.Text.Trim());
            await RefreshAfterWorktreeMutationAsync();
            if (result == ContentDialogResult.Primary) OpenWorktreeInNewInstance(path);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            await ShowErrorAsync("Could not create worktree", FormatWorktreeError(exception));
        }
    }

    private void PopulateWorktreeMenu(MenuFlyout flyout, WorktreeInfo worktree)
    {
        if (!worktree.IsCurrent)
            AddMenuItem(flyout, "Open", !_viewModel.IsBusy, () => OpenWorktreeAsync(worktree));
        AddMenuItem(flyout, "Open Folder", !_viewModel.IsBusy, () => OpenWorktreeFolderAsync(worktree));
        flyout.Items.Add(new MenuFlyoutSeparator());

        if (worktree.IsLocked)
            AddMenuItem(flyout, "Unlock", !_viewModel.IsBusy, () => UnlockWorktreeAsync(worktree));
        else
            AddMenuItem(flyout, "Lock…", !_viewModel.IsBusy, () => LockWorktreeAsync(worktree));

        if (!worktree.IsCurrent && !worktree.IsLocked)
        {
            flyout.Items.Add(new MenuFlyoutSeparator());
            AddMenuItem(flyout, "Remove Worktree", !_viewModel.IsBusy, () => RemoveWorktreeAsync(worktree));
        }

        flyout.Items.Add(new MenuFlyoutSeparator());
        AddMenuItem(flyout, "Prune Worktrees", !_viewModel.IsBusy, PruneWorktreesAsync);
    }

    private Task OpenWorktreeAsync(WorktreeInfo worktree)
    {
        OpenWorktreeInNewInstance(worktree.Path);
        return Task.CompletedTask;
    }

    private async Task OpenWorktreeFolderAsync(WorktreeInfo worktree)
    {
        try
        {
            OpenFolder(worktree.Path);
        }
        catch (Exception exception)
        {
            await ShowErrorAsync("Could not open worktree folder", FormatWorktreeError(exception));
        }
    }

    private async Task LockWorktreeAsync(WorktreeInfo worktree)
    {
        if (_worktreeService is null || _viewModel.Repository is null) return;
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
        try
        {
            await _worktreeService.LockAsync(
                _viewModel.Repository,
                worktree,
                string.IsNullOrWhiteSpace(reason.Text) ? null : reason.Text.Trim());
            await RefreshAfterWorktreeMutationAsync();
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            await ShowErrorAsync("Could not lock worktree", FormatWorktreeError(exception));
        }
    }

    private async Task UnlockWorktreeAsync(WorktreeInfo worktree)
    {
        if (_worktreeService is null || _viewModel.Repository is null) return;
        try
        {
            await _worktreeService.UnlockAsync(_viewModel.Repository, worktree);
            await RefreshAfterWorktreeMutationAsync();
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            await ShowErrorAsync("Could not unlock worktree", FormatWorktreeError(exception));
        }
    }

    private async Task RemoveWorktreeAsync(WorktreeInfo worktree)
    {
        if (_worktreeService is null || _viewModel.Repository is null || worktree.IsCurrent) return;
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

        try
        {
            await _worktreeService.RemoveAsync(_viewModel.Repository, worktree);
            await RefreshAfterWorktreeMutationAsync();
            return;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            var forceDialog = new ContentDialog
            {
                XamlRoot = XamlRoot,
                Title = "Worktree could not be removed",
                Content = $"{FormatWorktreeError(exception)}\n\nForce removal can discard changes in that worktree.",
                PrimaryButtonText = "Force Remove",
                CloseButtonText = "Cancel",
                DefaultButton = ContentDialogButton.Close
            };
            if (await forceDialog.ShowAsync() != ContentDialogResult.Primary) return;
        }

        try
        {
            await _worktreeService.RemoveAsync(_viewModel.Repository, worktree, force: true);
            await RefreshAfterWorktreeMutationAsync();
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            await ShowErrorAsync("Could not force remove worktree", FormatWorktreeError(exception));
        }
    }

    private async Task PruneWorktreesAsync()
    {
        if (_worktreeService is null || _viewModel.Repository is null) return;
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
        try
        {
            await _worktreeService.PruneAsync(_viewModel.Repository);
            await RefreshAfterWorktreeMutationAsync();
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            await ShowErrorAsync("Could not prune worktrees", FormatWorktreeError(exception));
        }
    }

    private async Task RefreshAfterWorktreeMutationAsync()
    {
        await _viewModel.RefreshAsyncForDesktopCheck();
        await RefreshWorktreePresentationAsync();
    }

    private static string NormalizeRequestedWorktreePath(Repository repository, string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        return Path.IsPathRooted(path)
            ? Path.GetFullPath(path)
            : Path.GetFullPath(path, repository.WorkingDirectory);
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

    private static string ShortHead(string head) =>
        string.IsNullOrWhiteSpace(head) ? "unknown" : head[..Math.Min(8, head.Length)];

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

    private static void OpenFolder(string path)
    {
        var fullPath = Path.GetFullPath(path);
        if (!Directory.Exists(fullPath))
            throw new DirectoryNotFoundException($"The worktree path no longer exists: {fullPath}");
        if (OperatingSystem.IsWindows()) StartProcess("explorer.exe", fullPath);
        else if (OperatingSystem.IsMacOS()) StartProcess("open", fullPath);
        else StartProcess("xdg-open", fullPath);
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

    private static string FormatWorktreeError(Exception exception)
    {
        var message = exception.Message;
        var lower = message.ToLowerInvariant();
        if (lower.Contains("already checked out", StringComparison.Ordinal))
            return "The branch is already checked out in another worktree.\n\n" + message;
        if (lower.Contains("already exists", StringComparison.Ordinal) || lower.Contains("destination", StringComparison.Ordinal) && lower.Contains("exists", StringComparison.Ordinal))
            return "The destination already exists. Choose another directory.\n\n" + message;
        if (lower.Contains("contains modified or untracked", StringComparison.Ordinal) || lower.Contains("is dirty", StringComparison.Ordinal))
            return "The worktree contains modified or untracked files. Use Force Remove only if those changes may be discarded.\n\n" + message;
        if (lower.Contains("locked", StringComparison.Ordinal))
            return "The worktree is locked. Unlock it before removing it, or use an explicit force operation.\n\n" + message;
        if (lower.Contains("not a valid object name", StringComparison.Ordinal) || lower.Contains("unknown revision", StringComparison.Ordinal))
            return "The branch or start point does not exist.\n\n" + message;
        if (lower.Contains("not a working tree", StringComparison.Ordinal) || lower.Contains("no such file", StringComparison.Ordinal))
            return "The worktree path no longer exists.\n\n" + message;
        if (lower.Contains("not a git command", StringComparison.Ordinal) || lower.Contains("unknown subcommand", StringComparison.Ordinal))
            return "The installed Git version does not support the required worktree operation.\n\n" + message;
        return message;
    }
}
