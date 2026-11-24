using System.Diagnostics;
using CSharpGit.Application.Abstractions;
using CSharpGit.Domain;
using CSharpGit.Presentation.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace CSharpGit.Presentation.Workflows;

// Owns historical submodule menus, dialogs and operation lifetime.
internal sealed class SubmoduleWorkflowController(
    ISubmoduleService submoduleService,
    OpenRepositoryViewModel viewModel,
    RepositoryFilesViewModel repositoryFilesViewModel,
    Func<XamlRoot?> xamlRoot,
    Func<ContentDialog, Task<ContentDialogResult>> showDialog,
    Func<bool> isShuttingDown,
    Func<string, string, Task> showError,
    Action<Func<Task>, string> runUiOperation,
    Action<MenuFlyout, string, bool, Func<Task>> addMenuItem,
    Func<string, Task> copyText)
{
    private readonly ISubmoduleService _submoduleService = submoduleService;
    private readonly OpenRepositoryViewModel _viewModel = viewModel;
    private readonly RepositoryFilesViewModel _repositoryFilesViewModel = repositoryFilesViewModel;
    private readonly Func<XamlRoot?> _xamlRoot = xamlRoot;
    private readonly Func<ContentDialog, Task<ContentDialogResult>> _showDialog = showDialog;
    private readonly Func<bool> _isShuttingDown = isShuttingDown;
    private readonly Func<string, string, Task> _showError = showError;
    private readonly Action<Func<Task>, string> _runUiOperation = runUiOperation;
    private readonly Action<MenuFlyout, string, bool, Func<Task>> _addMenuItem = addMenuItem;
    private readonly Func<string, Task> _copyText = copyText;

    private async Task<(Repository Repository, SubmoduleInfo? Info)?> FindSubmoduleAsync(
        RepositorySnapshotEntry entry)
    {
        var repository = _repositoryFilesViewModel.CurrentRepository;
        if (repository is null || !ReferenceEquals(repository, _viewModel.Repository)) return null;
        var infos = await _submoduleService.ReadAsync(repository);
        if (_isShuttingDown() || !ReferenceEquals(repository, _viewModel.Repository)) return null;
        return (repository, infos.FirstOrDefault(info =>
            string.Equals(info.Path, entry.Path, StringComparison.Ordinal)));
    }

    internal async Task OpenSelectedSubmoduleAsync(RepositorySnapshotEntry entry)
    {
        try
        {
            var selected = await FindSubmoduleAsync(entry);
            if (selected is not { } item) return;
            if (item.Info is not { CanOpen: true } info)
            {
                await ShowSubmoduleInfoAsync(entry, item.Info);
                return;
            }

            var folder = Path.GetFullPath(Path.Combine(item.Repository.WorkingDirectory,
                info.Path.Replace('/', Path.DirectorySeparatorChar)));
            if (!Directory.Exists(folder))
                throw new InvalidOperationException("Submodule directory is unavailable.");
            var executable = Environment.ProcessPath
                ?? throw new InvalidOperationException("Application executable path is unavailable.");
            var start = new ProcessStartInfo(executable) { UseShellExecute = false };
            if (string.Equals(Path.GetFileNameWithoutExtension(executable), "dotnet",
                    StringComparison.OrdinalIgnoreCase))
            {
                var assembly = System.Reflection.Assembly.GetEntryAssembly()?.Location;
                if (string.IsNullOrWhiteSpace(assembly))
                    throw new InvalidOperationException("Application entry assembly is unavailable.");
                start.ArgumentList.Add(assembly);
            }
            start.ArgumentList.Add(folder);
            using var process = Process.Start(start)
                ?? throw new InvalidOperationException("Could not start another CSharpGit process.");
        }
        catch (OperationCanceledException) { }
        catch (Exception exception) { await _showError("Could not open submodule", exception.Message); }
    }

    internal async Task ShowSubmoduleMenuAsync(FrameworkElement target, RepositorySnapshotEntry entry)
    {
        try
        {
            var selected = await FindSubmoduleAsync(entry);
            if (selected is not { } item) return;
            var repository = item.Repository;
            var info = item.Info;
            var flyout = new MenuFlyout();
            _addMenuItem(flyout, "Information", true, () => ShowSubmoduleInfoAsync(entry, info));
            flyout.Items.Add(new MenuFlyoutSeparator());
            _addMenuItem(flyout, "Open Repository", info is { CanOpen: true },
                () => OpenSelectedSubmoduleAsync(entry));
            _addMenuItem(flyout, "Initialize",
                info is { IsInitialized: false, IsConflicted: false, ErrorMessage: null },
                () => RunSubmoduleOperationAsync(repository, "Initialize submodule",
                    token => _submoduleService.InitializeAsync(repository, entry.Path, token)));
            _addMenuItem(flyout, "Update to Recorded Commit",
                info is { IsInitialized: true, IsConflicted: false, ErrorMessage: null },
                () => RunSubmoduleOperationAsync(repository, "Update submodule",
                    token => _submoduleService.UpdateAsync(repository, entry.Path, token)));
            _addMenuItem(flyout, "Update All Submodules", true,
                () => RunSubmoduleOperationAsync(repository, "Update all submodules",
                    token => _submoduleService.UpdateAllAsync(repository, token)));
            flyout.Items.Add(new MenuFlyoutSeparator());
            if (!string.IsNullOrWhiteSpace(info?.Url))
                _addMenuItem(flyout, "Copy URL", true, () => _copyText(info.Url!));
            if (info is not null)
                _addMenuItem(flyout, "Copy Recorded Commit Hash", true,
                    () => _copyText(info.RecordedCommit));
            if (info is null || !string.Equals(info.RecordedCommit, entry.ObjectId, StringComparison.Ordinal))
                _addMenuItem(flyout, "Copy Historical Commit Hash", true,
                    () => _copyText(entry.ObjectId));
            flyout.ShowAt(target);
        }
        catch (OperationCanceledException) { }
        catch (Exception exception) { await _showError("Could not read submodule", exception.Message); }
    }

    private async Task ShowSubmoduleInfoAsync(RepositorySnapshotEntry entry, SubmoduleInfo? info)
    {
        var details = string.Join(Environment.NewLine, new[]
        {
            $"Path: {entry.Path}",
            $"URL from .gitmodules: {info?.Url ?? "unavailable"}",
            $"Historical snapshot commit: {entry.ObjectId}",
            $"Parent index commit: {info?.RecordedCommit ?? "not present"}",
            $"Current checkout commit: {info?.CurrentCommit ?? "not initialized"}",
            $"Branch: {info?.Branch ?? (info?.IsInitialized == true ? "detached HEAD" : "—")}",
            $"Status: {info?.StatusText ?? "Not present in current index"}",
            $"Modified internal content: {(info?.ModifiedContent == true ? "yes" : "no")}",
            $"Untracked internal content: {(info?.UntrackedContent == true ? "yes" : "no")}"
        });
        var dialog = new ContentDialog
        {
            XamlRoot = _xamlRoot(),
            Title = "Submodule",
            Content = new TextBlock { Text = details, TextWrapping = TextWrapping.Wrap },
            CloseButtonText = "Close"
        };
        await _showDialog(dialog);
    }

    private async Task RunSubmoduleOperationAsync(
        Repository repository, string title, Func<CancellationToken, Task> mutation)
    {
        if (_isShuttingDown() || !ReferenceEquals(repository, _viewModel.Repository)) return;
        using var lease = _viewModel.TryBeginUserGitOperation(repository);
        if (lease is null) return;
        using var cancellation = new CancellationTokenSource();
        var ring = new ProgressRing { Width = 24, Height = 24, IsActive = true };
        var content = new StackPanel { Spacing = 12 };
        content.Children.Add(new TextBlock { Text = "Git operation in progress…", TextWrapping = TextWrapping.Wrap });
        content.Children.Add(ring);
        var dialog = new ContentDialog
        {
            XamlRoot = _xamlRoot(),
            Title = title,
            Content = content,
            CloseButtonText = "Cancel"
        };
        var started = false;
        var finished = false;
        string? failure = null;
        dialog.Loaded += (_, _) =>
        {
            if (started) return;
            started = true;
            _runUiOperation(async () =>
            {
                try { await mutation(cancellation.Token); }
                catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }
                catch (Exception exception) { failure = exception.Message; }
                finally
                {
                    finished = true;
                    ring.IsActive = false;
                    dialog.Hide();
                }
            }, title);
        };
        dialog.Closing += (_, args) =>
        {
            if (finished) return;
            cancellation.Cancel();
            args.Cancel = true;
        };
        await _showDialog(dialog);
        lease.Dispose();
        if (_isShuttingDown() || !ReferenceEquals(repository, _viewModel.Repository)) return;
        // A cancelled or failed recursive update may have changed earlier submodules.
        // Always reconcile the UI with Git, including partial success.
        try
        {
            await _viewModel.RefreshAsyncForDesktopCheck();
            await _repositoryFilesViewModel.RefreshAsync();
        }
        catch (Exception exception)
        {
            await _showError(title + " refresh failed", exception.Message);
        }
        if (failure is not null)
            await _showError(title + " failed", failure);
    }
}
